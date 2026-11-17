using NaviDnD.Data.Models;

namespace NaviDnD.MapGen.Generators;

// Block of open terrain (forest, plain, hills/mountains, swamp): without rooms and walls, cells - terrain
// (TerrainCatalog). Figure - based on noise from field coordinates (adjacent blocks are joined without a seam),
// edges without a neighboring terrain are closed by a natural barrier (thicket, rocks, water), from each exit
// to the center there is a path; unreachable pockets are connected by fords/clearings.
public static class OutdoorGenerator
{
    public sealed record Result(List<string> Terrain, List<Door> Doors);

    // River through the location (according to the world map): across the field along the axis, center -
    // row/column of the field, width of deep water in the cells (shallow water along the edges); meanders
    // through the noise. The paths cross it with a ford.
    public sealed record RiverSpec(bool Horizontal, int Center, int Width, int Seed);

    private const int W = MapChunk.Cols, H = MapChunk.Rows;

    // bands: side → code for an obstacle along the edge (no side - the edge is open: there is an adjacent terrain block).
    public static Result Generate(MapChunk chunk, IReadOnlyList<ChunkExit> exits, IReadOnlyDictionary<ChunkSide, char> bands, int noiseSeed,
        RiverSpec? river = null)
    {
        var rng = new Random(chunk.Seed);
        var g = new char[W, H];
        string theme = chunk.Theme;
        char ground = theme switch { "mountain" => 's', "swamp" => ',', "desert" => 'a', "snow" or "taiga" => 'n', _ => '.' };

        for (int c = 0; c < W; c++)
            for (int r = 0; r < H; r++)
            {
                int fc = chunk.OriginCol + c + 1, fr = chunk.OriginRow + r + 1;
                // Large masses establish readable groves/ridges; fine noise only breaks their edges.
                double n = 0.72 * Noise(fc / 8.0, fr / 6.0, noiseSeed) + 0.28 * Fbm(fc, fr, noiseSeed + 19);
                double m = Fbm(fc, fr, noiseSeed + 101);
                double p = Lattice(fc, fr, noiseSeed + 211);
                g[c, r] = theme switch
                {
                    "forest" => n > 0.68 ? 'F' : n > 0.53 ? (p < 0.78 ? 'T' : '"')
                        : n > 0.46 && m > 0.45 ? '"' : m > 0.57 ? ',' : p < 0.025 ? 'o' : '.',
                    "plains" => n > 0.73 ? (p < 0.65 ? 'T' : '"') : n > 0.64 ? '"'
                        : m > 0.54 ? ',' : p < 0.015 ? 'o' : '.',
                    "hills" or "mountain" => n > 0.65 ? '^' : n > 0.53 ? 'g'
                        : m > 0.65 ? (theme == "hills" ? 'T' : 'o')
                        : m > 0.52 ? (theme == "hills" ? ',' : 'g') : ground,
                    "swamp" => n > 0.66 ? 'W' : n > 0.56 ? '~' : n > 0.48 ? ':'
                        : m > 0.66 ? 'T' : m > 0.58 ? '"' : m > 0.43 ? ',' : '.',
                    "desert" => n > 0.59 ? 'd' : m > 0.74 ? 'g' : m > 0.61 && p < 0.2 ? 'c' : p < 0.015 ? 'o' : 'a',
                    "snow" => n > 0.73 ? '^' : n > 0.65 ? 'g' : m > 0.7 ? 'e' : 'n',
                    "taiga" => n > 0.57 ? 'e' : n > 0.48 && p < 0.35 ? 'e' : m > 0.65 ? '"' : 'n',
                    _ => ground,
                };
            }

        AddOutcrop(g, chunk);

        // Edges without a terrain neighbor: 1-3 tile obstruction strip.
        foreach (var (side, code) in bands)
        {
            int len = side is ChunkSide.Left or ChunkSide.Right ? H : W;
            for (int i = 0; i < len; i++)
            {
                var (bc, br) = SideCell(side, i, 0);
                double n = Fbm(chunk.OriginCol + bc + 1, chunk.OriginRow + br + 1, noiseSeed + 7);
                int depth = 1 + (n > 0.45 ? 1 : 0) + (n > 0.7 ? 1 : 0);
                for (int d = 0; d < depth; d++)
                {
                    var (c, r) = SideCell(side, i, d);
                    g[c, r] = code;
                }
            }
        }

        if (river != null) StampRiver(g, chunk, bands, noiseSeed, river, ground);

        // Prefer a naturally open meeting place, offset between seeds instead of a repeated central cross.
        int preferredC = rng.Next(5, W - 5), preferredR = rng.Next(4, H - 4);
        var hub = (from c in Enumerable.Range(3, W - 6)
                   from r in Enumerable.Range(3, H - 6)
                   orderby (Kind(g[c, r]).BlocksMove ? 12 : 0)
                       + Math.Abs(c - preferredC) + Math.Abs(r - preferredR)
                   select (c, r)).First();
        var (hubC, hubR) = hub;
        if (Kind(g[hubC, hubR]).BlocksMove) g[hubC, hubR] = g[hubC, hubR] == 'W' ? '~' : ground;
        char trail = theme is "mountain" or "hills" ? 's' : theme == "swamp" ? '.' : theme == "desert" ? 'a' : theme is "snow" or "taiga" ? 'n' : '=';
        var originalGround = (char[,])g.Clone();
        var trailCells = new HashSet<(int c, int r)>();
        var anchors = new HashSet<(int c, int r)>();
        foreach (var exit in exits)
        {
            var (ac, ar) = exit.AnchorCell(chunk);
            var anchor = (ac - chunk.OriginCol - 1, ar - chunk.OriginRow - 1);
            anchors.Add(anchor);
            Carve(g, anchor.Item1, anchor.Item2, hubC, hubR, trail, trailCells);
        }

        // Paths may meet before reaching the planning hub, leaving a meaningless spur beyond
        // their junction. Keep the network between real exits; a single-entry clearing keeps its approach.
        if (anchors.Count > 1) TrimTrailSpurs(g, originalGround, trailCells, anchors);
        var floodStart = anchors.Count > 0 ? anchors.First() : (hubC, hubR);
        ConnectPockets(g, floodStart.Item1, floodStart.Item2, ground);
        if (anchors.Count > 1) AddNaturalPass(g, trailCells, chunk.Theme, chunk.Seed);

        // Keep rocky route planning intact, but distinguish the worn trail from ordinary stony ground.
        if (theme is "hills" or "mountain")
            foreach (var (c, r) in trailCells)
                if (g[c, r] == 's') g[c, r] = '=';
        // Footprints distinguish the retained route without changing snow movement costs or fords.
        if (theme is "snow" or "taiga")
            foreach (var (c, r) in trailCells)
                if (g[c, r] == 'n') g[c, r] = 't';

        // Entrance from outside - exit door to the world map at the edge (LocationGrower.MarkWorldExit makes it purple).
        var doors = new List<Door>();
        foreach (var exit in exits.Where(e => e.External))
        {
            var (ac, ar) = exit.AnchorCell(chunk);
            var (dx, dy) = ChunkExit.Step(exit.Side);
            doors.Add(new Door { From = [ac, ar], To = [ac + dx, ar + dy], IsEntry = true, IsDoor = false });
        }

        var rows = new List<string>(H);
        for (int r = 0; r < H; r++)
        {
            var line = new char[W];
            for (int c = 0; c < W; c++) line[c] = g[c, r];
            rows.Add(new string(line));
        }
        return new Result(rows, doors);
    }

    internal static void StampRiver(char[,] g, MapChunk chunk, IReadOnlyDictionary<ChunkSide, char> bands,
        int noiseSeed, RiverSpec river, char ground)
    {
        // River across a field: deep water in the middle, shallow water at the edges.
        for (int i = 0; i < (river.Horizontal ? W : H); i++)
        {
            int along = river.Horizontal ? chunk.OriginCol + i + 1 : chunk.OriginRow + i + 1;
            int center = river.Center + (int)Math.Round(3 * Math.Sin(along / 6.0 + river.Seed % 7) + 3 * (Fbm(along, river.Seed % 997, noiseSeed + 33) - 0.5));
            for (int k = -river.Width / 2 - 2; k <= (river.Width - 1) / 2 + 2; k++)
            {
                int across = center + k;
                int local = across - (river.Horizontal ? chunk.OriginRow : chunk.OriginCol) - 1;
                if (local < 0 || local >= (river.Horizontal ? H : W)) continue;
                bool edge = k == -river.Width / 2 - 1 || k == (river.Width - 1) / 2 + 1;
                int c = river.Horizontal ? i : local, r = river.Horizontal ? local : i;
                bool bank = k == -river.Width / 2 - 2 || k == (river.Width - 1) / 2 + 2;
                // A narrow open bank makes the channel readable and gives explorers room
                // to follow it. Preserve closed boundaries and naturally occurring water.
                if (bank)
                {
                    bool boundary = c < 3 && bands.ContainsKey(ChunkSide.Left)
                        || c >= W - 3 && bands.ContainsKey(ChunkSide.Right)
                        || r < 3 && bands.ContainsKey(ChunkSide.Bottom)
                        || r >= H - 3 && bands.ContainsKey(ChunkSide.Top);
                    if (!boundary && g[c, r] is not ('W' or '~'))
                        g[c, r] = ground;
                    continue;
                }
                // At the closed border, the channel goes beyond the map with deep water, without a tree bridge.
                bool closedEdge = (c == 0 && bands.ContainsKey(ChunkSide.Left))
                    || (c == W - 1 && bands.ContainsKey(ChunkSide.Right))
                    || (r == 0 && bands.ContainsKey(ChunkSide.Bottom))
                    || (r == H - 1 && bands.ContainsKey(ChunkSide.Top));
                g[c, r] = edge && !closedEdge ? '~' : 'W';
            }
        }

    }

    private static TerrainKind Kind(char code) => TerrainCatalog.Get(code) ?? TerrainCatalog.Kinds['.'];

    // Occasional exposed rock gives a place a recognizable landmark and usable cover.
    // Keep it inside the chunk; boundary bands and the world river are applied afterwards.
    private static void AddOutcrop(char[,] g, MapChunk chunk)
    {
        if (chunk.Theme is not ("forest" or "plains" or "hills" or "mountain") || (uint)chunk.Seed % 4 != 3) return;
        var rng = new Random(unchecked(chunk.Seed ^ 0x51A7B));
        int cc = rng.Next(5, W - 5), rr = rng.Next(4, H - 4);
        double radiusC = 2.5 + rng.NextDouble(), radiusR = 1.5 + rng.NextDouble();
        for (int c = cc - 4; c <= cc + 4; c++)
            for (int r = rr - 3; r <= rr + 3; r++)
            {
                double distance = Math.Pow((c - cc) / radiusC, 2) + Math.Pow((r - rr) / radiusR, 2);
                double edge = 0.85 + 0.3 * Lattice(chunk.OriginCol + c, chunk.OriginRow + r, chunk.Seed);
                if (distance < 0.5 && ((c == cc && r == rr)
                    || Lattice(chunk.OriginCol + c, chunk.OriginRow + r, unchecked(chunk.Seed + 37)) > 0.4)) g[c, r] = 'o';
                else if (distance < edge) g[c, r] = distance < 0.65 ? 'g' : 's';
            }
    }

    // Cell at a distance depth inward from the side, i along the side (local coordinates 0-based).
    private static (int c, int r) SideCell(ChunkSide side, int i, int depth) => side switch
    {
        ChunkSide.Left => (depth, i),
        ChunkSide.Right => (W - 1 - depth, i),
        ChunkSide.Bottom => (i, depth),
        _ => (i, H - 1 - depth),
    };

    private static IEnumerable<(int c, int r)> Neighbors(int c, int r)
    {
        if (c > 0) yield return (c - 1, r);
        if (c + 1 < W) yield return (c + 1, r);
        if (r > 0) yield return (c, r - 1);
        if (r + 1 < H) yield return (c, r + 1);
    }

    // A low-cost route follows clear ground and existing trails; crossing water remains a ford.
    // Blocked perimeter cells are never used as shortcuts through the location boundary.
    private static void Carve(char[,] g, int sc, int sr, int tc, int tr, char trail, HashSet<(int c, int r)> trailCells)
    {
        // A corner exit needs one edge step before it can reach the interior.
        if ((sc == 0 || sc == W - 1) && (sr == 0 || sr == H - 1))
        {
            g[sc, sr] = g[sc, sr] is 'W' or '~' ? '~' : trail;
            trailCells.Add((sc, sr));
            sc += sc == 0 ? 1 : -1;
            g[sc, sr] = g[sc, sr] is 'W' or '~' ? '~' : trail;
            trailCells.Add((sc, sr));
        }
        var goals = new bool[W, H];
        goals[tc, tr] = true;
        foreach (var (c, r) in FindRoute(g, [(sc, sr)], goals))
        {
            g[c, r] = g[c, r] is 'W' or '~' ? '~' : trail;
            trailCells.Add((c, r));
        }
    }

    private static void TrimTrailSpurs(char[,] ground, char[,] original,
        HashSet<(int c, int r)> trail, HashSet<(int c, int r)> exits)
    {
        var leaves = new Queue<(int c, int r)>(trail.Where(cell => !exits.Contains(cell)
            && Neighbors(cell.c, cell.r).Count(trail.Contains) <= 1));
        while (leaves.TryDequeue(out var cell))
        {
            if (exits.Contains(cell) || !trail.Contains(cell) || Neighbors(cell.c, cell.r).Count(trail.Contains) > 1) continue;
            trail.Remove(cell);
            ground[cell.c, cell.r] = original[cell.c, cell.r];
            foreach (var neighbor in Neighbors(cell.c, cell.r).Where(trail.Contains)) leaves.Enqueue(neighbor);
        }
    }

    // Occasionally expose a short, difficult passage through the edge of a grove/ridge beside a long trail bend.
    // Never add a loop on empty ground: the new route must open 1-3 consecutive obstructed cells.
    internal static IReadOnlyList<(int c, int r)> AddNaturalPass(char[,] g,
        IReadOnlySet<(int c, int r)> trail, string theme, int seed)
    {
        if (theme is not ("forest" or "hills" or "mountain")) return [];
        var rng = new Random(unchecked(seed ^ 0x73D59B));
        if (rng.Next(4) != 0) return [];
        bool Interior((int c, int r) p) => p.c >= 3 && p.c < W - 3 && p.r >= 3 && p.r < H - 3;
        static int Distance((int c, int r) a, (int c, int r) b) => Math.Abs(a.c - b.c) + Math.Abs(a.r - b.r);
        bool CanOpen(char code) => theme == "forest" ? code is 'T' or 'F' : code == '^';
        var anchors = trail.Where(Interior).OrderBy(p => p.r).ThenBy(p => p.c).ToList();
        var candidates = new List<((int c, int r) a, (int c, int r) b, int detour, int tie)>();
        for (int i = 0; i < anchors.Count; i++)
        {
            var distances = new Dictionary<(int c, int r), int> { [anchors[i]] = 0 };
            var queue = new Queue<(int c, int r)>([anchors[i]]);
            while (queue.TryDequeue(out var p))
                foreach (var n in Neighbors(p.c, p.r))
                    if (trail.Contains(n) && distances.TryAdd(n, distances[p] + 1)) queue.Enqueue(n);
            for (int j = i + 1; j < anchors.Count; j++)
            {
                int direct = Distance(anchors[i], anchors[j]);
                if (direct is < 2 or > 6 || !distances.TryGetValue(anchors[j], out int along) || along < direct + 4) continue;
                candidates.Add((anchors[i], anchors[j], along - direct, rng.Next()));
            }
        }
        // At most 24 small monotone searches (each inside a <=7x7 rectangle, four obstacle-count states).
        foreach (var (a, b, _, _) in candidates.OrderByDescending(p => p.detour).ThenBy(p => p.tie).Take(24))
        {
            var start = (c: a.c, r: a.r, cuts: 0);
            var previous = new Dictionary<(int c, int r, int cuts), (int c, int r, int cuts)> { [start] = start };
            var queue = new Queue<(int c, int r, int cuts)>([start]);
            (int c, int r, int cuts)? best = null;
            bool alreadyOpen = false;
            while (queue.TryDequeue(out var p))
            {
                if ((p.c, p.r) == b)
                {
                    if (p.cuts == 0) { alreadyOpen = true; break; }
                    if (best == null || p.cuts < best.Value.cuts) best = p;
                    continue;
                }
                foreach (var n in Neighbors(p.c, p.r))
                {
                    if (!Interior(n) || Distance(n, b) >= Distance((p.c, p.r), b)
                        || (n != b && trail.Contains(n)) || g[n.c, n.r] is 'W' or '~') continue;
                    bool blocked = Kind(g[n.c, n.r]).BlocksMove;
                    if (blocked && !CanOpen(g[n.c, n.r])) continue;
                    int cuts = p.cuts + (blocked ? 1 : 0);
                    var next = (n.c, n.r, cuts);
                    if (cuts <= 3 && previous.TryAdd(next, p)) queue.Enqueue(next);
                }
            }
            if (alreadyOpen || best == null) continue;
            var route = new List<(int c, int r)>();
            var cursor = best.Value;
            while (cursor != start)
            {
                route.Add((cursor.c, cursor.r));
                cursor = previous[cursor];
            }
            var opened = route.Where(p => Kind(g[p.c, p.r]).BlocksMove).ToList();
            int first = route.FindIndex(p => Kind(g[p.c, p.r]).BlocksMove);
            int last = route.FindLastIndex(p => Kind(g[p.c, p.r]).BlocksMove);
            if (opened.Count is < 1 or > 3 || last - first + 1 != opened.Count) continue;
            foreach (var p in opened) g[p.c, p.r] = theme == "forest" ? '"' : 'g';
            return opened;
        }
        return [];
    }

    private static List<(int c, int r)> FindRoute(char[,] g, IEnumerable<(int c, int r)> starts, bool[,] goals)
    {
        var costs = new int[W, H];
        var previous = new (int c, int r)[W, H];
        var queue = new PriorityQueue<(int c, int r), (int cost, int order)>();
        int order = 0;
        for (int c = 0; c < W; c++)
            for (int r = 0; r < H; r++) costs[c, r] = int.MaxValue;
        foreach (var start in starts)
        {
            costs[start.c, start.r] = 0;
            previous[start.c, start.r] = start;
            queue.Enqueue(start, (0, order++));
        }
        while (queue.TryDequeue(out var current, out var priority))
        {
            var (c, r) = current;
            if (priority.cost != costs[c, r]) continue;
            if (goals[c, r])
            {
                var route = new List<(int c, int r)> { current };
                while (previous[c, r] != (c, r))
                {
                    (c, r) = previous[c, r];
                    route.Add((c, r));
                }
                return route;
            }
            foreach (var (nc, nr) in Neighbors(c, r))
            {
                var kind = Kind(g[nc, nr]);
                if (kind.BlocksMove && (nc == 0 || nr == 0 || nc == W - 1 || nr == H - 1)) continue;
                int step = g[nc, nr] == '=' ? 2 : kind.BlocksMove ? 24 : kind.StepCostFt > 5 ? 6 : 4;
                int cost = costs[c, r] + step;
                if (cost >= costs[nc, nr]) continue;
                costs[nc, nr] = cost;
                previous[nc, nr] = current;
                queue.Enqueue((nc, nr), (cost, order++));
            }
        }
        return [];
    }

    // Connect the nearest isolated region with the smallest practical cut, preserving groves and riverbanks.
    private static void ConnectPockets(char[,] g, int hubC, int hubR, char ground)
    {
        for (int iteration = 0; iteration < W * H; iteration++)
        {
            var reached = Flood(g, hubC, hubR);
            var goals = new bool[W, H];
            var starts = new List<(int c, int r)>();
            bool hasPocket = false;
            for (int c = 0; c < W; c++)
                for (int r = 0; r < H; r++)
                {
                    if (reached[c, r]) starts.Add((c, r));
                    else if (!Kind(g[c, r]).BlocksMove) { goals[c, r] = true; hasPocket = true; }
                }
            if (!hasPocket) return;
            var route = FindRoute(g, starts, goals);
            if (route.Count == 0) return;
            foreach (var (c, r) in route)
                if (Kind(g[c, r]).BlocksMove) g[c, r] = g[c, r] == 'W' ? '~' : ground;
        }
    }

    private static bool[,] Flood(char[,] g, int sc, int sr)
    {
        var seen = new bool[W, H];
        if (Kind(g[sc, sr]).BlocksMove) return seen;
        var queue = new Queue<(int, int)>([(sc, sr)]);
        seen[sc, sr] = true;
        while (queue.Count > 0)
        {
            var (c, r) = queue.Dequeue();
            foreach (var (nc, nr) in new[] { (c + 1, r), (c - 1, r), (c, r + 1), (c, r - 1) })
            {
                if (nc < 0 || nr < 0 || nc >= W || nr >= H || seen[nc, nr] || Kind(g[nc, nr]).BlocksMove) continue;
                seen[nc, nr] = true;
                queue.Enqueue((nc, nr));
            }
        }
        return seen;
    }

    // Field coordinate noise: two octaves of smoothed grating value.
    private static double Fbm(int x, int y, int seed) => 0.65 * Noise(x / 5.0, y / 5.0, seed) + 0.35 * Noise(x / 2.3, y / 2.3, seed + 1);

    private static double Noise(double x, double y, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        double a = Lattice(x0, y0, seed), b = Lattice(x0 + 1, y0, seed);
        double c = Lattice(x0, y0 + 1, seed), d = Lattice(x0 + 1, y0 + 1, seed);
        return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
    }

    private static double Lattice(int x, int y, int seed)
    {
        uint h = unchecked((uint)(x * 374761393 + y * 668265263 + seed * 982451653));
        h = unchecked((h ^ (h >> 13)) * 1274126177);
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (double)0xFFFFFF;
    }
}
