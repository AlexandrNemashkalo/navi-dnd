using NaviDnD.Data.Models;

namespace NaviDnD.MapGen.Generators;

// World map generator (WorldMap) - entirely in code, reproducible using seed: 1) terrain: height noise +
// continent mask (small world - one continent, large - 2-3 massifs), mountain ranges with ridge noise; sea
// level - by land share; 2) climate: temperature from latitude (the north is colder) and altitude, humidity -
// noise → biomes; 3) rivers and lakes: “filling” from the sea (priority flood) gives the flow of each cell, the
// accumulated flow is rivers, deep basins are lakes; 4) settlements according to the convenience of the place
// (rivers, coast, plains): capitals → cities → towns → villages, with indents; 5) kingdoms grow from capitals
// according to the “cost of travel” (mountains and rivers are natural borders); 6) roads - the shortest routes
// between settlements (roads between large ones, paths to villages); 7) places of adventure in the wilderness:
// ruins, dungeons, caves, sanctuaries; fortresses at the borders; 8) natural areas (forests, mountains, swamps,
// deserts, lakes) with names for captions. Names are syllabic, temporary: the AI later gives its own (stage 2).
public static class WorldGenerator
{
    // World parameters (new game questionnaire): Water - water (-2 very little... 0 usually... 2 very much),
    // Climate - -2 icy... 0 moderate... 2 hot, Kingdoms - number of kingdoms (0 - according to the size of the
    // world), Continents - number of continents (0 - according to the size of the world),
    // Mountains/Forests/Deserts/Rivers - -3 no, -2 very little... 0 usually... 2 very much.
    public sealed record Options(int Seed, string Size = WorldSizes.Small, string? Name = null, int Water = 0, int Climate = 0, int Kingdoms = 0,
        int Continents = 0, int Mountains = 0, int Forests = 0, int Deserts = 0, int Rivers = 0, string Language = L.Russian);

    // Default world name (its own source of randomness - does not match the names of the capitals of this seed).
    public static string DefaultName(int seed, string language) => new NameGen(new Random(seed ^ 0x77a1), language).Proper();

    private sealed class Ctx
    {
        public required int W, H;
        public required Random Rng;
        public required int Seed;
        public double[,] Height = null!;      // 0..1
        public double Sea;                    // sea level
        public char[,] Biome = null!;
        public double[,] Moist = null!;
        public int[,] River = null!;          // 0..9
        public int[,] Road = null!;           // 0..2
        public int[,] Owner = null!;          // -1 / kingdom index
        public double[,] Acc = null!;         // accumulated flow
        public double[,] Wander = null!;      // road step price multiplier (smooth noise) - roads winding
        public bool Large;
        public int Water, Climate, KingdomCount, Continents, Mountains, Forests, Deserts, RiverAmount;
    }

    public static WorldMap Generate(Options o)
    {
        bool large = o.Size == WorldSizes.Large;
        var c = new Ctx { W = large ? 256 : 128, H = large ? 192 : 96, Rng = new Random(o.Seed), Seed = o.Seed, Large = large,
            Water = Math.Clamp(o.Water, -2, 2), Climate = Math.Clamp(o.Climate, -2, 2), KingdomCount = o.Kingdoms,
            Continents = Math.Clamp(o.Continents, 0, 6), Mountains = Math.Clamp(o.Mountains, -3, 2), Forests = Math.Clamp(o.Forests, -3, 2),
            Deserts = Math.Clamp(o.Deserts, -3, 2), RiverAmount = Math.Clamp(o.Rivers, -3, 2) };
        Relief(c);
        Climate(c);
        Rivers(c);
        var names = new NameGen(new Random(o.Seed ^ 0x5eed), o.Language);
        var places = Settlements(c, names);
        var kingdoms = Kingdoms(c, places, names);
        RoadsBetween(c, places);
        Wilds(c, places, names);
        var features = Features(c, names);

        var map = new WorldMap
        {
            Id = $"world-{o.Seed}",
            Name = o.Name ?? names.Proper(),
            Language = o.Language,
            Seed = o.Seed,
            Size = o.Size,
            Water = c.Water,
            Climate = c.Climate,
            Continents = c.Continents,
            Mountains = c.Mountains,
            Forests = c.Forests,
            Deserts = c.Deserts,
            RiverAmount = c.RiverAmount,
            Width = c.W,
            Height = c.H,
            Places = places,
            Kingdoms = kingdoms,
            Features = features,
        };
        for (int y = 0; y < c.H; y++)
        {
            var b = new char[c.W]; var h = new char[c.W]; var r = new char[c.W]; var d = new char[c.W]; var ow = new char[c.W];
            for (int x = 0; x < c.W; x++)
            {
                b[x] = c.Biome[x, y];
                h[x] = (char)('0' + Math.Clamp((int)(c.Height[x, y] * 10), 0, 9));
                r[x] = (char)('0' + c.River[x, y]);
                d[x] = (char)('0' + c.Road[x, y]);
                ow[x] = c.Owner[x, y] < 0 ? '.' : (char)('a' + c.Owner[x, y]);
            }
            map.Biomes.Add(new string(b)); map.Heights.Add(new string(h)); map.Rivers.Add(new string(r));
            map.Roads.Add(new string(d)); map.Owners.Add(new string(ow));
        }
        AddIslands(map, new Random(o.Seed ^ 0x15a1));
        return map;
    }

    // Islands: individual land areas smaller than the main continent - with a name (label on the map). The
    // island code in WorldFeature.Biome is IslandCode. Can also be called for an already saved world without
    // islands.
    public const char IslandCode = 'I';

    // Places located on a river or water (worlds of old versions) - to the nearest dry square nearby.
    public static void MovePlacesOffWater(WorldMap map)
    {
        bool Dry(int x, int y) => map.InBounds(x, y) && map.RiverAt(x, y) == 0 && !WorldBiomes.IsWater(map.BiomeAt(x, y));
        foreach (var p in map.Places.Where(p => !Dry(p.X, p.Y)))
        {
            var spot = Enumerable.Range(1, 3)
                .SelectMany(r => Enumerable.Range(-r, 2 * r + 1).SelectMany(dx => Enumerable.Range(-r, 2 * r + 1).Select(dy => (x: p.X + dx, y: p.Y + dy))))
                .Where(q => Dry(q.x, q.y) && !map.Places.Any(o => o.X == q.x && o.Y == q.y))
                .OrderBy(q => Dist(q.x, q.y, p.X, p.Y) - (map.RoadAt(q.x, q.y) > 0 ? 0.5 : 0))
                .Cast<(int x, int y)?>().FirstOrDefault();
            if (spot is { } s) { p.X = s.x; p.Y = s.y; }
        }
    }

    public static void AddIslands(WorldMap map, Random rng)
    {
        if (map.Features.Any(f => f.Biome == IslandCode)) return;
        var names = new NameGen(rng, map.Language);
        var seen = new bool[map.Width, map.Height];
        var masses = new List<List<(int x, int y)>>();
        for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
            {
                if (seen[x, y] || IsSea(map.BiomeAt(x, y))) continue;
                var mass = new List<(int x, int y)>();
                var q = new Queue<(int x, int y)>([(x, y)]);
                seen[x, y] = true;
                while (q.Count > 0)
                {
                    var p = q.Dequeue();
                    mass.Add(p);
                    foreach (var n in Around(p.x, p.y))
                        if (map.InBounds(n.x, n.y) && !seen[n.x, n.y] && !IsSea(map.BiomeAt(n.x, n.y))) { seen[n.x, n.y] = true; q.Enqueue(n); }
                }
                masses.Add(mass);
            }
        if (masses.Count == 0) return;
        int largest = masses.Max(m => m.Count);
        foreach (var mass in masses)
        {
            // Continents (large massifs) - without signature; islands - from 4 cells.
            if (mass.Count < 4 || mass.Count > largest * 0.35) continue;
            double cx = mass.Average(p => p.x), cy = mass.Average(p => p.y);
            var anchor = mass.MinBy(p => Dist(p.x, p.y, cx, cy));
            map.Features.Add(new WorldFeature { Name = names.Island(mass.Count), Biome = IslandCode, X = anchor.x, Y = anchor.y, Size = mass.Count });
        }

        static bool IsSea(char b) => b is WorldBiomes.Ocean or WorldBiomes.Sea;
    }

    // ── 1. Terrain ───────────────────────────── ──────────────────────────────
    private static void Relief(Ctx c)
    {
        c.Height = new double[c.W, c.H];
        var rng = c.Rng;
        // Land masses (continents): by default, small world is one, large world is 2–3; given - so many, spaced
        // across the width of the world (smaller and further from each other, so as not to merge).
        int masses = c.Continents > 0 ? c.Continents : c.Large ? rng.Next(2, 4) : 1;
        var blobs = new List<(double x, double y, double r)>();
        for (int i = 0; i < masses; i++)
        {
            double bx, by, br;
            if (masses == 1)
            {
                bx = c.W * (0.42 + 0.16 * rng.NextDouble());
                by = c.H * (0.42 + 0.16 * rng.NextDouble());
                br = 0.42 * Math.Min(c.W, c.H) * (0.8 + 0.4 * rng.NextDouble());
            }
            else if (c.Continents == 0)
            {
                bx = c.W * (0.25 + 0.5 * rng.NextDouble());
                by = c.H * (0.25 + 0.5 * rng.NextDouble());
                br = 0.30 * Math.Min(c.W, c.H) * (0.8 + 0.4 * rng.NextDouble());
            }
            else
            {
                bx = c.W * (0.12 + 0.76 * (i + 0.5) / masses + 0.06 * (rng.NextDouble() - 0.5));
                by = c.H * (0.3 + 0.4 * rng.NextDouble());
                br = Math.Min(0.42 * Math.Min(c.W, c.H), 0.5 * c.W / masses) * (0.85 + 0.3 * rng.NextDouble());
            }
            blobs.Add((bx, by, br));
        }
        double scale = c.Large ? 48.0 : 30.0;
        int s = c.Seed;
        for (int x = 0; x < c.W; x++)
            for (int y = 0; y < c.H; y++)
            {
                // Distortion of coordinates - the coast is not round.
                double wx = x + 14 * (Fbm(x / scale, y / scale, s + 11, 3) - 0.5) * (c.Large ? 2 : 1.3);
                double wy = y + 14 * (Fbm(x / scale, y / scale, s + 13, 3) - 0.5) * (c.Large ? 2 : 1.3);
                double mask = 0, d1 = double.MaxValue, d2 = double.MaxValue;
                foreach (var (bx, by, br) in blobs)
                {
                    double d = Math.Sqrt((wx - bx) * (wx - bx) + (wy - by) * (wy - by)) / br;
                    mask = Math.Max(mask, 1 - d);
                    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
                // A given number of continents - a strait between them (where the point is almost equally close to the two centers).
                if (c.Continents > 1 && d2 < double.MaxValue)
                    mask -= 0.9 * Math.Clamp(1 - (d2 - d1) * 3.5, 0, 1);
                // The edges of the map are always the sea.
                double edge = Math.Min(Math.Min(x, c.W - 1 - x) / (c.W * 0.08), Math.Min(y, c.H - 1 - y) / (c.H * 0.08));
                double e = Math.Clamp(edge, 0, 1);
                mask = mask * e - (1 - e) * 0.5;
                double n = Fbm(x / (scale * 0.55), y / (scale * 0.55), s, 5);
                double h = 0.55 * n + 0.75 * mask;
                c.Height[x, y] = h;
            }
        // Sea level - by land share.
        var all = new List<double>(c.W * c.H);
        foreach (var v in c.Height) all.Add(v);
        all.Sort();
        c.Sea = all[(int)(all.Count * Math.Clamp((c.Large ? 0.52 : 0.58) + 0.11 * c.Water, 0.25, 0.82))];
        // Ridges: ridge noise on land.
        double max = all[^1];
        for (int x = 0; x < c.W; x++)
            for (int y = 0; y < c.H; y++)
            {
                double h = c.Height[x, y];
                if (h <= c.Sea) { c.Height[x, y] = Math.Clamp((h - all[0]) / (c.Sea - all[0]) * 0.3, 0, 0.3); continue; }
                double land = (h - c.Sea) / (max - c.Sea);
                double ridge = 1 - Math.Abs(2 * Fbm(x / (scale * 0.7), y / (scale * 0.7), s + 29, 4) - 1);
                double ranges = Math.Pow(ridge, 3) * Math.Clamp(land * 2.2, 0, 1);
                c.Height[x, y] = Math.Clamp(0.3 + 0.7 * (0.5 * land + 0.55 * ranges), 0.31, 1);
            }
        c.Sea = 0.3;
    }

    // ── 2. Climate → biomes ───────────────────────── ──────────────────────────
    private static void Climate(Ctx c)
    {
        c.Biome = new char[c.W, c.H];
        c.Moist = new double[c.W, c.H];
        int s = c.Seed;
        double scale = c.Large ? 40.0 : 26.0;
        for (int x = 0; x < c.W; x++)
            for (int y = 0; y < c.H; y++)
            {
                double h = c.Height[x, y];
                double m = Fbm(x / scale, y / scale, s + 41, 4);
                c.Moist[x, y] = m;
                if (h <= c.Sea) { c.Biome[x, y] = WorldBiomes.Ocean; continue; }
                double land = (h - c.Sea) / (1 - c.Sea);
                double lat = y / (double)c.H;
                double temp = (c.Large ? 0.08 + 0.85 * lat : 0.32 + 0.42 * lat)
                    + 0.12 * (Fbm(x / scale, y / scale, s + 47, 2) - 0.5) - 0.45 * land + 0.16 * c.Climate;
                // Thresholds are shifted by parameters: more mountains - lower height thresholds, more
                // forests/deserts - wider range; “no” (-3) - the threshold is unattainable (there are no
                // mountains - only hills remain).
                bool noMt = c.Mountains <= -3;
                double mt = noMt ? -0.23 : 0.12 * c.Mountains, fo = c.Forests <= -3 ? -1 : 0.08 * c.Forests, de = c.Deserts <= -3 ? -1 : 0.09 * c.Deserts;
                c.Biome[x, y] = land switch
                {
                    _ when !noMt && land > 0.84 - 0.05 * c.Mountains => WorldBiomes.Peaks,
                    _ when !noMt && land > 0.68 - mt => WorldBiomes.Mountains,
                    _ when land > 0.52 - mt => temp < 0.12 ? WorldBiomes.Tundra : WorldBiomes.Hills,
                    _ => temp < 0.06 ? WorldBiomes.Snow
                        : temp < 0.24 && m > 0.5 - fo ? WorldBiomes.SnowForest   // cold and humid - taiga
                        : temp < 0.16 ? WorldBiomes.Tundra
                        : temp > 0.62 - de && m < 0.42 + de ? WorldBiomes.Desert
                        : m > 0.7 && land < 0.12 ? WorldBiomes.Swamp
                        : m > 0.64 - fo ? WorldBiomes.DeepForest
                        : m > 0.52 - fo ? WorldBiomes.Forest
                        : m > 0.42 ? WorldBiomes.Grass
                        : WorldBiomes.Plains,
                };
            }
        // Water near the shore - coastal; low shore - sand/pebbles.
        var coastDist = DistanceToLand(c);
        for (int x = 0; x < c.W; x++)
            for (int y = 0; y < c.H; y++)
            {
                if (c.Biome[x, y] == WorldBiomes.Ocean) { if (coastDist[x, y] <= 2) c.Biome[x, y] = WorldBiomes.Sea; continue; }
                bool shore = Around(x, y).Any(n => In(c, n) && WorldBiomes.IsWater(c.Biome[n.x, n.y]));
                double land = (c.Height[x, y] - c.Sea) / (1 - c.Sea);
                if (shore && land < 0.12 && c.Biome[x, y] is WorldBiomes.Plains or WorldBiomes.Grass or WorldBiomes.Desert
                    && Fbm(x / 6.0, y / 6.0, s + 53, 2) > 0.45)
                    c.Biome[x, y] = WorldBiomes.Beach;
            }
    }

    private static int[,] DistanceToLand(Ctx c)
    {
        var d = new int[c.W, c.H];
        var q = new Queue<(int x, int y)>();
        for (int x = 0; x < c.W; x++)
            for (int y = 0; y < c.H; y++)
                if (c.Biome[x, y] != WorldBiomes.Ocean) { d[x, y] = 0; q.Enqueue((x, y)); }
                else d[x, y] = int.MaxValue;
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var n in Around(p.x, p.y))
                if (In(c, n) && d[n.x, n.y] > d[p.x, p.y] + 1) { d[n.x, n.y] = d[p.x, p.y] + 1; q.Enqueue(n); }
        }
        return d;
    }

    // ── 3. Rivers and lakes ────────────────────────── ───────────────────────────
    private static void Rivers(Ctx c)
    {
        int W = c.W, H = c.H;
        var filled = new double[W, H];
        var parent = new (int x, int y)[W, H];
        var seen = new bool[W, H];
        var order = new List<(int x, int y)>(W * H);
        var pq = new PriorityQueue<(int x, int y), double>();
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (c.Biome[x, y] is WorldBiomes.Ocean or WorldBiomes.Sea)
                {
                    seen[x, y] = true;
                    filled[x, y] = c.Height[x, y];
                    parent[x, y] = (-1, -1);
                    pq.Enqueue((x, y), c.Height[x, y]);
                }
        while (pq.Count > 0)
        {
            var p = pq.Dequeue();
            order.Add(p);
            foreach (var n in Around(p.x, p.y))
            {
                if (!In(c, n) || seen[n.x, n.y]) continue;
                seen[n.x, n.y] = true;
                parent[n.x, n.y] = p;
                filled[n.x, n.y] = Math.Max(c.Height[n.x, n.y], filled[p.x, p.y] + 1e-5);
                pq.Enqueue(n, filled[n.x, n.y]);
            }
        }

        // Lakes are basins deeper than the threshold (connected areas ≥ 3 cells).
        var lakeMask = new bool[W, H];
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (!WorldBiomes.IsWater(c.Biome[x, y]) && filled[x, y] - c.Height[x, y] > 0.03) lakeMask[x, y] = true;
        foreach (var region in Regions(c, (x, y) => lakeMask[x, y]))
            if (region.Count >= 4)
                foreach (var (x, y) in region) c.Biome[x, y] = WorldBiomes.Lake;

        // Accumulated drainage: from the top cells to the bottom (reverse filling order).
        c.Acc = new double[W, H];
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var (x, y) = order[i];
            if (WorldBiomes.IsWater(c.Biome[x, y]) && c.Biome[x, y] != WorldBiomes.Lake) continue;
            c.Acc[x, y] += 0.4 + c.Moist[x, y];
            var p = parent[x, y];
            if (p.x >= 0) c.Acc[p.x, p.y] += c.Acc[x, y];
        }
        c.River = new int[W, H];
        double threshold = c.RiverAmount <= -3 ? double.MaxValue
            : (c.Large ? 60 : 38) * c.RiverAmount switch { 2 => 0.3, 1 => 0.5, -1 => 2.0, -2 => 3.5, _ => 1.0 };
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                if (WorldBiomes.IsWater(c.Biome[x, y]) || c.Acc[x, y] < threshold) continue;
                c.River[x, y] = Math.Clamp(1 + (int)(Math.Log(c.Acc[x, y] / threshold, 2) * 1.6), 1, 9);
                // The river moistens the banks: the dry plains near the river are meadows.
                if (c.Biome[x, y] == WorldBiomes.Desert && c.River[x, y] >= 3) c.Biome[x, y] = WorldBiomes.Grass;
            }
    }

    // ── 4. Settlements ──────────────────────────── ────────────────────────────
    private static double Habitability(Ctx c, int x, int y)
    {
        double b = c.Biome[x, y] switch
        {
            WorldBiomes.Plains or WorldBiomes.Grass => 1.0,
            WorldBiomes.Beach => 0.8,
            WorldBiomes.Forest or WorldBiomes.Hills => 0.6,
            WorldBiomes.DeepForest => 0.3,
            WorldBiomes.SnowForest => 0.26,
            WorldBiomes.Desert or WorldBiomes.Tundra => 0.22,
            WorldBiomes.Swamp => 0.18,
            _ => 0,
        };
        if (b == 0) return 0;
        int river = Math.Max(c.River[x, y], Around(x, y).Where(n => In(c, n)).Select(n => c.River[n.x, n.y]).DefaultIfEmpty(0).Max());
        if (river > 0) b += 0.4 + 0.08 * river;
        if (Around8(x, y).Any(n => In(c, n) && c.Biome[n.x, n.y] is WorldBiomes.Sea)) b += 0.45;
        if (Around8(x, y).Any(n => In(c, n) && c.Biome[n.x, n.y] == WorldBiomes.Lake)) b += 0.35;
        return b + 0.15 * c.Rng.NextDouble();
    }

    private static List<WorldPlace> Settlements(Ctx c, NameGen names)
    {
        var score = new double[c.W, c.H];
        var candidates = new List<(int x, int y, double s)>();
        int landArea = 0;
        for (int x = 1; x < c.W - 1; x++)
            for (int y = 1; y < c.H - 1; y++)
            {
                if (!WorldBiomes.IsWater(c.Biome[x, y])) landArea++;
                double s = c.River[x, y] > 0 ? 0 : Habitability(c, x, y);   // by the river - yes, on the river itself - no
                if (s > 0) candidates.Add((x, y, s));
            }
        candidates.Sort((a, b) => b.s.CompareTo(a.s));
        var places = new List<WorldPlace>();
        bool coastal((int x, int y) p) => Around8(p.x, p.y).Any(n => In(c, n) && c.Biome[n.x, n.y] == WorldBiomes.Sea);

        void Pick(string type, int count, double minDist)
        {
            foreach (var (x, y, s) in candidates)
            {
                if (count == 0) return;
                if (places.Any(p => Dist(p.X, p.Y, x, y) < minDist * (WorldPlaceTypes.IsSettlement(p.Type) && p.Type != WorldPlaceTypes.Village ? 1 : 0.6))) continue;
                string t = type == WorldPlaceTypes.Town && coastal((x, y)) && c.Rng.Next(10) < 4 ? WorldPlaceTypes.Port : type;
                places.Add(new WorldPlace { Type = t, X = x, Y = y, Name = t == WorldPlaceTypes.Village ? names.Village() : names.Proper() });
                count--;
            }
        }
        int kingdoms = c.KingdomCount > 0 ? Math.Min(c.KingdomCount, c.Large ? 8 : 4) : c.Large ? c.Rng.Next(4, 7) : c.Rng.Next(1, 3);
        double capDist = Math.Sqrt(landArea / (double)kingdoms) * 0.75;
        Pick(WorldPlaceTypes.Capital, kingdoms, capDist);
        Pick(WorldPlaceTypes.City, c.Large ? c.Rng.Next(5, 8) : c.Rng.Next(1, 3), c.Large ? 18 : 14);
        Pick(WorldPlaceTypes.Town, c.Large ? c.Rng.Next(8, 12) : c.Rng.Next(2, 4), c.Large ? 12 : 10);
        Pick(WorldPlaceTypes.Village, c.Large ? c.Rng.Next(16, 24) : c.Rng.Next(5, 8), 7);
        return places;
    }

    // ── 5. Kingdoms ─────────────────────────── ───────────────────────────
    private static double MoveCost(Ctx c, int x, int y) => c.Biome[x, y] switch
    {
        WorldBiomes.Plains or WorldBiomes.Grass or WorldBiomes.Beach => 1,
        WorldBiomes.Forest or WorldBiomes.Tundra => 2,
        WorldBiomes.DeepForest or WorldBiomes.Hills or WorldBiomes.Desert or WorldBiomes.SnowForest => 3,
        WorldBiomes.Swamp or WorldBiomes.Snow => 4,
        WorldBiomes.Mountains => 9,
        WorldBiomes.Peaks => 25,
        _ => double.PositiveInfinity, // water
    };

    private static readonly List<int>[] KingdomColors =
    [
        [196, 72, 64], [70, 110, 196], [206, 168, 52], [120, 70, 168], [64, 156, 120], [196, 110, 50], [170, 70, 120],
    ];

    private static List<WorldKingdom> Kingdoms(Ctx c, List<WorldPlace> places, NameGen names)
    {
        c.Owner = new int[c.W, c.H];
        for (int x = 0; x < c.W; x++) for (int y = 0; y < c.H; y++) c.Owner[x, y] = -1;
        var capitals = places.Select((p, i) => (p, i)).Where(t => t.p.Type == WorldPlaceTypes.Capital).ToList();
        var kingdoms = capitals.Select((t, k) => new WorldKingdom
        {
            Name = names.Proper(),
            Color = [.. KingdomColors[k % KingdomColors.Length]],
            Capital = t.i,
        }).ToList();

        var cost = new double[c.W, c.H];
        for (int x = 0; x < c.W; x++) for (int y = 0; y < c.H; y++) cost[x, y] = double.PositiveInfinity;
        var pq = new PriorityQueue<(int x, int y, int k), double>();
        for (int k = 0; k < capitals.Count; k++)
        {
            var p = capitals[k].p;
            cost[p.X, p.Y] = 0;
            pq.Enqueue((p.X, p.Y, k), 0);
        }
        double maxCost = c.Large ? 120 : 90;
        while (pq.Count > 0)
        {
            var (x, y, k) = pq.Dequeue();
            if (c.Owner[x, y] >= 0) continue;
            c.Owner[x, y] = k;
            foreach (var n in Around(x, y))
            {
                if (!In(c, n) || c.Owner[n.x, n.y] >= 0) continue;
                double step = MoveCost(c, n.x, n.y);
                if (double.IsInfinity(step)) continue;
                if (c.River[n.x, n.y] >= 4) step += 2; // big rivers - border
                double nc = cost[x, y] + step;
                if (nc > maxCost || nc >= cost[n.x, n.y]) continue;
                cost[n.x, n.y] = nc;
                pq.Enqueue((n.x, n.y, k), nc);
            }
        }
        foreach (var p in places) p.Kingdom = c.Owner[p.X, p.Y];
        return kingdoms;
    }

    // ── 6. Roads ───────────────────────────── ──────────────────────────────
    private static void RoadsBetween(Ctx c, List<WorldPlace> places)
    {
        c.Road = new int[c.W, c.H];
        c.Wander = new double[c.W, c.H];
        for (int x = 0; x < c.W; x++)
            for (int y = 0; y < c.H; y++)
                // The coordinates of the noise are rotated: the grid noise has “cheap corridors” along the axes - the roads would go straight along them.
                c.Wander[x, y] = 0.35 + 2.4 * Math.Clamp((Fbm((x * 0.8 + y * 0.6) / 5.0, (y * 0.8 - x * 0.6) / 5.0, c.Seed + 71, 3) - 0.5) * 3 + 0.5, 0, 1);
        var big = places.Where(p => p.Type is WorldPlaceTypes.Capital or WorldPlaceTypes.City or WorldPlaceTypes.Town or WorldPlaceTypes.Port).ToList();
        // Roads: spanning tree for large settlements (along a straight line) - then laying out along the terrain.
        var connected = new List<WorldPlace>();
        var rest = big.OrderByDescending(p => p.Type == WorldPlaceTypes.Capital).ToList();
        if (rest.Count > 0) { connected.Add(rest[0]); rest.RemoveAt(0); }
        while (rest.Count > 0)
        {
            var (from, to) = rest.SelectMany(r => connected.Select(cn => (cn, r))).MinBy(e => Dist(e.cn.X, e.cn.Y, e.r.X, e.r.Y));
            Build(from, to, 2);
            connected.Add(to);
            rest.Remove(to);
        }
        // Paths to villages and adventure places - not in advance: they are laid when the place appears in the
        // game (GameWorld, WorldAtlas.TrailFrom).

        void Build(WorldPlace a, WorldPlace b, int level)
        {
            var path = PathFind(c, (a.X, a.Y), (b.X, b.Y));
            if (path == null) return;
            foreach (var (x, y) in path) c.Road[x, y] = Math.Max(c.Road[x, y], level);
        }
    }

    // Road path: moves along the sides and diagonals; price - terrain × smooth noise (the road winds, and does
    // not go straight with a right angle turn) + ascent (goes around the hills). The finished road is cheap
    // (the roads merge), the new crossing is expensive. As a result, the diagonal move is divided into two on
    // the sides through a cheaper cell (the roads on the map are connected on the sides, the ladder is drawn as
    // a straight inclined line).
    private static List<(int x, int y)>? PathFind(Ctx c, (int x, int y) from, (int x, int y) to)
    {
        double Cost(int x, int y, int px, int py)
        {
            double step = MoveCost(c, x, y);
            if (double.IsInfinity(step)) return step;
            if (c.Road[x, y] > 0) return 0.35;
            step = step * c.Wander[x, y] + 18 * Math.Max(0, c.Height[x, y] - c.Height[px, py]);
            if (c.River[x, y] > 0) step += 14;
            return step;
        }
        var dist = new Dictionary<(int, int), double> { [from] = 0 };
        var prev = new Dictionary<(int, int), (int, int)>();
        var pq = new PriorityQueue<(int x, int y), double>();
        pq.Enqueue(from, 0);
        int guard = c.W * c.H * 4;
        var done = new HashSet<(int, int)>();
        while (pq.Count > 0 && guard-- > 0)
        {
            var p = pq.Dequeue();
            if (!done.Add(p)) continue;
            if (p == to)
            {
                var path = new List<(int x, int y)> { p };
                while (prev.TryGetValue(p, out var q))
                {
                    if (q.Item1 != p.x && q.Item2 != p.y)
                    {
                        // Diagonal - through the cheaper of the two cells on the sides.
                        (int, int) a = (q.Item1, p.y), b = (p.x, q.Item2);
                        path.Add(Cost(a.Item1, a.Item2, p.x, p.y) <= Cost(b.Item1, b.Item2, p.x, p.y) ? a : b);
                    }
                    p = q;
                    path.Add(p);
                }
                return path;
            }
            double d = dist[p];
            foreach (var n in Around8(p.x, p.y))
            {
                if (!In(c, n)) continue;
                double step = Cost(n.x, n.y, p.x, p.y);
                if (double.IsInfinity(step)) continue;
                bool diag = n.x != p.x && n.y != p.y;
                if (diag)
                {
                    // You can cut a corner only if at least one cell on the sides is passable; across the river
                    // diagonally - like a crossing.
                    double a = Cost(n.x, p.y, p.x, p.y), b = Cost(p.x, n.y, p.x, p.y);
                    if (double.IsInfinity(a) && double.IsInfinity(b)) continue;
                    step = step * 1.41 + (c.River[n.x, p.y] > 0 && c.River[p.x, n.y] > 0 ? 14 : 0);
                }
                double nd = d + step;
                if (dist.TryGetValue(n, out var old) && old <= nd) continue;
                dist[n] = nd;
                prev[n] = p;
                pq.Enqueue(n, nd + Dist(n.x, n.y, to.x, to.y) * 0.5);
            }
        }
        return null;
    }

    // ── 7. Wilderness: places of adventure and fortresses ──────────────────────────────
    private static void Wilds(Ctx c, List<WorldPlace> places, NameGen names)
    {
        var settlements = places.Where(p => WorldPlaceTypes.IsSettlement(p.Type)).ToList();
        double Remote(int x, int y) => settlements.Count == 0 ? 99 : settlements.Min(p => Dist(p.X, p.Y, x, y));
        int total = c.Large ? c.Rng.Next(22, 30) : c.Rng.Next(7, 10);
        for (int attempt = 0; attempt < total * 60 && total > 0; attempt++)
        {
            int x = c.Rng.Next(2, c.W - 2), y = c.Rng.Next(2, c.H - 2);
            char b = c.Biome[x, y];
            if (WorldBiomes.IsWater(b) || b == WorldBiomes.Peaks || c.Road[x, y] > 0 || c.River[x, y] > 0) continue;
            if (Remote(x, y) < (c.Large ? 9 : 6)) continue;
            if (places.Any(p => Dist(p.X, p.Y, x, y) < 5)) continue;
            string type = b switch
            {
                WorldBiomes.Mountains or WorldBiomes.Hills => c.Rng.Next(3) == 0 ? WorldPlaceTypes.Dungeon : WorldPlaceTypes.Cave,
                WorldBiomes.Forest or WorldBiomes.DeepForest or WorldBiomes.SnowForest => c.Rng.Next(3) switch { 0 => WorldPlaceTypes.Shrine, 1 => WorldPlaceTypes.Ruins, _ => WorldPlaceTypes.Dungeon },
                WorldBiomes.Swamp => WorldPlaceTypes.Ruins,
                WorldBiomes.Desert => c.Rng.Next(2) == 0 ? WorldPlaceTypes.Ruins : WorldPlaceTypes.Dungeon,
                _ => c.Rng.Next(2) == 0 ? WorldPlaceTypes.Ruins : WorldPlaceTypes.Shrine,
            };
            places.Add(new WorldPlace { Type = type, X = x, Y = y, Kingdom = c.Owner[x, y], Name = names.Site(type) });
            total--;
        }
        // Fortresses - on the hills near the borders of the kingdoms.
        int castles = c.Large ? c.Rng.Next(5, 9) : c.Rng.Next(1, 3);
        for (int attempt = 0; attempt < castles * 200 && castles > 0; attempt++)
        {
            int x = c.Rng.Next(2, c.W - 2), y = c.Rng.Next(2, c.H - 2);
            int own = c.Owner[x, y];
            if (own < 0 || c.River[x, y] > 0 || c.Biome[x, y] is not (WorldBiomes.Hills or WorldBiomes.Plains or WorldBiomes.Grass)) continue;
            bool border = false;
            for (int dx = -4; dx <= 4 && !border; dx++)
                for (int dy = -4; dy <= 4 && !border; dy++)
                    if (In(c, (x + dx, y + dy)) && c.Owner[x + dx, y + dy] != own && !WorldBiomes.IsWater(c.Biome[x + dx, y + dy])) border = true;
            if (!border || places.Any(p => Dist(p.X, p.Y, x, y) < 6)) continue;
            places.Add(new WorldPlace { Type = WorldPlaceTypes.Castle, X = x, Y = y, Kingdom = own, Name = names.Site(WorldPlaceTypes.Castle) });
            castles--;
        }
    }

    // ── 8. Natural areas ──────────────────────── ────────────────────────
    private static List<WorldFeature> Features(Ctx c, NameGen names)
    {
        var list = new List<WorldFeature>();
        (Func<char, bool> match, char biome, int min)[] groups =
        [
            (b => b is WorldBiomes.Forest or WorldBiomes.DeepForest, WorldBiomes.Forest, c.Large ? 60 : 30),
            (b => b is WorldBiomes.Mountains or WorldBiomes.Peaks, WorldBiomes.Mountains, c.Large ? 30 : 14),
            (b => b == WorldBiomes.Hills, WorldBiomes.Hills, c.Large ? 60 : 30),
            (b => b == WorldBiomes.SnowForest, WorldBiomes.SnowForest, c.Large ? 50 : 24),
            (b => b == WorldBiomes.Swamp, WorldBiomes.Swamp, 10),
            (b => b == WorldBiomes.Desert, WorldBiomes.Desert, 40),
            (b => b == WorldBiomes.Lake, WorldBiomes.Lake, 4),
            (b => b is WorldBiomes.Tundra or WorldBiomes.Snow, WorldBiomes.Tundra, 50),
        ];
        foreach (var (match, biome, min) in groups)
            foreach (var region in Regions(c, (x, y) => match(c.Biome[x, y])))
            {
                if (region.Count < min) continue;
                double cx = region.Average(p => p.x), cy = region.Average(p => p.y);
                var anchor = region.MinBy(p => Dist(p.x, p.y, cx, cy));
                list.Add(new WorldFeature { Name = names.Feature(biome), Biome = biome, X = anchor.x, Y = anchor.y, Size = region.Count });
            }
        return list;
    }

    // ── Auxiliary ────────────────────────── ──────────────────────────
    private static IEnumerable<List<(int x, int y)>> Regions(Ctx c, Func<int, int, bool> match)
    {
        var seen = new bool[c.W, c.H];
        for (int x = 0; x < c.W; x++)
            for (int y = 0; y < c.H; y++)
            {
                if (seen[x, y] || !match(x, y)) continue;
                var region = new List<(int x, int y)>();
                var q = new Queue<(int x, int y)>([(x, y)]);
                seen[x, y] = true;
                while (q.Count > 0)
                {
                    var p = q.Dequeue();
                    region.Add(p);
                    foreach (var n in Around(p.x, p.y))
                        if (In(c, n) && !seen[n.x, n.y] && match(n.x, n.y)) { seen[n.x, n.y] = true; q.Enqueue(n); }
                }
                yield return region;
            }
    }

    private static bool In(Ctx c, (int x, int y) p) => p.x >= 0 && p.y >= 0 && p.x < c.W && p.y < c.H;
    private static (int x, int y)[] Around(int x, int y) => [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)];
    private static IEnumerable<(int x, int y)> Around8(int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (dx != 0 || dy != 0) yield return (x + dx, y + dy);
    }
    private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    private static double Fbm(double x, double y, int seed, int octaves)
    {
        double sum = 0, amp = 1, norm = 0, f = 1;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Noise(x * f, y * f, seed + i * 101);
            norm += amp;
            amp *= 0.5;
            f *= 2.03;
        }
        return sum / norm;
    }

    private static double Noise(double x, double y, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        double a = Lattice(x0, y0, seed), b = Lattice(x0 + 1, y0, seed);
        double c = Lattice(x0, y0 + 1, seed), d = Lattice(x0 + 1, y0 + 1, seed);
        double top = a + (b - a) * fx, bottom = c + (d - c) * fx;
        return top + (bottom - top) * fy;
    }

    private static double Lattice(int x, int y, int seed)
    {
        uint h = unchecked((uint)(x * 374761393 + y * 668265263 + seed * 982451653));
        h = unchecked((h ^ (h >> 13)) * 1274126177);
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (double)0xFFFFFF;
    }

    // ── Names (temporal, syllabic) - in the language of the world ──────────────────────────
    private sealed class NameGen(Random rng, string language)
    {
        private readonly HashSet<string> _used = [];
        private readonly bool En = language == L.English;
        private static readonly string[] Starts = ["Ар", "Вел", "Кал", "Дор", "Мир", "Тар", "Эл", "Гра", "Бри", "Сол", "Ост", "Лор", "Зар", "Хел", "Нор", "Фал", "Вин", "Тор", "Кер", "Ам", "Ил", "Эр", "Мор", "Бел", "Сар", "Гол", "Дан", "Лим", "Рав", "Кор", "Ул", "Ван", "Сел", "Тил", "Гер"];
        private static readonly string[] Mids = ["а", "е", "и", "о", "ан", "ен", "ор", "ар", "ил", "ер", "", "", ""];
        private static readonly string[] Ends = ["дор", "мор", "ин", "ель", "град", "ск", "ен", "ар", "ос", "вин", "рет", "дан", "гард", "ла", "ия", "ор", "им", "ест", "ан", "ход", "мир"];
        private static readonly string[] VillageEnds = ["овка", "ино", "ище", "ец", "ки", "ово", "ня", "ичи", "ка"];
        private static readonly string[] VillageStarts = ["Берёз", "Ольх", "Медв", "Лис", "Сосн", "Камен", "Озёр", "Рыб", "Мельн", "Глин", "Ключ", "Вереш", "Боров", "Ясен", "Красн", "Тих", "Грязн", "Сух", "Заболот", "Полян"];
        private static readonly string[] StartsEn = ["Ar", "Vel", "Kal", "Dor", "Mir", "Tar", "El", "Gra", "Bri", "Sol", "Ost", "Lor", "Zar", "Hel", "Nor", "Fal", "Vin", "Tor", "Ker", "Am", "Il", "Er", "Mor", "Bel", "Sar", "Gol", "Dan", "Lim", "Rav", "Cor", "Ul", "Van", "Sel", "Til", "Ger"];
        private static readonly string[] MidsEn = ["a", "e", "i", "o", "an", "en", "or", "ar", "il", "er", "", "", ""];
        private static readonly string[] EndsEn = ["dor", "mor", "in", "el", "grad", "sk", "en", "ar", "os", "vin", "ret", "dan", "gard", "la", "ia", "or", "im", "est", "an", "hold", "mir"];
        private static readonly string[] VillageEndsEn = ["ford", "ton", "wick", "dale", "by", "ham", "stead", "brook", "field"];
        private static readonly string[] VillageStartsEn = ["Birch", "Alder", "Bear", "Fox", "Pine", "Stone", "Lake", "Fish", "Mill", "Clay", "Spring", "Heath", "Oak", "Ash", "Red", "Still", "Mud", "Dry", "Marsh", "Glade"];

        public string Proper()
        {
            var (starts, mids, ends) = En ? (StartsEn, MidsEn, EndsEn) : (Starts, Mids, Ends);
            for (int i = 0; i < 50; i++)
            {
                string n = starts[rng.Next(starts.Length)] + mids[rng.Next(mids.Length)] + ends[rng.Next(ends.Length)];
                if (n.Length is >= 4 and <= 11 && _used.Add(n)) return n;
            }
            return En ? "Nameless" : "Безымянное";
        }

        public string Village()
        {
            var (starts, ends) = En ? (VillageStartsEn, VillageEndsEn) : (VillageStarts, VillageEnds);
            for (int i = 0; i < 50; i++)
            {
                string n = starts[rng.Next(starts.Length)] + ends[rng.Next(ends.Length)];
                if (_used.Add(n)) return n;
            }
            return Proper();
        }

        public string Site(string type)
        {
            string proper = Proper();
            if (En)
                return type switch
                {
                    WorldPlaceTypes.Ruins => Pick("Ruins of", "Remnants of") + " " + proper,
                    WorldPlaceTypes.Dungeon => Pick("Crypt of", "Dungeon of", "Catacombs of") + " " + proper,
                    WorldPlaceTypes.Cave => Pick("Cave of", "Grotto of", "Lair of") + " " + proper,
                    WorldPlaceTypes.Shrine => Pick("Shrine of", "Altar of", "Sanctuary of") + " " + proper,
                    WorldPlaceTypes.Castle => Pick("Fortress", "Castle", "Fort") + " " + proper,
                    _ => proper,
                };
            return type switch
            {
                WorldPlaceTypes.Ruins => Pick("Руины", "Развалины") + " " + proper,
                WorldPlaceTypes.Dungeon => Pick("Склеп", "Подземелье", "Катакомбы") + " " + proper,
                WorldPlaceTypes.Cave => Pick("Пещера", "Грот", "Логово") + " " + proper,
                WorldPlaceTypes.Shrine => Pick("Святилище", "Алтарь", "Капище") + " " + proper,
                WorldPlaceTypes.Castle => Pick("Крепость", "Замок", "Форт") + " " + proper,
                _ => proper,
            };
        }

        public string Island(int size)
        {
            string Adj(params string[] a) => a[rng.Next(a.Length)];
            if (En)
                return size < 25
                    ? rng.Next(2) == 0 ? $"{Adj("Misty", "Gull", "Rocky", "Nameless", "Lonely", "Seal")} Isle" : $"Isle of {Proper()}"
                    : rng.Next(2) == 0 ? $"{Adj("Green", "Far", "Stone", "Northern", "Southern", "Windy")} Island" : $"Island of {Proper()}";
            return size < 25
                ? rng.Next(2) == 0 ? $"{Adj("Туманный", "Чаячий", "Скалистый", "Безымянный", "Одинокий", "Тюлений")} остров" : $"Остров {Proper()}"
                : rng.Next(2) == 0 ? $"{Adj("Зелёный", "Дальний", "Каменный", "Северный", "Южный", "Ветреный")} остров" : $"Остров {Proper()}";
        }

        public string Feature(char biome)
        {
            string proper = Proper();
            string Adj(params string[] a) => a[rng.Next(a.Length)];
            if (En)
                return biome switch
                {
                    WorldBiomes.Forest => rng.Next(2) == 0 ? $"{Adj("Dark", "Old", "Whispering", "Wild", "Gloomy", "Green")} Wood" : $"{proper} Forest",
                    WorldBiomes.Mountains => rng.Next(2) == 0 ? $"{Adj("Grey", "Northern", "Dragon", "Stone", "Hoary", "Iron")} Mountains" : $"{proper} Mountains",
                    WorldBiomes.Hills => rng.Next(2) == 0 ? $"{Adj("Windy", "Green", "Barrow", "Rolling", "Sheep")} Hills" : $"{proper} Hills",
                    WorldBiomes.Swamp => rng.Next(2) == 0 ? $"{Adj("Rotting", "Misty", "Black", "Midge")} Fens" : $"{proper} Marshes",
                    WorldBiomes.Desert => rng.Next(2) == 0 ? $"{Adj("Red", "Scorched", "Salt", "Dead")} Sands" : $"{proper} Desert",
                    WorldBiomes.Lake => rng.Next(2) == 0 ? $"{Adj("Mirror", "Deep", "Still", "Moon", "Blue")} Lake" : $"Lake {proper}",
                    WorldBiomes.Tundra => $"{Adj("White", "Icy", "Frozen")} Lands",
                    WorldBiomes.SnowForest => rng.Next(2) == 0 ? $"{Adj("White", "Winter", "Northern", "Icy", "Hoary")} Pinewood" : $"{proper} Taiga",
                    _ => proper,
                };
            return biome switch
            {
                WorldBiomes.Forest => rng.Next(2) == 0 ? $"{Adj("Тёмный", "Старый", "Шепчущий", "Дикий", "Сумрачный", "Зелёный")} лес" : $"Лес {proper}",
                WorldBiomes.Mountains => rng.Next(2) == 0 ? $"{Adj("Серые", "Северные", "Драконьи", "Каменные", "Седые", "Железные")} горы" : $"Горы {proper}",
                WorldBiomes.Hills => rng.Next(2) == 0 ? $"{Adj("Ветреные", "Зелёные", "Курганные", "Пологие", "Овечьи")} холмы" : $"Холмы {proper}",
                WorldBiomes.Swamp => rng.Next(2) == 0 ? $"{Adj("Гнилые", "Туманные", "Чёрные", "Комариные")} топи" : $"Болота {proper}",
                WorldBiomes.Desert => rng.Next(2) == 0 ? $"{Adj("Красные", "Выжженные", "Солёные", "Мёртвые")} пески" : $"Пустыня {proper}",
                WorldBiomes.Lake => rng.Next(2) == 0 ? $"{Adj("Зеркальное", "Глубокое", "Тихое", "Лунное", "Синее")} озеро" : $"Озеро {proper}",
                WorldBiomes.Tundra => $"{Adj("Белые", "Ледяные", "Мёрзлые")} земли",
                WorldBiomes.SnowForest => rng.Next(2) == 0 ? $"{Adj("Белый", "Зимний", "Северный", "Ледяной", "Седой")} бор" : $"Тайга {proper}",
                _ => proper,
            };
        }

        private string Pick(params string[] a) => a[rng.Next(a.Length)];
    }
}
