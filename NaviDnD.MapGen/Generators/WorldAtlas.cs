using System.Text;
using NaviDnD.Data.Models;

namespace NaviDnD.MapGen.Generators;

// Текстовые сводки карты мира для нейронки: что где, окружение места, путь между местами. Клетка мира ≈ четверть дня
// пути пешком по дороге; вне дороги — дольше по рельефу.
public static class WorldAtlas
{
    // ── Сводка земли для начала игры (StartNewGame): рельеф, королевства (K-номера), природные области ──
    // Мест, кроме столиц, нет — нейронка добавляет их сама (add_place).
    public static string TerrainSummary(WorldMap w)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Мир: {(w.Size == WorldSizes.Large ? "большой континент" : "небольшой материк")}, {w.Width}×{w.Height} клеток (клетка ≈ четверть дня пути), север — вверху.");
        sb.AppendLine("Королевства (K-номер: площадь, рельеф, где, соседи, столица — где и окружение):");
        for (int k = 0; k < w.Kingdoms.Count; k++)
        {
            var area = Tiles(w).Where(t => w.OwnerAt(t.x, t.y) == k).ToList();
            var neighbours = Enumerable.Range(0, w.Kingdoms.Count).Where(o => o != k && Borders(w, k, o)).Select(o => $"K{o}");
            int cap = w.Kingdoms[k].Capital;
            string capInfo = cap >= 0 && cap < w.Places.Count
                ? $"{Compass(w, w.Places[cap].X, w.Places[cap].Y)}, {Surroundings(w, w.Places[cap].X, w.Places[cap].Y, named: true)}" : "—";
            sb.AppendLine($"  K{k}: {area.Count} кл., {BiomeMix(w, area)}, {Side(w, area)}; соседи: {string.Join(", ", neighbours.DefaultIfEmpty("нет"))}; столица: {capInfo}");
        }
        sb.AppendLine("Природные области (имя: вид, размер, где):");
        foreach (var f in w.Features.OrderByDescending(f => f.Size).Take(24))
        {
            string kind = f.Biome == WorldGenerator.IslandCode ? "остров" : WorldBiomes.Get(f.Biome).Name.ToLowerInvariant();
            sb.AppendLine($"  {f.Name}: {kind}, {f.Size} кл., {Compass(w, f.X, f.Y)}");
        }
        return sb.ToString();
    }

    // ── Новое место (add_place): клетка по пожеланиям мастера ──
    public sealed record SpotRequest(string Type, WorldPlace? Near, string? Direction, double? Days, string? Terrain, int Kingdom = -1);

    // Лучшая клетка: свободная заготовка генератора подходящего вида или своя клетка; учитывает расстояние и
    // направление от Near, рельеф/реку/море/дорогу/глушь из Terrain, королевство. occupied — места игры.
    public static (int x, int y, int slot)? FindSpot(WorldMap geo, SpotRequest req, IReadOnlyCollection<(int x, int y)> occupied, ISet<int> usedSlots)
    {
        bool settlement = WorldPlaceTypes.IsSettlement(req.Type) || req.Type == WorldPlaceTypes.Castle;
        string terrain = (req.Terrain ?? "").ToLowerInvariant();
        double? wantAngle = req.Direction is { Length: > 0 } dir ? AngleOf(dir.ToLowerInvariant()) : null;
        double best = double.MaxValue;
        (int x, int y, int slot)? found = null;
        var rng = new Random(HashCode.Combine(req.Type, req.Near?.Name, req.Direction, req.Terrain));

        double Score(int x, int y, bool slot)
        {
            char b = geo.BiomeAt(x, y);
            if (WorldBiomes.IsWater(b) || b == WorldBiomes.Peaks || geo.RiverAt(x, y) > 0) return double.MaxValue;
            if (occupied.Any(o => Math.Abs(o.x - x) <= 2 && Math.Abs(o.y - y) <= 2)) return double.MaxValue;
            double s = slot ? -1.5 : 0;
            if (req.Near is { } n)
            {
                double d = Math.Sqrt(Sq(x - n.X) + Sq(y - n.Y)) * CellDays;
                s += Math.Abs(d - (req.Days ?? 2)) * 1.5;
                if (wantAngle is double wa && (x != n.X || y != n.Y))
                {
                    double a = Math.Atan2(-(y - n.Y), x - n.X) * 180 / Math.PI;
                    double diff = Math.Abs(((a - wa) % 360 + 540) % 360 - 180);
                    s += diff / 45 * 2;
                }
            }
            if (req.Kingdom >= 0 && geo.OwnerAt(x, y) != req.Kingdom) s += 6;
            bool Any(int r, Func<int, int, bool> f)
            {
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                        if (f(x + dx, y + dy)) return true;
                return false;
            }
            bool road = Any(1, (a, c) => geo.RoadAt(a, c) > 0);
            (string word, Func<bool> ok)[] hints =
            [
                ("лес", () => Any(1, (a, c) => geo.BiomeAt(a, c) is WorldBiomes.Forest or WorldBiomes.DeepForest or WorldBiomes.SnowForest)),
                ("чащ", () => Any(1, (a, c) => geo.BiomeAt(a, c) == WorldBiomes.DeepForest)),
                ("гор", () => Any(2, (a, c) => geo.BiomeAt(a, c) is WorldBiomes.Mountains or WorldBiomes.Peaks)),
                ("холм", () => Any(1, (a, c) => geo.BiomeAt(a, c) == WorldBiomes.Hills)),
                ("болот", () => Any(1, (a, c) => geo.BiomeAt(a, c) == WorldBiomes.Swamp)),
                ("пустын", () => Any(1, (a, c) => geo.BiomeAt(a, c) == WorldBiomes.Desert)),
                ("снег", () => Any(1, (a, c) => geo.BiomeAt(a, c) is WorldBiomes.Snow or WorldBiomes.Tundra or WorldBiomes.SnowForest)),
                ("рек", () => Any(1, (a, c) => geo.RiverAt(a, c) > 0)),
                ("мор", () => Any(1, (a, c) => geo.BiomeAt(a, c) is WorldBiomes.Sea or WorldBiomes.Ocean)),
                ("побереж", () => Any(1, (a, c) => geo.BiomeAt(a, c) is WorldBiomes.Sea or WorldBiomes.Ocean)),
                ("озер", () => Any(2, (a, c) => geo.BiomeAt(a, c) == WorldBiomes.Lake)),
                ("дорог", () => road),
                ("тракт", () => road),
                ("глуш", () => !Any(4, (a, c) => geo.RoadAt(a, c) > 0)),
            ];
            foreach (var (word, ok) in hints)
                if (terrain.Contains(word) && !ok()) s += 7;
            if (settlement)
            {
                if (b is WorldBiomes.Mountains or WorldBiomes.Snow) s += 4;
                if (req.Type is WorldPlaceTypes.City or WorldPlaceTypes.Town or WorldPlaceTypes.Port && !road) s += 4;
                if (req.Type == WorldPlaceTypes.Port && !Any(1, (a, c) => geo.BiomeAt(a, c) is WorldBiomes.Sea or WorldBiomes.Ocean)) s += 8;
            }
            else if (road) s += 2;   // места приключений — в стороне от дорог
            return s + 0.3 * rng.NextDouble();
        }

        // Заготовки генератора подходящего вида (поселение к поселению, логово к логову).
        for (int i = 0; i < geo.Places.Count; i++)
        {
            var p = geo.Places[i];
            if (usedSlots.Contains(i) || p.Type == WorldPlaceTypes.Capital) continue;
            bool fits = settlement ? SlotKind(p.Type) == SlotKind(req.Type) : !(WorldPlaceTypes.IsSettlement(p.Type) || p.Type == WorldPlaceTypes.Castle);
            if (!fits) continue;
            double sc = Score(p.X, p.Y, slot: true);
            if (sc < best) { best = sc; found = (p.X, p.Y, i); }
        }
        // Своя клетка: около Near — подробно, без Near — по всей карте через клетку.
        int x0 = 1, y0 = 1, x1 = geo.Width - 2, y1 = geo.Height - 2, step = 2;
        if (req.Near is { } near)
        {
            int r = (int)Math.Ceiling((req.Days ?? 2) / CellDays * 1.6) + 6;
            x0 = Math.Max(1, near.X - r); x1 = Math.Min(geo.Width - 2, near.X + r);
            y0 = Math.Max(1, near.Y - r); y1 = Math.Min(geo.Height - 2, near.Y + r);
            step = 1;
        }
        for (int y = y0; y <= y1; y += step)
            for (int x = x0; x <= x1; x += step)
            {
                double sc = Score(x, y, slot: false);
                if (sc < best) { best = sc; found = (x, y, -1); }
            }
        return found;
    }

    private static int SlotKind(string type) => type switch
    {
        WorldPlaceTypes.City or WorldPlaceTypes.Capital => 0,
        WorldPlaceTypes.Town or WorldPlaceTypes.Port => 1,
        WorldPlaceTypes.Village => 2,
        WorldPlaceTypes.Castle => 3,
        _ => 4,
    };

    private static double? AngleOf(string dir) =>
        dir.Contains("северо-вост") || dir == "ne" ? 45 : dir.Contains("северо-зап") || dir == "nw" ? 135
        : dir.Contains("юго-вост") || dir == "se" ? -45 : dir.Contains("юго-зап") || dir == "sw" ? -135
        : dir.Contains("север") || dir == "n" ? 90 : dir.Contains("юг") || dir == "s" ? -90
        : dir.Contains("восток") || dir == "e" ? 0 : dir.Contains("запад") || dir == "w" ? 180 : null;

    // Тропа от места к ближайшей дороге или видимому месту (по суше, ≤ 40 клеток). Уже у дороги — не нужна.
    public static List<(int x, int y)>? TrailFrom(WorldMap w, int sx, int sy)
    {
        if (Enumerable.Range(-1, 3).Any(dx => Enumerable.Range(-1, 3).Any(dy => w.RoadAt(sx + dx, sy + dy) > 0))) return null;
        bool Target(int x, int y) => (x != sx || y != sy) && (w.RoadAt(x, y) > 0 || w.Places.Any(p => !p.Hidden && p.X == x && p.Y == y));
        double Cost(int x, int y) => w.BiomeAt(x, y) switch
        {
            WorldBiomes.Plains or WorldBiomes.Grass or WorldBiomes.Beach => 1,
            WorldBiomes.Forest or WorldBiomes.Tundra or WorldBiomes.Desert or WorldBiomes.Hills => 2,
            WorldBiomes.DeepForest or WorldBiomes.SnowForest or WorldBiomes.Snow => 3,
            WorldBiomes.Swamp or WorldBiomes.Mountains => 5,
            _ => double.PositiveInfinity,
        } + (w.RiverAt(x, y) > 0 ? 6 : 0);
        var dist = new Dictionary<(int, int), double> { [(sx, sy)] = 0 };
        var prev = new Dictionary<(int, int), (int, int)>();
        var pq = new PriorityQueue<(int x, int y), double>();
        var done = new HashSet<(int, int)>();
        pq.Enqueue((sx, sy), 0);
        while (pq.Count > 0)
        {
            var p = pq.Dequeue();
            if (!done.Add(p) || Math.Abs(p.x - sx) + Math.Abs(p.y - sy) > 40) continue;
            if (Target(p.x, p.y))
            {
                var path = new List<(int x, int y)>();
                var q = p;
                while (prev.TryGetValue(q, out var pr)) { q = pr; path.Add(q); }
                return path;   // от клетки рядом с целью до самого места
            }
            foreach (var n in new[] { (p.x + 1, p.y), (p.x - 1, p.y), (p.x, p.y + 1), (p.x, p.y - 1) })
            {
                if (!w.InBounds(n.Item1, n.Item2)) continue;
                double c = Cost(n.Item1, n.Item2);
                if (double.IsInfinity(c)) continue;
                double nd = dist[p] + c;
                if (dist.TryGetValue(n, out var old) && old <= nd) continue;
                dist[n] = nd;
                prev[n] = p;
                pq.Enqueue(n, nd);
            }
        }
        return null;
    }

    // ── Сводка мира для начала игры: имена, описания, королевства, места ──
    public static string Overview(WorldMap w, bool withDmNotes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Мир «{w.Name}»{(w.Description is { Length: > 0 } d ? $": {d}" : "")}");
        if (withDmNotes && w.DmNotes is { Length: > 0 } wn) sb.AppendLine($"  [мастер] {wn}");
        foreach (var (k, i) in w.Kingdoms.Select((k, i) => (k, i)))
        {
            string cap = k.Capital >= 0 && k.Capital < w.Places.Count ? w.Places[k.Capital].Name : "—";
            sb.AppendLine($"Королевство «{k.Name}» (столица {cap}){(k.Description is { Length: > 0 } kd ? $": {kd}" : "")}");
            if (withDmNotes && k.DmNotes is { Length: > 0 } kn) sb.AppendLine($"  [мастер] {kn}");
        }
        sb.AppendLine("Места:");
        foreach (var p in w.Places.OrderBy(p => PlaceRank(p.Type)).ThenBy(p => p.Name))
        {
            sb.Append($"  {p.Name} — {WorldPlaceTypes.Label(p.Type)}, {KingdomName(w, p)}, {Compass(w, p.X, p.Y)}{(p.Hidden ? " (герой не знает)" : "")}");
            if (p.Description is { Length: > 0 } pd) sb.Append($": {pd}");
            if (withDmNotes && p.DmNotes is { Length: > 0 } pn) sb.Append($" [мастер] {pn}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ── Запросы world_query ──
    public static WorldPlace? FindPlace(WorldMap w, string name)
    {
        string n = name.Trim().Trim('«', '»', '"');
        return w.Places.FirstOrDefault(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
               ?? w.Places.FirstOrDefault(p => p.Name.Contains(n, StringComparison.OrdinalIgnoreCase))
               ?? w.Places.FirstOrDefault(p => n.Contains(p.Name, StringComparison.OrdinalIgnoreCase));
    }

    public static string PlaceInfo(WorldMap w, WorldPlace p, bool withDmNotes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{p.Name} — {WorldPlaceTypes.Label(p.Type)}, {KingdomName(w, p)}, {Compass(w, p.X, p.Y)}");
        if (p.Description is { Length: > 0 } d) sb.AppendLine(d);
        if (withDmNotes && p.DmNotes is { Length: > 0 } n) sb.AppendLine($"[мастер] {n}");
        sb.AppendLine($"Окружение: {Surroundings(w, p.X, p.Y, named: true)}");
        return sb.ToString();
    }

    // Места в радиусе radiusDays дней пути (по прямой, клетка = CellDays) — с направлением и временем пути.
    public static string Near(WorldMap w, WorldPlace from, double radiusDays = 6)
    {
        var list = w.Places.Where(p => p != from)
            .Select(p => (p, d: Math.Sqrt(Sq(p.X - from.X) + Sq(p.Y - from.Y)) * CellDays))
            .Where(t => t.d <= radiusDays).OrderBy(t => t.d).Take(12).ToList();
        if (list.Count == 0) return $"В {radiusDays} днях пути от «{from.Name}» мест нет.";
        var sb = new StringBuilder($"Рядом с «{from.Name}»:\n");
        foreach (var (p, _) in list)
        {
            var r = Route(w, from, p);
            sb.AppendLine($"  {p.Name} — {WorldPlaceTypes.Label(p.Type)}, {Direction(p.X - from.X, p.Y - from.Y)}, {r?.Summary ?? "пути нет"}");
        }
        return sb.ToString();
    }

    public sealed record RouteInfo(double Days, double RoadShare, List<(int x, int y)> Path, string Summary);

    // Клетка мира по дороге — четверть дня пешком (масштаб времени пути).
    public const double CellDays = 0.25;

    // Сколько дней пешком входить в клетку: по дороге — быстрее всего (×0.75 CellDays), вне дороги — дольше по рельефу
    // (×1.5 … ×4); брод +CellDays; вода и пики — нельзя (∞). Дорога в 2,5–5 раз быстрее бездорожья — маршрут держится
    // её и при заметном обходе (при ×1 путь срезал напрямик через лес).
    public static double StepDays(WorldMap w, int x, int y) => CellDays * (w.RoadAt(x, y) > 0 ? 0.75 : w.BiomeAt(x, y) switch
    {
        WorldBiomes.Plains or WorldBiomes.Grass or WorldBiomes.Beach => 1.5,
        WorldBiomes.Forest or WorldBiomes.Tundra or WorldBiomes.Desert => 2,
        WorldBiomes.DeepForest or WorldBiomes.Hills or WorldBiomes.SnowForest or WorldBiomes.Snow => 2.5,
        WorldBiomes.Swamp or WorldBiomes.Mountains => 4,
        _ => double.PositiveInfinity,
    } + (w.RiverAt(x, y) > 0 ? 1 : 0));

    public static RouteInfo? Route(WorldMap w, WorldPlace a, WorldPlace b) => Route(w, (a.X, a.Y), (b.X, b.Y));

    // Путь пешком между клетками (по сторонам, дешевле — по дорогам).
    public static RouteInfo? Route(WorldMap w, (int x, int y) start, (int x, int y) goal)
    {
        var dist = new Dictionary<(int, int), double> { [start] = 0 };
        var prev = new Dictionary<(int, int), (int, int)>();
        var pq = new PriorityQueue<(int x, int y), double>();
        var done = new HashSet<(int, int)>();
        pq.Enqueue(start, 0);
        while (pq.Count > 0)
        {
            var p = pq.Dequeue();
            if (!done.Add(p)) continue;
            if (p == goal) break;
            foreach (var n in new[] { (p.x + 1, p.y), (p.x - 1, p.y), (p.x, p.y + 1), (p.x, p.y - 1) })
            {
                if (!w.InBounds(n.Item1, n.Item2)) continue;
                double c = n == goal ? Math.Min(StepDays(w, n.Item1, n.Item2), 1.5 * CellDays) : StepDays(w, n.Item1, n.Item2);
                if (double.IsInfinity(c)) continue;
                double nd = dist[p] + c;
                if (dist.TryGetValue(n, out var old) && old <= nd) continue;
                dist[n] = nd;
                prev[n] = p;
                pq.Enqueue(n, nd + (Math.Abs(n.Item1 - goal.x) + Math.Abs(n.Item2 - goal.y)) * CellDays * 0.75);   // ≤ шага по дороге — оценка не завышает
            }
        }
        if (!dist.TryGetValue(goal, out double days)) return null;
        var path = new List<(int x, int y)> { goal };
        var q = goal;
        while (prev.TryGetValue(q, out var pr)) { q = pr; path.Add(q); }
        path.Reverse();
        double road = path.Count(t => w.RoadAt(t.x, t.y) > 0) / (double)Math.Max(1, path.Count);
        var kingdoms = path.Select(t => w.OwnerAt(t.x, t.y)).Where(o => o >= 0 && o < w.Kingdoms.Count).Distinct().Select(o => w.Kingdoms[o].Name).ToList();
        double rounded = Math.Max(0.5, Math.Round(days * 2) / 2);
        string summary = $"{rounded:0.#} {DaysWord(rounded)} пути, {(road > 0.7 ? "по дороге" : road > 0.25 ? "частью по дороге" : "без дороги")}"
                         + (kingdoms.Count > 1 ? $", через {string.Join(" → ", kingdoms)}" : "");
        return new RouteInfo(rounded, road, path, summary);
    }

    public static string DaysText(double d) => $"{d:0.#} {DaysWord(d)}";

    // ── Помощники ──
    public static string KingdomName(WorldMap w, WorldPlace p)
    {
        int k = p.Kingdom >= 0 ? p.Kingdom : w.OwnerAt(p.X, p.Y);
        return k >= 0 && k < w.Kingdoms.Count ? $"королевство {w.Kingdoms[k].Name}" : "ничья земля";
    }

    // Окружение клетки: рельеф, река/берег, дороги, ближайшая природная область (с именем — если named).
    public static string Surroundings(WorldMap w, int x, int y, bool named)
    {
        var parts = new List<string> { WorldBiomes.Get(w.BiomeAt(x, y)).Name.ToLowerInvariant() };
        bool river = false, sea = false, lake = false, mountains = false;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                char b = w.BiomeAt(x + dx, y + dy);
                if (w.RiverAt(x + dx, y + dy) > 0) river = true;
                if (b is WorldBiomes.Sea or WorldBiomes.Ocean) sea = true;
                if (b == WorldBiomes.Lake) lake = true;
                if (b is WorldBiomes.Mountains or WorldBiomes.Peaks) mountains = true;
            }
        if (river) parts.Add("у реки");
        if (sea) parts.Add("у моря");
        if (lake) parts.Add("у озера");
        if (mountains) parts.Add("у гор");
        int road = Enumerable.Range(-1, 3).SelectMany(dx => Enumerable.Range(-1, 3).Select(dy => w.RoadAt(x + dx, y + dy))).Max();
        parts.Add(road == 2 ? "на тракте" : road == 1 ? "у тропы" : "в глуши");
        var feature = w.Features.Select((f, i) => (f, i, d: Sq(f.X - x) + Sq(f.Y - y))).Where(t => t.d <= 64).OrderBy(t => t.d).FirstOrDefault();
        if (feature.f != null)
            parts.Add(named ? $"рядом «{feature.f.Name}»" : $"рядом F{feature.i}");
        return string.Join(", ", parts);
    }

    // Имя клетки для игрока и мастера: «холмы — Холмы Корград» (ближайшая область того же рельефа), иначе рельеф.
    public static string TileName(WorldMap w, int x, int y)
    {
        char b = w.BiomeAt(x, y);
        string biome = WorldBiomes.Get(b).Name.ToLowerInvariant();
        var f = w.Features.Where(f => f.Biome == b && Sq(f.X - x) + Sq(f.Y - y) <= Math.Max(64, f.Size))
            .OrderBy(f => Sq(f.X - x) + Sq(f.Y - y)).FirstOrDefault();
        return f != null ? $"{biome} — {f.Name}" : biome;
    }

    public static string Compass(WorldMap w, int x, int y)
    {
        double fx = x / (double)w.Width, fy = y / (double)w.Height;
        string ns = fy < 0.33 ? "север" : fy > 0.67 ? "юг" : "";
        string ew = fx < 0.33 ? "запад" : fx > 0.67 ? "восток" : "";
        return ns.Length + ew.Length == 0 ? "центр мира" : ns.Length > 0 && ew.Length > 0 ? $"{ns}о-{ew}" : ns + ew;
    }

    public static string Direction(int dx, int dy)
    {
        double a = Math.Atan2(-dy, dx) * 180 / Math.PI;   // y вниз — юг
        string[] dirs = ["на восток", "на северо-восток", "на север", "на северо-запад", "на запад", "на юго-запад", "на юг", "на юго-восток"];
        return dirs[((int)Math.Round(a / 45) + 8) % 8];
    }

    private static string DaysWord(double d) =>
        d % 1 != 0 ? "дня" : (int)d % 10 == 1 && (int)d % 100 != 11 ? "день" : (int)d % 10 is >= 2 and <= 4 && (int)d % 100 is < 12 or > 14 ? "дня" : "дней";

    private static int PlaceRank(string type) => type switch
    {
        WorldPlaceTypes.Capital => 0, WorldPlaceTypes.City => 1, WorldPlaceTypes.Port or WorldPlaceTypes.Town => 2,
        WorldPlaceTypes.Castle => 3, WorldPlaceTypes.Village => 4, _ => 5,
    };

    private static IEnumerable<(int x, int y)> Tiles(WorldMap w)
    {
        for (int y = 0; y < w.Height; y++)
            for (int x = 0; x < w.Width; x++)
                yield return (x, y);
    }

    private static bool Borders(WorldMap w, int a, int b)
    {
        for (int y = 0; y < w.Height; y++)
            for (int x = 0; x < w.Width - 1; x++)
            {
                int o1 = w.OwnerAt(x, y), o2 = w.OwnerAt(x + 1, y), o3 = w.OwnerAt(x, y + 1);
                if ((o1 == a && (o2 == b || o3 == b)) || (o1 == b && (o2 == a || o3 == a))) return true;
            }
        return false;
    }

    private static string BiomeMix(WorldMap w, List<(int x, int y)> area) =>
        string.Join(", ", area.GroupBy(t => WorldBiomes.Get(w.BiomeAt(t.x, t.y)).Name.ToLowerInvariant())
            .OrderByDescending(g => g.Count()).Take(3).Select(g => $"{g.Key} {100 * g.Count() / Math.Max(1, area.Count)}%"));

    private static string Side(WorldMap w, List<(int x, int y)> area) =>
        area.Count == 0 ? "—" : Compass(w, (int)area.Average(t => t.x), (int)area.Average(t => t.y));

    private static int Sq(int v) => v * v;
}
