using NaviDnD.Data.Models;
using NaviDnD.MapGen.Models;

namespace NaviDnD.MapGen.Generators;

// Генерация блока локации целиком на стороне кода — без нейронки (быстро, бесплатно, по seed
// воспроизводимо). Код сам составляет план: сколько комнат, какие формы/размеры, в каких зонах блока,
// как соединены (остовное дерево + иногда петля), где выходы. Геометрию по плану строит DungeonGenerator.
// Нейронка потом только называет комнаты и расставляет существ/объекты (PopulateChunk/StartNewGame).
public static class ChunkGenerator
{
    // Зоны блока (как в DungeonGenerator.ZoneBounds: "north" — младшие строки сетки, "south" — старшие)
    // и их центры в сетке 20×15 — для выбора соседей и комнаты, от которой тянется коридор к выходу.
    private static readonly (string Zone, double Col, double Row)[] Zones =
    [
        ("south_west", 3.5, 12.5), ("south", 10.5, 12.5), ("south_east", 17.5, 12.5),
        ("west",       3.5,  7.5), ("center", 10.5, 7.5), ("east",       17.5,  7.5),
        ("north_west", 3.5,  2.5), ("north",  10.5, 2.5), ("north_east", 17.5,  2.5),
    ];

    public sealed record Result(List<Room> Rooms, List<Door> Doors);

    private const int MaxAttempts = 10;

    // exits — все выходы блока (входы-швы от соседей и новые). Результат — в координатах поля.
    // Генератор иногда не может проложить коридор (путь перегородила другая комната) и оставляет комнату
    // изолированной — тогда пробуем другой seed; не вышло за MaxAttempts — берём лучшую попытку и
    // вырезаем недостижимые клетки, чтобы на карте не было комнат, в которые не попасть.
    public static Result Generate(MapChunk chunk, IReadOnlyList<ChunkExit> exits)
    {
        Result? best = null;
        int bestUnreachable = int.MaxValue;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var result = GenerateOnce(chunk, exits, unchecked(chunk.Seed + attempt * 7919));
            var unreachable = UnreachableCells(chunk, exits, result);
            if (unreachable.Count == 0) return result;
            if (unreachable.Count < bestUnreachable) { best = result; bestUnreachable = unreachable.Count; }
        }
        return Prune(chunk, exits, best!);
    }

    private static Result GenerateOnce(MapChunk chunk, IReadOnlyList<ChunkExit> exits, int seed)
    {
        var rng = new Random(seed);
        // Пещера: залы неровные/округлые, лазы извилистые, дверей нет.
        bool cave = chunk.Theme == MapChunk.Cave;

        int roomCount = rng.Next(3, 6);
        var zones = Zones.OrderBy(_ => rng.Next()).Take(roomCount).ToList();
        var rooms = zones.Select((z, i) => new RoomRequest
        {
            Name = $"Комната {i + 1}",
            Zone = z.Zone,
            Size = rng.Next(10) switch { < 3 => RoomSize.Small, < 8 => RoomSize.Medium, _ => RoomSize.Large },
            Shape = cave
                ? (rng.Next(10) < 7 ? RoomShape.Irregular : RoomShape.Circular)
                : rng.Next(10) switch
                {
                    < 5 => RoomShape.Rectangular, < 7 => RoomShape.Irregular, < 9 => RoomShape.LShaped, _ => RoomShape.Circular
                },
        }).ToList();

        // Остовное дерево по близости зон (каждая новая комната — к ближайшей уже связанной) + иногда петля.
        var passages = new List<PassageRequest>();
        for (int i = 1; i < rooms.Count; i++)
        {
            int nearest = Enumerable.Range(0, i).MinBy(j => Dist(zones[i], zones[j]));
            passages.Add(Passage(rooms[i].Name, rooms[nearest].Name, rng, cave));
        }
        if (rooms.Count >= 4 && rng.Next(10) < 4)
        {
            int a = rng.Next(rooms.Count), b = rng.Next(rooms.Count);
            if (a != b && !passages.Any(p => (p.From == rooms[a].Name && p.To == rooms[b].Name) || (p.From == rooms[b].Name && p.To == rooms[a].Name)))
                passages.Add(Passage(rooms[a].Name, rooms[b].Name, rng, cave));
        }

        // Выход: коридор от ближайшей к этой стороне комнаты до клетки на краю блока.
        var transitions = exits.Select(e =>
        {
            var (ec, er) = EdgePoint(e);
            int nearest = Enumerable.Range(0, rooms.Count).MinBy(i => Math.Abs(zones[i].Col - ec) + Math.Abs(zones[i].Row - er));
            return new MapTransition
            {
                EdgePoint = [ec, er],
                Room = rooms[nearest].Name,
                Shape = cave ? CorridorShape.Winding : CorridorShape.LShaped,
                HasDoor = !cave && !e.External && rng.Next(10) < 3,
            };
        }).ToList();

        var map = new DungeonGenerator().Generate(new MapGenRequest
        {
            Type = MapType.Dungeon,
            Cols = MapChunk.Cols,
            Rows = MapChunk.Rows,
            Seed = seed,
            Dungeon = new DungeonRequest { Rooms = rooms, Passages = passages, Transitions = transitions },
        });

        map.Translate(chunk.OriginCol, chunk.OriginRow);
        foreach (var room in map.Rooms ?? [])
        {
            if (room.Name == "Corridor") room.Name = cave ? "Лаз" : "Коридор";
            if (cave) room.Color = [.. CaveFloor];
        }
        return new Result(map.Rooms ?? [], map.Doors ?? []);
    }

    // ── Связность ────────────────────────────────────────────────────────────
    // Обход клеток от первого выхода: внутри комнаты — по соседним клеткам, между разными комнатами —
    // только через дверь/проход (как ходит герой, MovementCalculator.CanMoveTo; двери считаем открываемыми).
    private static HashSet<(int, int)> UnreachableCells(MapChunk chunk, IReadOnlyList<ChunkExit> exits, Result result)
    {
        var roomOf = new Dictionary<(int, int), int>();
        for (int i = 0; i < result.Rooms.Count; i++)
            foreach (var p in result.Rooms[i].Positions)
                roomOf[(p[0], p[1])] = i;

        var links = new HashSet<((int, int), (int, int))>();
        foreach (var d in result.Doors)
        {
            if (d.From is not { Count: >= 2 } || d.To is not { Count: >= 2 }) continue;
            var a = (d.From[0], d.From[1]);
            var b = (d.To[0], d.To[1]);
            links.Add((a, b));
            links.Add((b, a));
        }

        var unreachable = roomOf.Keys.ToHashSet();
        if (unreachable.Count == 0) return [];
        // Без выходов (подвал — связан с остальным только лестницей) — связность от первой комнаты.
        var start = exits.Count > 0 ? exits[0].AnchorCell(chunk) : (result.Rooms[0].Positions[0][0], result.Rooms[0].Positions[0][1]);
        if (!roomOf.ContainsKey(start)) return unreachable;

        var queue = new Queue<(int, int)>([start]);
        unreachable.Remove(start);
        while (queue.Count > 0)
        {
            var (c, r) = queue.Dequeue();
            foreach (var n in new[] { (c + 1, r), (c - 1, r), (c, r + 1), (c, r - 1) })
            {
                if (!unreachable.Contains(n)) continue;
                bool sameRoom = roomOf[n] == roomOf[(c, r)];
                if (!sameRoom && !links.Contains(((c, r), n))) continue;
                unreachable.Remove(n);
                queue.Enqueue(n);
            }
        }
        return unreachable;
    }

    // Убрать недостижимые клетки (и опустевшие комнаты) и двери, ведущие в них.
    private static Result Prune(MapChunk chunk, IReadOnlyList<ChunkExit> exits, Result result)
    {
        var bad = UnreachableCells(chunk, exits, result);
        if (bad.Count == 0) return result;
        foreach (var room in result.Rooms)
            room.Positions = room.Positions.Where(p => !bad.Contains((p[0], p[1]))).ToList();
        var rooms = result.Rooms.Where(r => r.Positions.Count > 0).ToList();
        var doors = result.Doors.Where(d =>
            d.From is not { Count: >= 2 } || !bad.Contains((d.From[0], d.From[1]))).ToList();
        return new Result(rooms, doors);
    }

    // Клетка за краем сетки блока, в сторону выхода (как transitions.edgePoint у generate_map).
    private static (int col, int row) EdgePoint(ChunkExit e) => e.Side switch
    {
        ChunkSide.Left => (0, e.Offset),
        ChunkSide.Right => (MapChunk.Cols + 1, e.Offset),
        ChunkSide.Bottom => (e.Offset, 0),
        _ => (e.Offset, MapChunk.Rows + 1),
    };

    // Цвет пола пещеры — землисто-каменный (у рукотворного подземелья — серо-синий цвет генератора).
    public static readonly List<int> CaveFloor = [62, 55, 47];

    private static PassageRequest Passage(string from, string to, Random rng, bool cave) => new()
    {
        From = from,
        To = to,
        Type = PassageType.Corridor,
        Shape = cave || rng.Next(10) >= 7 ? CorridorShape.Winding : CorridorShape.LShaped,
        HasDoor = !cave && rng.Next(10) < 4,
    };

    private static double Dist((string, double Col, double Row) a, (string, double Col, double Row) b) =>
        Math.Abs(a.Col - b.Col) + Math.Abs(a.Row - b.Row);
}
