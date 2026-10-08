using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Models;

namespace NaviDnD.MapGen.Generators;

public class DungeonGenerator : IMapGenerator
{
    // Размер карты из запроса (MapGenRequest.Cols/Rows, до MapConfig.MaxSize). Вся раскладка (зоны,
    // размещение, коридоры) считается от него. Методы статические — значение на время одного Generate
    // в поле потока, вне вызова — прежние 20×15.
    [ThreadStatic] private static int _cols;
    [ThreadStatic] private static int _rows;
    private static int MaxCols => _cols > 0 ? _cols : 20;
    private static int MaxRows => _rows > 0 ? _rows : 15;

    private static readonly Dictionary<RoomSize, (int minW, int maxW, int minH, int maxH)> SizeBounds = new()
    {
        [RoomSize.Small]  = (3, 4,  3, 4),
        [RoomSize.Medium] = (4, 6,  4, 6),
        [RoomSize.Large]  = (6, 8,  5, 7),
        [RoomSize.Huge]   = (8, 10, 6, 8),
    };

    private record Cell(int Col, int Row);

    private record Box(int Col, int Row, int W, int H)
    {
        public int Right  => Col + W - 1;
        public int Bottom => Row + H - 1;
        // gap=1  → rooms must be 2+ cells apart
        // gap=0  → rooms must not share cells (adjacent OK)
        // gap=-1 → rooms must have 1+ cell gap (strict)
        public bool Overlaps(Box o, int gap = 1) =>
            Col    <= o.Right  + gap &&
            Right  >= o.Col    - gap &&
            Row    <= o.Bottom + gap &&
            Bottom >= o.Row    - gap;
    }

    // ── Generate ─────────────────────────────────────────────────────────────

    public MapConfig Generate(MapGenRequest request)
    {
        _cols = Math.Clamp(request.Cols, 5, MapConfig.MaxSize);
        _rows = Math.Clamp(request.Rows, 5, MapConfig.MaxSize);
        try { return GenerateSized(request); }
        finally { _cols = 0; _rows = 0; }
    }

    private MapConfig GenerateSized(MapGenRequest request)
    {
        var dungeon = request.Dungeon ?? new DungeonRequest();
        var rng = new Random(request.Seed ?? Environment.TickCount);

        var nameToIndex = dungeon.Rooms
            .Select((r, i) => (r.Name, i))
            .ToDictionary(x => x.Name, x => x.i, StringComparer.OrdinalIgnoreCase);

        // Placement: Opening pairs get forced-adjacent boxes
        var boxes = PlaceRooms(dungeon.Rooms, dungeon.Passages, nameToIndex, rng);
        var cells = BuildCells(dungeon.Rooms, boxes, rng);

        var corridorCells = new HashSet<(int Col, int Row)>();
        var doors = new List<Door>();

        foreach (var passage in dungeon.Passages)
        {
            if (!nameToIndex.TryGetValue(passage.From, out int fi)) continue;
            if (!nameToIndex.TryGetValue(passage.To,   out int ti)) continue;
            Connect(fi, ti, cells, passage, corridorCells, doors, rng);
        }

        foreach (var transition in dungeon.Transitions)
        {
            if (!nameToIndex.TryGetValue(transition.Room, out int ri)) continue;
            ConnectTransition(ri, transition, cells, corridorCells, doors, rng);
        }

        var rooms = dungeon.Rooms
            .Select((r, i) => new Room
            {
                Name      = r.Name,
                Color     = PickColor(r.Name, rng),
                Positions = cells[i].Select(c => (List<int>)[c.Col, c.Row]).ToList(),
            })
            .ToList();

        if (corridorCells.Count > 0)
        {
            // Safety: exclude any cell already claimed by a named room
            var namedCells = new HashSet<(int Col, int Row)>(
                cells.SelectMany(s => s.Select(c => (c.Col, c.Row))));

            var corridorOnly = corridorCells
                .Where(c => !namedCells.Contains(c))
                .ToList();

            if (corridorOnly.Count > 0)
                rooms.Add(new Room
                {
                    Name      = "Corridor",
                    Passage   = true,
                    Color     = [55, 55, 65],
                    Positions = corridorOnly.Select(c => (List<int>)[c.Col, c.Row]).ToList(),
                });
        }

        // Remove duplicate door pairs (From/To == To/From)
        var seenDoors = new HashSet<(int, int, int, int)>();
        doors = doors.Where(d =>
        {
            int f0 = d.From[0], f1 = d.From[1], t0 = d.To[0], t1 = d.To[1];
            var key = f0 < t0 || (f0 == t0 && f1 <= t1)
                ? (f0, f1, t0, t1) : (t0, t1, f0, f1);
            return seenDoors.Add(key);
        }).ToList();

        return new MapConfig
        {
            Cols   = MaxCols,
            Rows   = MaxRows,
            Colors = new ColorSetting { MapBackground = new DisplayConfig().MainBackground, MapForeground = [200, 190, 160] },
            Rooms  = rooms,
            Doors  = doors,
            Area   = [],
        };
    }

    // ── Room placement ────────────────────────────────────────────────────────

    private static List<Box> PlaceRooms(
        List<RoomRequest> requests,
        List<PassageRequest> passages,
        Dictionary<string, int> nameToIndex,
        Random rng)
    {
        var placed = new List<Box?>(new Box?[requests.Count]);

        // Build Opening adjacency list (index → list of index)
        var openingNeighbors = new Dictionary<int, List<int>>();
        foreach (var p in passages.Where(p => p.Type == PassageType.Opening))
        {
            if (!nameToIndex.TryGetValue(p.From, out int fi)) continue;
            if (!nameToIndex.TryGetValue(p.To,   out int ti)) continue;
            openingNeighbors.GetOrCreate(fi).Add(ti);
            openingNeighbors.GetOrCreate(ti).Add(fi);
        }

        // Pass 1: rooms with explicit Zone
        for (int i = 0; i < requests.Count; i++)
            if (requests[i].Zone is not null)
                placed[i] = TryPlaceInZone(requests[i], placed, rng);

        // Pass 2: BFS over Opening graph — place each unplaced room adjacent to its placed partner
        var queue = new Queue<int>();
        var visited = new HashSet<int>();

        // Seed with already-placed rooms that have Opening neighbours
        for (int i = 0; i < requests.Count; i++)
            if (placed[i] is not null && openingNeighbors.ContainsKey(i))
                queue.Enqueue(i);

        // If no placed room seeded the queue, place the first Opening-connected room anywhere
        if (queue.Count == 0 && openingNeighbors.Count > 0)
        {
            int seed = openingNeighbors.Keys.First();
            placed[seed] = TryPlaceAnywhere(requests[seed], placed, rng);
            if (placed[seed] is not null) queue.Enqueue(seed);
        }

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (!visited.Add(cur)) continue;
            if (placed[cur] is null) continue;

            if (!openingNeighbors.TryGetValue(cur, out var neighbors)) continue;
            foreach (int nb in neighbors)
            {
                if (placed[nb] is not null) { queue.Enqueue(nb); continue; }
                // Try to place adjacent to cur; fall back to anywhere
                placed[nb] = TryPlaceAdjacent(requests[nb], placed[cur]!, placed, rng)
                          ?? TryPlaceAnywhere(requests[nb], placed, rng);
                queue.Enqueue(nb);
            }
        }

        // Pass 3: remaining rooms anywhere (gap=1, retry gap=0)
        for (int i = 0; i < requests.Count; i++)
            if (placed[i] is null)
                placed[i] = TryPlaceAnywhere(requests[i], placed, rng);

        // Pass 4: last resort — place still-null rooms anywhere with no gap constraint
        // Prevents multiple failed rooms from all collapsing to the same fallback box
        for (int i = 0; i < requests.Count; i++)
            if (placed[i] is null)
            {
                var (minW, maxW, minH, maxH) = SizeBounds[requests[i].Size];
                placed[i] = TryPlace(minW, maxW, minH, maxH, 1, MaxCols, 1, MaxRows, placed, rng, gap: 0);
            }

        return placed.Select(b => b ?? new Box(1, 1, 3, 3)).ToList();
    }

    // Place box touching one side of anchor, no cell overlap with any placed box
    private static Box? TryPlaceAdjacent(RoomRequest req, Box anchor, List<Box?> placed, Random rng)
    {
        var (minW, maxW, minH, maxH) = SizeBounds[req.Size];
        for (int attempt = 0; attempt < 400; attempt++)
        {
            int w = rng.Next(minW, maxW + 1);
            int h = rng.Next(minH, maxH + 1);
            int side = rng.Next(4);
            int c, r;
            switch (side)
            {
                case 0: c = anchor.Right + 1;  r = rng.Next(anchor.Row - h + 1, anchor.Bottom + 1); break; // right
                case 1: c = anchor.Col - w;    r = rng.Next(anchor.Row - h + 1, anchor.Bottom + 1); break; // left
                case 2: c = rng.Next(anchor.Col - w + 1, anchor.Right + 1); r = anchor.Bottom + 1;  break; // below
                default: c = rng.Next(anchor.Col - w + 1, anchor.Right + 1); r = anchor.Row - h;    break; // above
            }
            c = Math.Clamp(c, 1, MaxCols - w + 1);
            r = Math.Clamp(r, 1, MaxRows - h + 1);
            var box = new Box(c, r, w, h);
            // gap=0: no cell overlap allowed, but touching is fine
            if (placed.All(p => p is null || !box.Overlaps(p, 0)))
                return box;
        }
        return null;
    }

    private static Box? TryPlaceInZone(RoomRequest req, List<Box?> placed, Random rng)
    {
        var (cMin, cMax, rMin, rMax) = ZoneBounds(req.Zone!);
        var (minW, maxW, minH, maxH) = SizeBounds[req.Size];
        // Try with 2-cell gap first; fall back to touching (gap=0) if zone is too tight
        return TryPlace(minW, maxW, minH, maxH, cMin, cMax, rMin, rMax, placed, rng, gap: 1)
            ?? TryPlace(minW, maxW, minH, maxH, cMin, cMax, rMin, rMax, placed, rng, gap: 0);
    }

    private static Box? TryPlaceAnywhere(RoomRequest req, List<Box?> placed, Random rng)
    {
        var (minW, maxW, minH, maxH) = SizeBounds[req.Size];
        return TryPlace(minW, maxW, minH, maxH, 1, MaxCols, 1, MaxRows, placed, rng, gap: 1)
            ?? TryPlace(minW, maxW, minH, maxH, 1, MaxCols, 1, MaxRows, placed, rng, gap: 0);
    }

    private static Box? TryPlace(
        int minW, int maxW, int minH, int maxH,
        int cMin, int cMax, int rMin, int rMax,
        List<Box?> placed, Random rng, int gap = 1)
    {
        for (int attempt = 0; attempt < 400; attempt++)
        {
            int w = rng.Next(minW, maxW + 1);
            int h = rng.Next(minH, maxH + 1);
            int c = rng.Next(cMin, Math.Max(cMin + 1, cMax - w + 2));
            int r = rng.Next(rMin, Math.Max(rMin + 1, rMax - h + 2));
            c = Math.Clamp(c, 1, MaxCols - w + 1);
            r = Math.Clamp(r, 1, MaxRows - h + 1);
            var box = new Box(c, r, w, h);
            if (placed.All(p => p is null || !box.Overlaps(p, gap)))
                return box;
        }
        return null;
    }

    private static (int cMin, int cMax, int rMin, int rMax) ZoneBounds(string zone) => zone.ToLower() switch
    {
        "north"      => (1,             MaxCols,         1,              MaxRows / 3),
        "south"      => (1,             MaxCols,         MaxRows * 2 / 3, MaxRows),
        "west"       => (1,             MaxCols / 3,     1,              MaxRows),
        "east"       => (MaxCols * 2/3, MaxCols,         1,              MaxRows),
        "center"     => (MaxCols / 4,   MaxCols * 3 / 4, MaxRows / 4,    MaxRows * 3 / 4),
        "north_west" => (1,             MaxCols / 3,     1,              MaxRows / 3),
        "north_east" => (MaxCols * 2/3, MaxCols,         1,              MaxRows / 3),
        "south_west" => (1,             MaxCols / 3,     MaxRows * 2/3,  MaxRows),
        "south_east" => (MaxCols * 2/3, MaxCols,         MaxRows * 2/3,  MaxRows),
        _            => (1,             MaxCols,         1,              MaxRows),
    };

    // ── Connection ────────────────────────────────────────────────────────────

    private static void Connect(
        int fi, int ti,
        List<HashSet<Cell>> allCells,
        PassageRequest passage,
        HashSet<(int Col, int Row)> corridorCells,
        List<Door> doors,
        Random rng)
    {
        if (passage.Type == PassageType.Opening)
        {
            if (!TryConnectOpening(allCells[fi], allCells[ti], passage.HasDoor, doors))
                ConnectCorridor(fi, ti, allCells, passage, corridorCells, doors, rng);
        }
        else
            ConnectCorridor(fi, ti, allCells, passage, corridorCells, doors, rng);
    }

    // Opening: find adjacent cell pair, add a single door — no corridor room
    // Returns false if rooms don't touch (caller should fall back to corridor)
    private static bool TryConnectOpening(
        HashSet<Cell> fromCells, HashSet<Cell> toCells,
        bool hasDoor, List<Door> doors)
    {
        foreach (var f in fromCells)
        foreach (var (dc, dr) in (ReadOnlySpan<(int,int)>)[(1,0),(-1,0),(0,1),(0,-1)])
        {
            var nb = new Cell(f.Col + dc, f.Row + dr);
            if (toCells.Contains(nb))
            {
                doors.Add(MakeDoor(f, nb, hasDoor));
                return true;
            }
        }
        return false;
    }

    // Corridor: BFS/Straight/Winding path, optional width expansion, corridor room
    private static void ConnectCorridor(
        int fi, int ti,
        List<HashSet<Cell>> allCells,
        PassageRequest passage,
        HashSet<(int Col, int Row)> corridorCells,
        List<Door> doors,
        Random rng)
    {
        var fromCells = allCells[fi];
        var toCells   = allCells[ti];

        // Path-finding: block all rooms except from/to so BFS can start/end inside them
        var blocked = new HashSet<Cell>();
        for (int i = 0; i < allCells.Count; i++)
            if (i != fi && i != ti)
                blocked.UnionWith(allCells[i]);

        var (ca, cb) = FindClosestPair(fromCells, toCells);

        if (AreAdjacent(ca, cb))
        {
            doors.Add(MakeDoor(ca, cb, passage.HasDoor));
            return;
        }

        // Block from/to room interiors so the path can't meander inside them.
        // Without this, Winding paths wander through room cells; RemoveAll then strips
        // those cells and leaves an empty path → AddAlignedDoors finds no boundary → no doors.
        var pathBlocked = new HashSet<Cell>(blocked);
        foreach (var c in fromCells) if (c != ca) pathBlocked.Add(c);
        foreach (var c in toCells)   if (c != cb) pathBlocked.Add(c);

        var path = FindCorridorPath(ca, cb, pathBlocked, passage.Shape, rng);
        if (path.Count == 0) return;

        // Remove intermediate path cells that fell inside from/to rooms
        path.RemoveAll(c => fromCells.Contains(c) || toCells.Contains(c));

        // Expansion must not bleed into any room
        var expandBlocked = new HashSet<Cell>(blocked);
        expandBlocked.UnionWith(fromCells);
        expandBlocked.UnionWith(toCells);

        var widened = passage.Width > 1
            ? ExpandPath(path, passage.Width, expandBlocked)
            : new HashSet<Cell>(path);

        foreach (var c in widened)
            corridorCells.Add((c.Col, c.Row));

        // Exactly `width` aligned doors at each room boundary
        AddAlignedDoors(widened, fromCells, passage.Width, hasDoor: false,         doorFromRoom: true,  doors);
        AddAlignedDoors(widened, toCells,   passage.Width, passage.HasDoor,         doorFromRoom: false, doors);
    }

    // Transition: corridor from a room to a map-edge point (outside grid)
    private static void ConnectTransition(
        int ri,
        MapTransition transition,
        List<HashSet<Cell>> allCells,
        HashSet<(int Col, int Row)> corridorCells,
        List<Door> doors,
        Random rng)
    {
        if (transition.EdgePoint.Count < 2) return;

        var roomCells = allCells[ri];
        var ep     = new Cell(transition.EdgePoint[0], transition.EdgePoint[1]);
        // First valid grid cell in the direction of the edge point
        var anchor = new Cell(
            Math.Clamp(ep.Col, 1, MaxCols),
            Math.Clamp(ep.Row, 1, MaxRows));

        // Path-finding: block all rooms except ri so BFS can start inside it
        var blocked = new HashSet<Cell>();
        for (int i = 0; i < allCells.Count; i++)
            if (i != ri) blocked.UnionWith(allCells[i]);

        // Closest room cell to anchor
        var closestRoom = roomCells
            .MinBy(c => Math.Abs(c.Col - anchor.Col) + Math.Abs(c.Row - anchor.Row))!;

        // Block room interior so path can't meander inside it (same fix as ConnectCorridor)
        var pathBlocked = new HashSet<Cell>(blocked);
        foreach (var c in roomCells) if (c != closestRoom) pathBlocked.Add(c);

        // Path from room boundary to anchor (anchor excluded by FindCorridorPath, added back below)
        var path = AreAdjacent(closestRoom, anchor)
            ? new List<Cell>()
            : FindCorridorPath(closestRoom, anchor, pathBlocked, transition.Shape, rng);

        // Remove intermediate path cells that fell inside the room
        path.RemoveAll(c => roomCells.Contains(c));

        // Anchor itself becomes the last corridor cell (exit cell on grid edge)
        var fullPath = path.Append(anchor).ToList();

        // Expansion must not bleed into any room
        var expandBlocked = new HashSet<Cell>(blocked);
        expandBlocked.UnionWith(roomCells);

        var widened = transition.Width > 1
            ? ExpandPath(fullPath, transition.Width, expandBlocked)
            : new HashSet<Cell>(fullPath);

        foreach (var c in widened)
            corridorCells.Add((c.Col, c.Row));

        // Exactly `width` aligned doors at the room boundary
        AddAlignedDoors(widened, roomCells, transition.Width, hasDoor: false, doorFromRoom: true, doors);

        // Exit door: anchor → EdgePoint (outside grid marks the map transition)
        var entryDoor = MakeDoor(anchor, ep, transition.HasDoor);
        entryDoor.IsEntry = true;
        doors.Add(entryDoor);
    }

    // ── Path finding ──────────────────────────────────────────────────────────

    private static List<Cell> FindCorridorPath(
        Cell from, Cell to, HashSet<Cell> blocked, CorridorShape shape, Random rng) =>
        shape switch
        {
            CorridorShape.Straight => FindStraightPath(from, to, blocked),
            CorridorShape.Winding  => FindWindingPath(from, to, blocked, rng),
            _                      => FindBfsPath(from, to, blocked),
        };

    // Prefer straight L-shape; fall back to BFS if blocked
    private static List<Cell> FindStraightPath(Cell from, Cell to, HashSet<Cell> blocked)
    {
        var hFirst = BuildLShape(from, to, horizontalFirst: true);
        if (hFirst.All(c => !blocked.Contains(c))) return hFirst;

        var vFirst = BuildLShape(from, to, horizontalFirst: false);
        if (vFirst.All(c => !blocked.Contains(c))) return vFirst;

        return FindBfsPath(from, to, blocked);
    }

    private static List<Cell> BuildLShape(Cell from, Cell to, bool horizontalFirst)
    {
        var path = new List<Cell>();
        int c = from.Col, r = from.Row;
        if (horizontalFirst)
        {
            while (c != to.Col) { c += Math.Sign(to.Col - c); if (c == to.Col && r == to.Row) break; path.Add(new Cell(c, r)); }
            while (r != to.Row) { r += Math.Sign(to.Row - r); if (c == to.Col && r == to.Row) break; path.Add(new Cell(c, r)); }
        }
        else
        {
            while (r != to.Row) { r += Math.Sign(to.Row - r); if (c == to.Col && r == to.Row) break; path.Add(new Cell(c, r)); }
            while (c != to.Col) { c += Math.Sign(to.Col - c); if (c == to.Col && r == to.Row) break; path.Add(new Cell(c, r)); }
        }
        return path;
    }

    // BFS shortest path avoiding blocked; returns intermediate cells only (excludes from/to)
    private static List<Cell> FindBfsPath(Cell from, Cell to, HashSet<Cell> blocked)
    {
        var prev = new Dictionary<Cell, Cell> { [from] = from };
        var queue = new Queue<Cell>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == to) break;
            foreach (var (dc, dr) in (ReadOnlySpan<(int,int)>)[(0,-1),(0,1),(-1,0),(1,0)])
            {
                var nb = new Cell(cur.Col + dc, cur.Row + dr);
                if (nb.Col < 1 || nb.Col > MaxCols || nb.Row < 1 || nb.Row > MaxRows) continue;
                if (prev.ContainsKey(nb)) continue;
                if (nb != to && blocked.Contains(nb)) continue;
                prev[nb] = cur;
                queue.Enqueue(nb);
            }
        }
        if (!prev.ContainsKey(to))
            return BuildLShape(from, to, horizontalFirst: true); // last-resort fallback

        var path = new List<Cell>();
        var c = to;
        while (prev[c] != c) { path.Add(c); c = prev[c]; }
        path.Reverse();
        if (path.Count > 0 && path[^1] == to) path.RemoveAt(path.Count - 1);
        return path;
    }

    // Random walk biased toward target; fall back to BFS if stuck
    private static List<Cell> FindWindingPath(Cell from, Cell to, HashSet<Cell> blocked, Random rng)
    {
        var path = new List<Cell>();
        var visited = new HashSet<Cell> { from };
        var cur = from;

        for (int step = 0; step < (MaxCols + MaxRows) * 4 && !AreAdjacent(cur, to); step++)
        {
            var candidates = new List<Cell>();
            foreach (var (dc, dr) in (ReadOnlySpan<(int,int)>)[(0,-1),(0,1),(-1,0),(1,0)])
            {
                var nb = new Cell(cur.Col + dc, cur.Row + dr);
                if (nb.Col < 1 || nb.Col > MaxCols || nb.Row < 1 || nb.Row > MaxRows) continue;
                if (blocked.Contains(nb) || visited.Contains(nb)) continue;
                candidates.Add(nb);
            }
            if (candidates.Count == 0) break;

            var next = rng.NextDouble() < 0.65
                ? candidates.MinBy(n => Math.Abs(n.Col - to.Col) + Math.Abs(n.Row - to.Row))!
                : candidates[rng.Next(candidates.Count)];

            path.Add(next);
            visited.Add(next);
            cur = next;
        }

        var last = path.Count > 0 ? path[^1] : from;
        return AreAdjacent(last, to) ? path : FindBfsPath(from, to, blocked);
    }

    // Expand 1-wide path perpendicularly for Width > 1
    private static HashSet<Cell> ExpandPath(List<Cell> path, int width, HashSet<Cell> blocked)
    {
        var result = new HashSet<Cell>(path);
        int half    = (width - 1) / 2;
        int halfPos = width / 2;

        for (int i = 0; i < path.Count; i++)
        {
            var cur  = path[i];
            var prev = i > 0              ? path[i - 1] : cur;
            var next = i < path.Count - 1 ? path[i + 1] : cur;

            bool movingH = prev.Row == cur.Row || next.Row == cur.Row;
            bool movingV = prev.Col == cur.Col || next.Col == cur.Col;

            if (movingH)
                for (int dr = -half; dr <= halfPos; dr++)
                    TryAddCell(result, cur.Col, cur.Row + dr, blocked);
            if (movingV)
                for (int dc = -half; dc <= halfPos; dc++)
                    TryAddCell(result, cur.Col + dc, cur.Row, blocked);
        }
        return result;
    }

    private static void TryAddCell(HashSet<Cell> set, int col, int row, HashSet<Cell> blocked)
    {
        if (col < 1 || col > MaxCols || row < 1 || row > MaxRows) return;
        var c = new Cell(col, row);
        if (!blocked.Contains(c)) set.Add(c);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool AreAdjacent(Cell a, Cell b) =>
        Math.Abs(a.Col - b.Col) + Math.Abs(a.Row - b.Row) == 1;

    private static (Cell from, Cell to) FindClosestPair(HashSet<Cell> from, HashSet<Cell> to)
    {
        Cell bestF = from.First(), bestT = to.First();
        int bestDist = int.MaxValue;
        foreach (var f in from)
            foreach (var t in to)
            {
                int d = Math.Abs(f.Col - t.Col) + Math.Abs(f.Row - t.Row);
                if (d < bestDist) { bestDist = d; bestF = f; bestT = t; }
            }
        return (bestF, bestT);
    }

    /// <summary>
    /// Adds exactly <paramref name="width"/> aligned doors between the corridor and a room.
    /// Finds the dominant approach direction (the direction from corridor cell → room cell
    /// that appears most often), then picks the central <paramref name="width"/> pairs
    /// sorted perpendicular to that direction.
    /// This ensures a width-1 corridor produces 1 door and a width-2 corridor produces 2
    /// doors in a straight line, regardless of bends near the room boundary.
    /// </summary>
    private static void AddAlignedDoors(
        HashSet<Cell> widened,
        HashSet<Cell> roomCells,
        int width,
        bool hasDoor,
        bool doorFromRoom,   // true  → door goes room→corridor  (from-side, always open)
                             // false → door goes corridor→room  (to-side, may be locked)
        List<Door> doors)
    {
        var seen = new HashSet<(Cell, Cell)>();
        var candidates = new List<(Cell corr, Cell room)>();

        foreach (var wc in widened)
        foreach (var (dc, dr) in (ReadOnlySpan<(int, int)>)[(1, 0), (-1, 0), (0, 1), (0, -1)])
        {
            var nb = new Cell(wc.Col + dc, wc.Row + dr);
            if (roomCells.Contains(nb) && seen.Add((wc, nb)))
                candidates.Add((wc, nb));
        }

        if (candidates.Count == 0) return;

        // Dominant direction: corridor → room (e.g. (+1,0) means room is to the right)
        var (dx, dy) = candidates
            .GroupBy(p => (p.room.Col - p.corr.Col, p.room.Row - p.corr.Row))
            .MaxBy(g => g.Count())!.Key;

        // Keep only pairs in the dominant direction, sorted along the perpendicular axis
        var aligned = candidates
            .Where(p => p.room.Col - p.corr.Col == dx && p.room.Row - p.corr.Row == dy)
            .OrderBy(p => dx != 0 ? p.corr.Row : p.corr.Col)   // perpendicular sort
            .ToList();

        // Take `width` central doors (skip excess from both sides evenly)
        int take = Math.Min(width, aligned.Count);
        int skip = (aligned.Count - take) / 2;

        foreach (var (corr, room) in aligned.Skip(skip).Take(take))
            doors.Add(doorFromRoom
                ? MakeDoor(room, corr, hasDoor)   // room → corridor
                : MakeDoor(corr, room, hasDoor));  // corridor → room
    }

    private static Door MakeDoor(Cell from, Cell to, bool hasDoor) => new()
    {
        From   = [from.Col, from.Row],
        To     = [to.Col,   to.Row],
        IsDoor = hasDoor,
        Color  = hasDoor ? [139, 90, 43] : null,
    };

    // ── Cell shape builders ───────────────────────────────────────────────────

    private static List<HashSet<Cell>> BuildCells(List<RoomRequest> requests, List<Box> boxes, Random rng)
    {
        var result = new List<HashSet<Cell>>();
        for (int i = 0; i < requests.Count; i++)
            result.Add(requests[i].Shape switch
            {
                RoomShape.Circular  => CircleCells(boxes[i]),
                RoomShape.Irregular => IrregularCells(boxes[i], rng),
                RoomShape.LShaped   => LShapedCells(boxes[i], rng),
                _                   => RectCells(boxes[i]),
            });
        return result;
    }

    private static HashSet<Cell> RectCells(Box b)
    {
        var set = new HashSet<Cell>();
        for (int c = b.Col; c <= b.Right; c++)
            for (int r = b.Row; r <= b.Bottom; r++)
                set.Add(new Cell(c, r));
        return set;
    }

    private static HashSet<Cell> CircleCells(Box b)
    {
        var set = new HashSet<Cell>();
        float cx = (b.Col + b.Right) / 2f;
        float cy = (b.Row + b.Bottom) / 2f;
        float rx = b.W / 2f;
        float ry = b.H / 2f;
        for (int c = b.Col; c <= b.Right; c++)
            for (int r = b.Row; r <= b.Bottom; r++)
            {
                float dx = (c - cx) / rx;
                float dy = (r - cy) / ry;
                if (dx * dx + dy * dy <= 1.0f)
                    set.Add(new Cell(c, r));
            }
        if (set.Count == 0) set.Add(new Cell(b.Col, b.Row));
        return set;
    }

    private static HashSet<Cell> IrregularCells(Box b, Random rng)
    {
        var all = RectCells(b);
        int targetRemove = rng.Next(all.Count / 5, all.Count / 3 + 1);
        int removed = 0;

        // Protected 2×3 core centred in the box — erosion must never touch these cells
        int coreCol = (b.Col + b.Right - 1) / 2;
        int coreRow = (b.Row + b.Bottom - 1) / 2;
        var core = new HashSet<Cell>();
        for (int dc = 0; dc < 2; dc++)
            for (int dr = 0; dr < 3; dr++)
                core.Add(new Cell(coreCol + dc, coreRow + dr));

        // Multiple erosion passes: recalculate border each time so removal bites deeper
        for (int pass = 0; pass < 5 && removed < targetRemove; pass++)
        {
            var border = all
                .Where(c => !core.Contains(c) && HasEmptyNeighbor(c, all))
                .OrderBy(_ => rng.Next())
                .ToList();

            foreach (var cell in border)
            {
                if (removed >= targetRemove) break;
                all.Remove(cell);
                if (all.Count >= 4 && IsConnected(all))
                    removed++;
                else
                    all.Add(cell);
            }
        }
        return all;
    }

    private static HashSet<Cell> LShapedCells(Box b, Random rng)
    {
        var all = RectCells(b);

        // Cut one random corner to form an L.
        // Cut size: 35–60% of each dimension so both arms of the L stay substantial.
        int corner = rng.Next(4);
        int cutW = rng.Next((int)(b.W * 0.35), (int)(b.W * 0.60) + 1);
        int cutH = rng.Next((int)(b.H * 0.35), (int)(b.H * 0.60) + 1);

        int colStart, colEnd, rowStart, rowEnd;
        switch (corner)
        {
            case 0: colStart = b.Right - cutW + 1; colEnd = b.Right;  rowStart = b.Row;             rowEnd = b.Row + cutH - 1;    break; // top-right
            case 1: colStart = b.Col;              colEnd = b.Col + cutW - 1; rowStart = b.Row;     rowEnd = b.Row + cutH - 1;    break; // top-left
            case 2: colStart = b.Right - cutW + 1; colEnd = b.Right;  rowStart = b.Bottom - cutH + 1; rowEnd = b.Bottom;          break; // bottom-right
            default:colStart = b.Col;              colEnd = b.Col + cutW - 1; rowStart = b.Bottom - cutH + 1; rowEnd = b.Bottom;  break; // bottom-left
        }

        for (int c = colStart; c <= colEnd; c++)
            for (int r = rowStart; r <= rowEnd; r++)
                all.Remove(new Cell(c, r));

        if (!IsConnected(all) || all.Count < 6)
            return RectCells(b); // fallback — box was too small for a proper L

        // Light erosion to roughen the hard-cut edges (2 passes, up to ~15% of cells)
        int targetRemove = rng.Next(all.Count / 8, all.Count / 5 + 1);
        int removed = 0;

        // Core: 2×3 block placed in the "elbow" of the L (opposite to cut corner)
        int safeCol = corner is 0 or 2 ? b.Col + 1 : b.Right - 2;
        int safeRow = corner is 0 or 1 ? b.Bottom - 3 : b.Row;
        var core = new HashSet<Cell>();
        for (int dc = 0; dc < 2; dc++)
            for (int dr = 0; dr < 3; dr++)
            {
                var cell = new Cell(safeCol + dc, safeRow + dr);
                if (all.Contains(cell)) core.Add(cell);
            }

        for (int pass = 0; pass < 2 && removed < targetRemove; pass++)
        {
            var border = all
                .Where(c => !core.Contains(c) && HasEmptyNeighbor(c, all))
                .OrderBy(_ => rng.Next())
                .ToList();

            foreach (var cell in border)
            {
                if (removed >= targetRemove) break;
                all.Remove(cell);
                if (all.Count >= 6 && IsConnected(all))
                    removed++;
                else
                    all.Add(cell);
            }
        }

        return all;
    }

    private static bool HasEmptyNeighbor(Cell c, HashSet<Cell> cells)
    {
        foreach (var (dc, dr) in (ReadOnlySpan<(int, int)>)[(1, 0), (-1, 0), (0, 1), (0, -1)])
            if (!cells.Contains(new Cell(c.Col + dc, c.Row + dr))) return true;
        return false;
    }

    private static bool IsConnected(HashSet<Cell> cells)
    {
        if (cells.Count <= 1) return true;
        var start   = cells.First();
        var visited = new HashSet<Cell> { start };
        var queue   = new Queue<Cell>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var (dc, dr) in (ReadOnlySpan<(int,int)>)[(1,0),(-1,0),(0,1),(0,-1)])
            {
                var nb = new Cell(cur.Col + dc, cur.Row + dr);
                if (cells.Contains(nb) && visited.Add(nb))
                    queue.Enqueue(nb);
            }
        }
        return visited.Count == cells.Count;
    }

    // ── Colour heuristics ─────────────────────────────────────────────────────

    private static List<int> PickColor(string name, Random rng)
    {
        string n = name.ToLower();
        //if (Contains(n, "entrance", "entry", "foyer")) return [101, 67, 33];
        //if (Contains(n, "throne", "royal", "king", "queen")) return [80, 0, 120];
        //if (Contains(n, "tomb", "crypt", "grave", "burial")) return [50, 50, 55];
        //if (Contains(n, "guard", "barracks", "armory")) return [60, 70, 60];
        //if (Contains(n, "treasury", "vault", "gold")) return [140, 110, 0];
        //if (Contains(n, "library", "archive", "study")) return [30, 60, 90];
        //if (Contains(n, "altar", "temple", "shrine", "chapel")) return [120, 80, 0];
        //if (Contains(n, "dungeon", "prison", "cell")) return [40, 40, 40];
        //if (Contains(n, "cave", "cavern", "grotto")) return [70, 50, 30];
        //if (Contains(n, "passage", "corridor", "tunnel")) return [55, 55, 65];
        //if (Contains(n, "chamber", "hall", "room")) return [60, 45, 70];
        //return [rng.Next(40, 120), rng.Next(30, 90), rng.Next(20, 70)];
        return [55, 55, 65];
    }

    private static bool Contains(string text, params string[] keywords) =>
        keywords.Any(text.Contains);
}

// Extension helper (Dictionary.GetOrCreate)
file static class DictExtensions
{
    public static TValue GetOrCreate<TKey, TValue>(
        this Dictionary<TKey, TValue> dict, TKey key) where TKey : notnull where TValue : new()
    {
        if (!dict.TryGetValue(key, out var v)) dict[key] = v = new TValue();
        return v;
    }
}
