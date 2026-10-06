using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD;

// Локация из блоков 20×15 (MapConfig.Chunks) по плану нейронки (plan_location): какие блоки, где они
// относительно друг друга и что в каждом по сюжету. Геометрию всех блоков код строит сразу — локация
// конечна; нейронка наполняет блок, когда герой к нему подходит (UnpopulatedNearHero → PopulateChunk).
public static class LocationGrower
{
    // На каком расстоянии (в клетках) от границы ещё не наполненного блока он наполняется.
    public const int TriggerDistance = 4;

    public const int MaxBlocks = 8;

    // Блок плана: X — вправо (восток), Y — вверх (север), Z — этаж (0 — земля, 1+ — выше, −1… — подвалы),
    // относительно друг друга; первый — стартовый (вход снаружи, Z = 0). Terrain — тип местности блока:
    // dungeon (по умолчанию), cave, building, village или открытая (MapChunk.IsOutdoorTheme). Building — тип
    // здания (tavern, house, shop, temple, manor, barracks, tower): по нему планировка и мебель.
    public record PlannedBlock(int X, int Y, string? Purpose, string? Terrain = null, int Z = 0, string? Building = null);

    public static readonly string[] Terrains = ["dungeon", MapChunk.Cave, MapChunk.Building, MapChunk.Village, "forest", "plains", "hills", "mountain", "swamp", "desert", "snow", "taiga"];

    // Клетка карты мира, где строится локация (GameWorld.SiteAt): местность открытых блоков (по биому), река
    // через локацию (ось, ширина), стороны с водой (море/озеро — край локации водой). Нет — как задумал мастер.
    public sealed record WorldSite(string Theme, bool? RiverHorizontal, int RiverWidth, HashSet<ChunkSide> WaterSides);

    // Новая локация по плану. Прежнее содержимое карты (комнаты, существа…) сбрасывается, герой ставится
    // у входа снаружи (startInside — внутри, в первой комнате за входом). Возвращает стартовый блок или текст
    // ошибки плана (карта тогда не тронута).
    public static (MapChunk? start, string? error) CreatePlanned(WorldState ws, List<PlannedBlock> blocks, string theme = "dungeon", bool startInside = false,
        WorldSite? site = null)
    {
        if (Validate(blocks, theme) is { } error) return (null, error);

        // Этажи лежат на поле в разных местах сетки блоков: земля — в центре, остальные — в свободных клетках.
        var rng = new Random();
        if (PlaceFloors(blocks, rng) is not { } place) return (null, "этажи не помещаются на поле — уменьшите план");

        var map = ws.Map;
        map.Rooms = [];
        map.Doors = [];
        map.Area = [];
        map.Entities = [];
        map.Objects = [];
        map.Chunks = [];
        map.Stairs = null;
        map.Furniture = [];
        map.Cols = MapConfig.MaxSize;
        map.Rows = MapConfig.MaxSize;
        // Цвета карты — как у DungeonGenerator; без этого остаются чёрный фон и белые линии сетки.
        map.Colors = new ColorSetting { MapBackground = new NaviDnD.Data.DisplayConfig().MainBackground, MapForeground = [200, 190, 160] };

        var chunks = blocks.Select(b =>
        {
            var (gx, gy) = place[b];
            string t = b.Terrain ?? theme;
            // Открытая местность — как на карте мира (в горах не будет пустыни).
            if (site != null && MapChunk.IsOutdoorTheme(t)) t = site.Theme;
            return new MapChunk { X = gx, Y = gy, Z = b.Z, Seed = rng.Next(), Theme = t, Purpose = b.Purpose, Style = t == MapChunk.Building ? b.Building ?? "house" : null };
        }).ToList();
        // Соседство — по плану (x, y, z): блоки разных этажей, оказавшиеся рядом на поле, не связаны.
        var byPlan = blocks.Select((b, i) => (b, i)).ToDictionary(x => (x.b.X, x.b.Y, x.b.Z), x => chunks[x.i]);
        var planOf = chunks.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => blocks[x.i]);
        MapChunk? Neighbor(MapChunk c, ChunkSide side)
        {
            var (dx, dy) = ChunkExit.Step(side);
            var b = planOf[c];
            return byPlan.GetValueOrDefault((b.X + dx, b.Y + dy, b.Z));
        }

        foreach (var c in chunks.Where(c => c.Z > 0))
            if (byPlan.GetValueOrDefault((planOf[c].X, planOf[c].Y, 0)) is { } ground)
                (c.GroundX, c.GroundY) = (ground.X, ground.Y);

        var exits = chunks.ToDictionary(c => c, _ => new List<ChunkExit>());
        var start = chunks[0];
        var freeSides = Enum.GetValues<ChunkSide>().Where(s => Neighbor(start, s) == null).ToList();
        var startSide = freeSides[rng.Next(freeSides.Count)];
        exits[start].Add(new ChunkExit { Side = startSide, Offset = RandomOffset(startSide, rng), External = true });

        // Связи: остовное дерево по соседям своего этажа + изредка лишний проход — петля. Этажи между собой
        // связаны только лестницами — у каждого этажа своё дерево.
        var linked = new HashSet<(MapChunk, MapChunk)>();
        void Link(MapChunk a, ChunkSide side)
        {
            var b = Neighbor(a, side)!;
            int offset = RandomOffset(side, rng);
            exits[a].Add(new ChunkExit { Side = side, Offset = offset });
            exits[b].Add(new ChunkExit { Side = ChunkExit.Opposite(side), Offset = offset });
            linked.Add((a, b));
            linked.Add((b, a));
        }

        var visited = new HashSet<MapChunk>();
        foreach (var root in chunks)
        {
            if (!visited.Add(root)) continue;
            var queue = new Queue<MapChunk>([root]);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var side in Enum.GetValues<ChunkSide>().OrderBy(_ => rng.Next()))
                {
                    if (Neighbor(c, side) is not { } n || !visited.Add(n)) continue;
                    Link(c, side);
                    queue.Enqueue(n);
                }
            }
        }
        foreach (var c in chunks)
            foreach (var side in new[] { ChunkSide.Right, ChunkSide.Top })
                if (Neighbor(c, side) is { } n && !linked.Contains((c, n)) && rng.Next(4) == 0)
                    Link(c, side);

        // Один контур на всё здание: этажи одной клетки плана (x, y) — с общим seed контура.
        var footprintSeeds = new Dictionary<(int, int), int>();
        int noiseSeed = rng.Next();
        // Река с карты мира — поперёк всей местности (одна линия на поле, блоки стыкуются).
        OutdoorGenerator.RiverSpec? river = null;
        if (site?.RiverHorizontal is bool rh && chunks.Any(c => MapChunk.IsOutdoorTheme(c.Theme) && c.Z == 0))
        {
            var outdoor = chunks.Where(c => MapChunk.IsOutdoorTheme(c.Theme) && c.Z == 0).ToList();
            int center = rh ? (outdoor.Min(c => c.OriginRow) + outdoor.Max(c => c.OriginRow) + MapChunk.Rows) / 2 + rng.Next(-3, 4)
                            : (outdoor.Min(c => c.OriginCol) + outdoor.Max(c => c.OriginCol) + MapChunk.Cols) / 2 + rng.Next(-4, 5);
            river = new OutdoorGenerator.RiverSpec(rh, center, site.RiverWidth, rng.Next());
        }
        foreach (var c in chunks)
        {
            c.Exits = exits[c];
            if (c.Theme == MapChunk.Building)
            {
                var b = planOf[c];
                if (!footprintSeeds.TryGetValue((b.X, b.Y), out int fs)) footprintSeeds[(b.X, b.Y)] = fs = rng.Next();
                var seamDoors = Enum.GetValues<ChunkSide>()
                    .Where(side => Neighbor(c, side)?.Theme == MapChunk.Building && side is ChunkSide.Right or ChunkSide.Top).ToList();
                var built = BuildingGenerator.Generate(c, exits[c], fs, ground: c.Z == 0, GroundBands(c), seamDoors);
                c.Terrain = c.Z == 0 && site != null ? Reskin(built.Terrain, site.Theme) : built.Terrain;
                map.Rooms.AddRange(built.Rooms);
                map.Doors.AddRange(built.Doors);
                map.Furniture.AddRange(built.Furniture);
                map.Chunks.Add(c);
                continue;
            }
            if (c.Theme == MapChunk.Village)
            {
                var village = BuildingGenerator.GenerateVillage(c, exits[c], GroundBands(c));
                c.Terrain = site != null ? Reskin(village.Terrain, site.Theme) : village.Terrain;
                map.Rooms.AddRange(village.Rooms);
                map.Doors.AddRange(village.Doors);
                map.Furniture.AddRange(village.Furniture);
                map.Chunks.Add(c);
                continue;
            }
            if (!MapChunk.IsOutdoorTheme(c.Theme)) { AddChunk(map, c, exits[c]); continue; }

            // Края открытой местности: к соседу-местности/зданию (его двор) — открыто, к подземелью — скальная
            // стена (вход в него — проём в скале), к пустоте — естественная преграда по типу местности.
            var outdoorBands = new Dictionary<ChunkSide, char>();
            foreach (var side in Enum.GetValues<ChunkSide>())
            {
                if (Neighbor(c, side) is { } n)
                {
                    if (!MapChunk.IsOpenGround(n.Theme)) outdoorBands[side] = '^';
                }
                else outdoorBands[side] = site?.WaterSides.Contains(side) == true ? 'W'
                    : c.Theme switch { "hills" or "mountain" or "desert" => '^', "swamp" => 'W', "snow" or "taiga" => 'e', _ => 'F' };
            }
            var result = OutdoorGenerator.Generate(c, exits[c], outdoorBands, noiseSeed, c.Z == 0 ? river : null);
            c.Terrain = result.Terrain;
            map.Doors.AddRange(result.Doors);
            map.Chunks.Add(c);
        }

        // Лестницы между этажами, стоящими друг над другом в плане.
        foreach (var lower in chunks)
        {
            var b = planOf[lower];
            if (byPlan.GetValueOrDefault((b.X, b.Y, b.Z + 1)) is { } upper)
                AddStairs(map, lower, upper, rng);
        }

        var (startCol, startRow) = exits[start][0].AnchorCell(start);
        MarkWorldExit(map, startCol, startRow);
        if (ws.Hero != null)
            ws.Hero.Position = startInside && FirstRoomCell(map, start, startCol, startRow) is var (ic, ir) ? [ic, ir] : [startCol, startRow];
        return (start, null);

        // Края двора здания/поселения: к открытой земле — открыто, к подземелью — скала, без соседа — деревья и чаща.
        Dictionary<ChunkSide, char> GroundBands(MapChunk c)
        {
            var bands = new Dictionary<ChunkSide, char>();
            foreach (var side in Enum.GetValues<ChunkSide>())
            {
                var n = Neighbor(c, side);
                if (n == null) bands[side] = BuildingGenerator.NaturalBand;
                else if (!MapChunk.IsOpenGround(n.Theme)) bands[side] = '^';
            }
            return bands;
        }
    }

    // Первая клетка комнаты на пути от входа снаружи (шагами героя: внутри комнаты — к соседней клетке, между
    // комнатами/двором — только через дверь или проход) — сюда ставится герой, если игра начинается внутри.
    private static (int col, int row)? FirstRoomCell(MapConfig map, MapChunk chunk, int col, int row)
    {
        var roomOf = new Dictionary<(int, int), int>();
        for (int i = 0; i < map.Rooms!.Count; i++)
            foreach (var p in map.Rooms[i].Positions ?? [])
                if (p.Count >= 2 && chunk.Contains(p[0], p[1])) roomOf[(p[0], p[1])] = i;
        var links = new HashSet<((int, int), (int, int))>();
        foreach (var d in map.Doors ?? [])
        {
            if (d.IsWindow == true || d.From is not { Count: >= 2 } f || d.To is not { Count: >= 2 } t) continue;
            links.Add(((f[0], f[1]), (t[0], t[1])));
            links.Add(((t[0], t[1]), (f[0], f[1])));
        }
        bool Walkable((int c, int r) x) => chunk.Contains(x.c, x.r) && map.TerrainAt(x.c, x.r) is { BlocksMove: false } k && !TerrainCatalog.IsStair(k.Code);
        var start = (col, row);
        var seen = new HashSet<(int, int)> { start };
        var queue = new Queue<(int c, int r)>([start]);
        while (queue.Count > 0)
        {
            var x = queue.Dequeue();
            if (roomOf.TryGetValue(x, out int room) && map.Rooms[room].Name is not ("Коридор" or "Галерея"))
            {
                // Шаг-другой вглубь комнаты, а не на пороге.
                var deeper = new[] { (x.c + 1, x.r), (x.c - 1, x.r), (x.c, x.r + 1), (x.c, x.r - 1) }
                    .FirstOrDefault(n => roomOf.TryGetValue(n, out int rn) && rn == room && Walkable(n) && !seen.Contains(n));
                return deeper != default ? deeper : x;
            }
            foreach (var n in new[] { (x.c + 1, x.r), (x.c - 1, x.r), (x.c, x.r + 1), (x.c, x.r - 1) })
            {
                if (!Walkable(n) || seen.Contains(n)) continue;
                int ra = roomOf.GetValueOrDefault(x, -1), rb = roomOf.GetValueOrDefault(n, -1);
                if (ra != rb && !links.Contains((x, n))) continue;
                seen.Add(n);
                queue.Enqueue(n);
            }
        }
        return null;
    }

    // Запасной вариант (нейронка не прислала план): один блок.
    public static MapChunk CreateStart(WorldState ws, string theme = "dungeon") =>
        CreatePlanned(ws, [new PlannedBlock(0, 0, null)], theme).start!;

    private static string? Validate(List<PlannedBlock> blocks, string theme)
    {
        if (blocks.Count == 0) return "план пуст — нужен хотя бы один блок";
        if (blocks.Count > MaxBlocks) return $"не больше {MaxBlocks} блоков";
        if (blocks.Select(b => (b.X, b.Y, b.Z)).Distinct().Count() != blocks.Count) return "у двух блоков одинаковые x,y,z";
        if (blocks.FirstOrDefault(b => b.Terrain != null && !Terrains.Contains(b.Terrain)) is { } badTerrain)
            return $"неизвестный terrain \"{badTerrain.Terrain}\", допустимы: {string.Join(", ", Terrains)}";
        if (blocks[0].Z != 0) return "первый (стартовый) блок должен быть на земле (z = 0)";
        var planned = blocks.ToDictionary(b => (b.X, b.Y, b.Z));
        foreach (var b in blocks.Where(b => b.Z != 0))
        {
            string t = b.Terrain ?? theme;
            if (MapChunk.IsOutdoorTheme(t) || t == MapChunk.Village) return $"{t} — только на земле (z = 0)";
            if (b.Z > 0)
            {
                if (planned.GetValueOrDefault((b.X, b.Y, b.Z - 1)) is not { } below)
                    return $"под блоком ({b.X},{b.Y},{b.Z}) нужен блок ({b.X},{b.Y},{b.Z - 1}) — этажи стоят друг на друге";
                if ((below.Terrain ?? theme) != MapChunk.Building || t != MapChunk.Building)
                    return $"выше земли — только этажи здания: блоки ({b.X},{b.Y},{b.Z}) и под ним должны быть building";
            }
            if (b.Z < 0 && !planned.ContainsKey((b.X, b.Y, b.Z + 1)))
                return $"над подвалом ({b.X},{b.Y},{b.Z}) нужен блок ({b.X},{b.Y},{b.Z + 1})";
        }
        foreach (var floor in blocks.GroupBy(b => b.Z))
        {
            int w = floor.Max(b => b.X) - floor.Min(b => b.X) + 1, h = floor.Max(b => b.Y) - floor.Min(b => b.Y) + 1;
            if (w > MapChunk.GridCols || h > MapChunk.GridRows)
                return $"этаж z={floor.Key} занимает {w}×{h} блоков, максимум {MapChunk.GridCols}×{MapChunk.GridRows}";
        }

        // Связность: соседи сторонами на одном этаже + лестница между этажами одной клетки (x, y).
        var seen = new HashSet<(int, int, int)> { (blocks[0].X, blocks[0].Y, 0) };
        var queue = new Queue<(int x, int y, int z)>([(blocks[0].X, blocks[0].Y, 0)]);
        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();
            foreach (var n in new[] { (x + 1, y, z), (x - 1, y, z), (x, y + 1, z), (x, y - 1, z), (x, y, z + 1), (x, y, z - 1) })
                if (planned.ContainsKey(n) && seen.Add(n)) queue.Enqueue(n);
        }
        if (seen.Count != blocks.Count)
            return "блоки должны соединяться: сторонами на одном этаже (соседи по x или y на 1) или этажами друг над другом";
        var b0 = blocks[0];
        if (Enum.GetValues<ChunkSide>().All(s =>
            {
                var (dx, dy) = ChunkExit.Step(s);
                return planned.ContainsKey((b0.X + dx, b0.Y + dy, 0));
            }))
            return "у первого (стартового) блока нужна свободная сторона для входа снаружи";
        return null;
    }

    // Клетка сетки блоков поля для каждого блока плана. Этаж земли — в центре; каждый другой этаж — целиком
    // (с сохранением раскладки) в свободное место, по возможности не вплотную к чужим этажам.
    private static Dictionary<PlannedBlock, (int x, int y)>? PlaceFloors(List<PlannedBlock> blocks, Random rng)
    {
        var place = new Dictionary<PlannedBlock, (int x, int y)>(ReferenceEqualityComparer.Instance);
        var taken = new Dictionary<(int, int), int>(); // клетка сетки → этаж
        foreach (var floor in blocks.GroupBy(b => b.Z).OrderBy(g => Math.Abs(g.Key)).ThenByDescending(g => g.Key))
        {
            var list = floor.ToList();
            int minX = list.Min(b => b.X), minY = list.Min(b => b.Y);
            int w = list.Max(b => b.X) - minX + 1, h = list.Max(b => b.Y) - minY + 1;
            (int ox, int oy)? best = null;
            int bestScore = int.MaxValue;
            List<(int x, int y)> offsets = floor.Key == 0
                ? [((MapChunk.GridCols - w) / 2, (MapChunk.GridRows - h) / 2)]
                : Enumerable.Range(0, MapChunk.GridCols - w + 1)
                    .SelectMany(x => Enumerable.Range(0, MapChunk.GridRows - h + 1).Select(y => (x, y)))
                    .OrderBy(_ => rng.Next()).ToList();
            foreach (var (ox, oy) in offsets)
            {
                var cells = list.Select(b => (b.X - minX + ox, b.Y - minY + oy)).ToList();
                if (cells.Any(taken.ContainsKey)) continue;
                // Чем меньше соседей с других этажей (включая диагональ), тем лучше. Край поля — хуже: камера там
                // упирается в границу, и при переходе по лестнице карта сдвигалась бы.
                int score = cells.Sum(c => taken.Count(t => t.Value != floor.Key
                    && Math.Abs(t.Key.Item1 - c.Item1) <= 1 && Math.Abs(t.Key.Item2 - c.Item2) <= 1))
                    + cells.Count(c => c.Item1 == 0 || c.Item1 == MapChunk.GridCols - 1 || c.Item2 == 0 || c.Item2 == MapChunk.GridRows - 1) * 10;
                if (score < bestScore) { bestScore = score; best = (ox, oy); }
            }
            if (best is not var (bx, by)) return null;
            foreach (var b in list)
            {
                var cell = (b.X - minX + bx, b.Y - minY + by);
                place[b] = cell;
                taken[cell] = floor.Key;
            }
        }
        return place;
    }

    private static readonly string[] Passages = ["Коридор", "Лаз", "Галерея"];

    // Лестница: клетка нижнего блока «вверх» (U) ↔ клетка верхнего «вниз» (D). Клетка — проходимая, у стены,
    // не у двери/выхода и не перерезает комнату (шаг на неё переносит героя — пройти сквозь неё нельзя).
    // Лучше — в главном зале/коридоре (вес клетки — размер её комнаты, у коридора — наибольший), по возможности
    // на одном месте в обоих блоках (этажи здания стоят друг над другом).
    private static void AddStairs(MapConfig map, MapChunk lower, MapChunk upper, Random rng)
    {
        var lowerCells = StairCandidates(map, lower);
        var upperCells = StairCandidates(map, upper);
        if (lowerCells.Count == 0 || upperCells.Count == 0) return;
        double Jitter() => rng.NextDouble() * 4;
        var common = lowerCells.Keys.Where(upperCells.ContainsKey)
            .Select(x => (x, score: lowerCells[x] + upperCells[x] + Jitter())).OrderByDescending(p => p.score).FirstOrDefault();
        var lowerBest = lowerCells.MaxBy(p => p.Value + Jitter());
        var upperBest = upperCells.MaxBy(p => p.Value + Jitter());
        bool aligned = common.score > 0; // одна клетка на обоих этажах — лестница стоит «друг над другом»
        var (lc, lr) = aligned ? common.x : lowerBest.Key;
        var (uc, ur) = aligned ? common.x : upperBest.Key;
        SetTerrain(lower, lc, lr, 'U');
        SetTerrain(upper, uc, ur, 'D');
        (map.Stairs ??= []).Add(new StairLink
        {
            A = [lower.OriginCol + lc + 1, lower.OriginRow + lr + 1],
            B = [upper.OriginCol + uc + 1, upper.OriginRow + ur + 1],
        });
    }

    // Подходящие для лестницы клетки блока (локальные координаты) → вес (чем больше, тем уместнее).
    private static Dictionary<(int c, int r), int> StairCandidates(MapConfig map, MapChunk chunk)
    {
        // Клетка → область: индекс комнаты, −1 — открытая местность вне комнат.
        var region = new Dictionary<(int c, int r), int>();
        var inAnyRoom = new HashSet<(int, int)>();
        var rooms = map.Rooms ?? [];
        for (int i = 0; i < rooms.Count; i++)
            foreach (var p in rooms[i].Positions ?? [])
            {
                if (p.Count < 2 || !chunk.Contains(p[0], p[1])) continue;
                var local = (p[0] - chunk.OriginCol - 1, p[1] - chunk.OriginRow - 1);
                inAnyRoom.Add(local);
                region[local] = i;
            }
        if (chunk.Terrain is { } rows)
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    if (rows[r][c] != ' ' && !inAnyRoom.Contains((c, r)))
                        region[(c, r)] = -1;

        var avoid = new HashSet<(int, int)>();
        void Avoid(int col, int row, int radius)
        {
            for (int dc = -radius; dc <= radius; dc++)
                for (int dr = -radius; dr <= radius; dr++)
                    avoid.Add((col - chunk.OriginCol - 1 + dc, row - chunk.OriginRow - 1 + dr));
        }
        foreach (var d in map.Doors ?? [])
            if (d.IsWindow != true)
                foreach (var p in new[] { d.From, d.To })
                    if (p is { Count: >= 2 } && chunk.Contains(p[0], p[1])) Avoid(p[0], p[1], 1);
        foreach (var e in chunk.Exits)
        {
            var (ac, ar) = e.AnchorCell(chunk);
            Avoid(ac, ar, 2);
        }
        // Средний этаж: лестница вниз уже стоит — вверх ставим не рядом с ней.
        for (int r = 0; r < MapChunk.Rows; r++)
            for (int c = 0; c < MapChunk.Cols; c++)
                if (TerrainCatalog.IsStair(TerrainChar(chunk, c, r))) Avoid(chunk.OriginCol + c + 1, chunk.OriginRow + r + 1, 1);

        bool Free((int c, int r) x) => region.ContainsKey(x) && map.TerrainAt(chunk.OriginCol + x.c + 1, chunk.OriginRow + x.r + 1) is not { BlocksMove: true };
        (int, int)[] Around((int c, int r) x) => [(x.c + 1, x.r), (x.c - 1, x.r), (x.c, x.r + 1), (x.c, x.r - 1)];

        // В комнате — у стены; на открытой местности — где угодно, кроме тропы. Есть комнаты (здание с двором) —
        // лестница в доме, а не во дворе.
        // Строго: не у дверей/выходов/другой лестницы; мягко — только не на самой двери и не на лестнице.
        var exact = new HashSet<(int, int)>();
        foreach (var d in map.Doors ?? [])
            if (d.IsWindow != true)
                foreach (var p in new[] { d.From, d.To })
                    if (p is { Count: >= 2 }) exact.Add((p[0] - chunk.OriginCol - 1, p[1] - chunk.OriginRow - 1));
        bool strict = true;
        // В коридоре двери через каждые 3–4 клетки — там обходим только сами клетки дверей.
        bool InPassage((int c, int r) x) => region[x] >= 0 && Passages.Contains(rooms[region[x]].Name);
        bool Avoided((int c, int r) x) => strict && !InPassage(x) ? avoid.Contains(x) : exact.Contains(x) || TerrainCatalog.IsStair(TerrainChar(chunk, x.c, x.r));
        List<(int c, int r)> Candidates(bool byWall) => region.Keys.Where(x => Free(x) && !Avoided(x) && TerrainChar(chunk, x.c, x.r) != '='
                && (!byWall || region[x] < 0 || Around(x).Any(n => !region.TryGetValue(n, out int rn) || rn != region[x]))
                && !CutsRegion(x, region, Free))
            .OrderBy(x => x).ToList();
        // У стены не нашлось (тесный этаж башни со второй лестницей) — где угодно, лишь бы не перерезать комнату.
        var list = Candidates(byWall: true);
        if (list.Count == 0) list = Candidates(byWall: false);
        if (list.Count == 0) { strict = false; list = Candidates(byWall: false); }
        if (list.Any(x => region[x] >= 0)) list = list.Where(x => region[x] >= 0).ToList();
        int Weight((int c, int r) x) => region[x] < 0 ? 1 : Passages.Contains(rooms[region[x]].Name) ? 100 : rooms[region[x]].Positions?.Count ?? 1;
        return list.ToDictionary(x => x, Weight);
    }

    // Клетка x — «перешеек»: без неё её область (комната / открытая местность) распадается.
    private static bool CutsRegion((int c, int r) x, Dictionary<(int c, int r), int> region, Func<(int c, int r), bool> free)
    {
        int id = region[x];
        var cells = region.Where(p => p.Value == id && p.Key != x && free(p.Key)).Select(p => p.Key).ToHashSet();
        if (cells.Count < 4) return true;
        var startCell = cells.First();
        var seen = new HashSet<(int, int)> { startCell };
        var queue = new Queue<(int c, int r)>([startCell]);
        while (queue.Count > 0)
        {
            var (c, r) = queue.Dequeue();
            foreach (var n in new[] { (c + 1, r), (c - 1, r), (c, r + 1), (c, r - 1) })
                if (cells.Contains(n) && seen.Add(n)) queue.Enqueue(n);
        }
        return seen.Count != cells.Count;
    }

    private static char TerrainChar(MapChunk chunk, int c, int r) =>
        chunk.Terrain is { } t && r < t.Count && c < t[r].Length ? t[r][c] : ' ';

    private static void SetTerrain(MapChunk chunk, int c, int r, char code)
    {
        chunk.Terrain ??= [.. Enumerable.Repeat(new string(' ', MapChunk.Cols), MapChunk.Rows)];
        var line = chunk.Terrain[r].ToCharArray();
        line[c] = code;
        chunk.Terrain[r] = new string(line);
    }

    // Двор здания и улицы деревни — земля по клетке мира: в пустыне песок и колючки, в снегах снег и ели
    // (полы домов, мебель, ограды не трогаются — у них свои коды).
    private static List<string> Reskin(List<string> terrain, string theme)
    {
        Func<char, char>? map = theme switch
        {
            "desert" => ch => ch switch { '.' or ',' => 'a', '"' => 'c', 'T' => 'c', 'F' => '^', _ => ch },
            "snow" or "taiga" => ch => ch switch { '.' or ',' or '"' => 'n', 'T' or 'F' => 'e', _ => ch },
            "mountain" => ch => ch switch { '.' => 's', _ => ch },
            _ => null,
        };
        return map == null ? terrain : terrain.Select(row => new string(row.Select(map).ToArray())).ToList();
    }

    // Клетка у входа снаружи (выход на карту мира) — сюда встаёт герой, вернувшийся в сохранённую локацию.
    public static (int col, int row)? EntranceCell(MapConfig map)
    {
        if (map.Chunks?.FirstOrDefault(c => c.Exits.Any(e => e.External)) is not { } start) return null;
        return start.Exits.First(e => e.External).AnchorCell(start);
    }

    // Цвет двери выхода из локации на карту мира — отличается от обычных дверей.
    public static readonly List<int> WorldExitColor = [160, 90, 220];

    // Вход снаружи (клетка anchor на краю блока) — дверь выхода на карту мира: закрытая, своего цвета.
    // Заодно закрывает щель в стене, за которой пустота поля.
    private static void MarkWorldExit(MapConfig map, int col, int row)
    {
        foreach (var door in map.Doors ?? [])
        {
            if (door.IsEntry != true || door.From is not { Count: >= 2 } f || f[0] != col || f[1] != row) continue;
            door.IsWorldExit = true;
            door.IsDoor = true;
            door.IsDoorOpen = false;
            door.Color = [.. WorldExitColor];
        }
    }

    // Ещё не наполненный блок, к которому герой подошёл (в TriggerDistance от его границ) — пора наполнить.
    public static MapChunk? UnpopulatedNearHero(WorldState ws)
    {
        if (ws.Map.Chunks is not { Count: > 0 } chunks || ws.Hero?.Position is not { Count: >= 2 } hp) return null;
        int floor = ws.Map.FloorAt(hp[0], hp[1]);
        return chunks.FirstOrDefault(c => !c.Populated && c.Z == floor
            && hp[0] >= c.OriginCol + 1 - TriggerDistance && hp[0] <= c.OriginCol + MapChunk.Cols + TriggerDistance
            && hp[1] >= c.OriginRow + 1 - TriggerDistance && hp[1] <= c.OriginRow + MapChunk.Rows + TriggerDistance);
    }

    // Комнаты и двери блока дописываются в конец списков — id уже существующих комнат/дверей не меняются.
    private static void AddChunk(MapConfig map, MapChunk chunk, List<ChunkExit> exits)
    {
        chunk.Exits = exits;
        var result = ChunkGenerator.Generate(chunk, exits);
        chunk.Terrain = DungeonDecorator.Decorate(chunk, result.Rooms, result.Doors);
        (map.Rooms ??= []).AddRange(result.Rooms);
        (map.Doors ??= []).AddRange(result.Doors);
        (map.Chunks ??= []).Add(chunk);
    }

    // Шов не у самого угла блока — коридоры к соседним выходам не слипаются.
    private static int RandomOffset(ChunkSide side, Random rng) => side is ChunkSide.Left or ChunkSide.Right
        ? rng.Next(3, MapChunk.Rows - 1)
        : rng.Next(3, MapChunk.Cols - 1);
}
