using NaviDnD.Data.Models;

namespace NaviDnD.MapGen.Generators;

// Buildings (MapChunk.Theme = building) and settlements (village).) The outline of the building is not a simple
// rectangle (body + wings G/T/P/cross, the tower has a cross), inside there are rooms close to common walls
// (wall = border of two rooms), doors/openings (some are open), windows in the external walls, furniture
// according to the purpose of the room. Layout - according to the type of building (MapChunk.Style): at the
// tavern/shop/temple/estate/barracks on the ground floor there is a large hall (counter/counter/altar with
// benches right in it) and a number of utility rooms near the back wall, on the upper floors there is a
// corridor with rooms. The outline is common for all floors of the building (footprintSeed - according to the
// place in the plan), the floors stand on top of each other. The first floor is in the courtyard (paths from
// the block exits to the entrance); settlement - 3-5 houses along the streets to a square with a well,
// vegetable gardens behind a fence.
public static class BuildingGenerator
{
    public sealed record Result(List<Room> Rooms, List<Door> Doors, List<string> Terrain, List<Furniture> Furniture);

    private const int W = MapChunk.Cols, H = MapChunk.Rows;
    // Outline of the building - inside [MinC..MaxC]×[MinR..MaxR]: there is a courtyard at least 2 cells wide around.
    private const int MinC = 2, MaxC = W - 3, MinR = 2, MaxR = H - 3;

    public static readonly List<int> WoodFloor = [74, 60, 46];
    public static readonly List<int> HallFloor = [84, 66, 48];
    public static readonly List<int> StoneFloor = [58, 56, 60];
    public static readonly List<int> TempleFloor = [70, 70, 78];
    private static readonly List<int> DoorColor = [139, 90, 43];
    public static readonly List<int> WindowColor = [150, 196, 226];

    // Code for a strip along the edge without a neighbor: natural obstacle (trees, thicket, bushes).
    public const char NaturalBand = '*';

    private enum Role { Living, Hall, Kitchen, Storage, Bedroom, Workshop, Vestry, Armory, Study, Corridor, Tower }

    private sealed record Rect(int C, int R, int Width, int Height)
    {
        public IEnumerable<(int c, int r)> Cells()
        {
            for (int c = C; c < C + Width; c++)
                for (int r = R; r < R + Height; r++)
                    yield return (c, r);
        }
    }

    private sealed class Region
    {
        public required HashSet<(int c, int r)> Cells { get; init; }
        public Role Role { get; set; }
        public string Name { get; set; } = L.W("Room");
        public Rect? Box { get; init; }            // hall: rectangle and back side (stand/altar)
        public ChunkSide? Back { get; init; }
    }

    private static bool HasHall(string? style) => style is "tavern" or "shop" or "temple" or "manor" or "barracks";

    // ── Building ─────────────────────────────── ──────────────────────────────── bands - side → code for the
    // barrier along the edge of the yard (only for the first floor); seamDoorSides - sides where this block
    // itself places a passage through the seam to the neighboring building of the same floor (the dungeon
    // neighbor places his own).
    public static Result Generate(MapChunk chunk, IReadOnlyList<ChunkExit> exits, int footprintSeed, bool ground,
        IReadOnlyDictionary<ChunkSide, char> bands, IReadOnlyCollection<ChunkSide> seamDoorSides, bool moat = false)
    {
        var rng = new Random(chunk.Seed);
        string? style = chunk.Style;
        bool basement = chunk.Z < 0;
        var b = new Builder(chunk, rng);

        var parts = Footprint(new Random(footprintSeed), style);
        var inside = parts.SelectMany(p => p.Cells()).ToHashSet();
        var anchors = exits.Select(e => Local(e.AnchorCell(chunk), chunk)).ToList();
        (int c, int r) target = anchors.Count > 0 ? anchors[0] : (W / 2, 0);

        var regions = Layout(parts, style, ground, basement, target, rng);
        b.AddRooms(regions, basement ? StoneFloor : style == "temple" ? TempleFloor : WoodFloor);
        b.ConnectRooms(regions, style);

        if (ground)
        {
            b.Ground(bands);
            var hall = regions.FirstOrDefault(r => r.Role == Role.Hall);
            var entrance = b.AddEntrance(inside, hall?.Cells, target, backDoor: !moat);
            b.Streets(anchors, entrance, inside);
            b.ExternalExits(exits);
            b.Trees(inside, entrance);
            if (moat) b.Moat(inside, entrance, anchors);
        }
        else
            b.Galleries(exits, inside, seamDoorSides, basement);

        b.Floor(inside, basement);
        if (!basement) b.Windows(regions, inside);
        foreach (var region in regions) b.Furnish(region, basement);
        return b.Result();
    }

    // ── Settlement ────────────────────────────── ──────────────────────────────
    public static Result GenerateVillage(MapChunk chunk, IReadOnlyList<ChunkExit> exits, IReadOnlyDictionary<ChunkSide, char> bands, string? layout = null)
    {
        var rng = new Random(chunk.Seed);
        var b = new Builder(chunk, rng);
        b.Ground(bands);

        // A square with a well in the center and streets from the block exits to it.
        int hubC = layout == "road" ? W / 3 : W / 2, hubR = H / 2;
        var street = new HashSet<(int c, int r)>();
        for (int c = hubC - 2; c <= hubC + 2; c++)
            for (int r = hubR - 1; r <= hubR + 1; r++) street.Add((c, r));
        foreach (var exit in exits)
        {
            var (c, r) = Local(exit.AnchorCell(chunk), chunk);
            // First inwards from the edge, then towards the square - the street does not go along the edge.
            var (dx, dy) = ChunkExit.Step(exit.Side);
            street.Add((c, r));
            for (int i = 0; i < 2; i++) { c -= dx; r -= dy; street.Add((c, r)); }
            bool horizontalFirst = rng.Next(2) == 0;
            if (layout == "road") horizontalFirst = exit.Side is ChunkSide.Top or ChunkSide.Bottom;
            for (int guard = 0; guard < 60 && (c, r) != (hubC, hubR); guard++)
            {
                if ((horizontalFirst && c != hubC) || r == hubR) c += Math.Sign(hubC - c);
                else r += Math.Sign(hubR - r);
                street.Add((c, r));
            }
        }
        if (layout == "road")
            for (int r = 0; r < H; r++) street.Add((hubC, r));
        if (layout == "palisade")
            for (int c = 0; c < W; c++)
                for (int r = 0; r < H; r++)
                    if (OnBorder((c, r))) b.G[c, r] = '#';
        if (layout == "river")
            for (int c = 0; c < W; c++)
                for (int r = H - 3; r < H - 1; r++) b.G[c, r] = 'W';
        foreach (var (c, r) in street) b.G[c, r] = b.G[c, r] == 'W' ? '~' : '=';
        b.G[hubC, hubR] = 'V';

        // Houses: rectangle or L, not on the street and not close to other houses.
        var taken = new HashSet<(int, int)>(street.SelectMany(x => Around8(x).Append(x)));
        var houses = new List<HashSet<(int c, int r)>>();
        for (int attempt = 0; attempt < 300 && houses.Count < (layout == "road" ? 6 : layout == "river" ? 3 : 5); attempt++)
        {
            int w = layout is "road" or "river" ? rng.Next(3, 6) : rng.Next(4, 8), h = layout is "road" or "river" ? rng.Next(3, 5) : rng.Next(3, 6);
            int c0 = rng.Next(2, W - 2 - w + 1), r0 = rng.Next(2, H - 2 - h + 1);
            var parts = new List<Rect> { new(c0, r0, w, h) };
            if (w >= 6 && h >= 4 && rng.Next(2) == 0)
            {
                // G: corner notch.
                int nw = rng.Next(2, w / 2 + 1), nh = rng.Next(1, h - 2);
                bool right = rng.Next(2) == 0, top = rng.Next(2) == 0;
                parts = top
                    ? [new(c0, r0, w, h - nh), new(right ? c0 : c0 + nw, r0 + h - nh, w - nw, nh)]
                    : [new(c0, r0 + nh, w, h - nh), new(right ? c0 : c0 + nw, r0, w - nw, nh)];
            }
            var cells = parts.SelectMany(p => p.Cells()).ToHashSet();
            if (cells.Any(x => taken.Contains(x) || b.Blocked(x))) continue;
            houses.Add(cells);
            foreach (var x in cells) foreach (var n in Around8(x).Append(x)) taken.Add(n);

            int number = houses.Count;
            var regions = new List<Region>();
            foreach (var part in parts) Split(part, rng, regions, hall: false, minArea: 10);
            var names = new[] { L.W("kitchen"), L.W("bedroom"), L.W("pantry"), L.W("workshop") };
            Role[] roles = [Role.Kitchen, Role.Bedroom, Role.Storage, Role.Workshop];
            for (int i = 0; i < regions.Count; i++)
            {
                regions[i].Role = regions.Count == 1 ? Role.Living : roles[i % roles.Length];
                regions[i].Name = regions.Count == 1 ? L.WF("House {0}", number) : L.WF("House {0}", number) + ": " + names[i % names.Length];
            }
            b.AddRooms(regions, WoodFloor);
            b.ConnectRooms(regions, "house");
            var door = b.AddEntrance(cells, null, (hubC, hubR), backDoor: false, street);
            // Path from door to street.
            foreach (var x in b.YardPath(door, street, cells) ?? []) if (b.G[x.c, x.r] != 'V') b.G[x.c, x.r] = '=';
            b.Floor(cells, basement: false);
            b.Windows(regions, cells);
            foreach (var region in regions) b.Furnish(region, basement: false);
        }

        // Vegetable gardens behind a fence with a gate; trees.
        var houseCells = houses.SelectMany(x => x).ToHashSet();
        for (int attempt = 0, gardens = 0; attempt < 60 && gardens < 3; attempt++)
        {
            int w = rng.Next(4, 7), h = rng.Next(3, 5);
            int c0 = rng.Next(1, W - 1 - w + 1), r0 = rng.Next(1, H - 1 - h + 1);
            var rect = new Rect(c0, r0, w, h).Cells().ToList();
            if (rect.Any(x => b.G[x.c, x.r] is not ('.' or ',') || houseCells.Contains(x) || Around8(x).Any(houseCells.Contains))) continue;
            var saved = rect.ToDictionary(x => x, x => b.G[x.c, x.r]);
            var edge = rect.Where(x => x.c == c0 || x.c == c0 + w - 1 || x.r == r0 || x.r == r0 + h - 1).ToList();
            foreach (var x in rect) b.G[x.c, x.r] = edge.Contains(x) ? '#' : 'G';
            var gates = edge.Where(x => (x.c == c0 || x.c == c0 + w - 1) != (x.r == r0 || x.r == r0 + h - 1)).ToList();
            var gate = gates[rng.Next(gates.Count)];
            b.G[gate.c, gate.r] = '.';
            if (b.OutsideConnected(houseCells, (hubC, hubR + 1))) gardens++;
            else foreach (var (x, was) in saved) b.G[x.c, x.r] = was;
        }
        b.ExternalExits(exits);
        b.Trees(houseCells, (hubC, hubR + 1));
        return b.Result();
    }

    // ── Circuit ─────────────────────────────── ──────────────────────────────── The main body + wings (G, T,
    // P, cross) or a recess from the corner - a list of non-intersecting rectangles, the first - the body (for
    // hall buildings - always the whole one: there is a hall in it). The tower is a cross (cut corners).
    private static List<Rect> Footprint(Random rng, string? style)
    {
        bool big = style is "tavern" or "manor" or "barracks" or "temple";
        int w = style switch { "tower" => rng.Next(7, 9), "house" => rng.Next(6, 10), "shop" => rng.Next(8, 11), _ when big => rng.Next(11, 14), _ => rng.Next(8, 12) };
        int h = style switch { "tower" => rng.Next(6, 8), "house" => rng.Next(4, 6), _ when big => rng.Next(6, 9), _ => rng.Next(5, 7) };
        int pad = big ? 0 : 2;
        int c0 = rng.Next(MinC + pad / 2, Math.Max(MinC + pad / 2 + 1, MaxC - w + 2 - pad / 2));
        int r0 = rng.Next(MinR + pad / 2, Math.Max(MinR + pad / 2 + 1, MaxR - h + 2 - pad / 2));
        if (style == "tower")
            return [new(c0 + 1, r0, w - 2, h), new(c0, r0 + 1, 1, h - 2), new(c0 + w - 1, r0 + 1, 1, h - 2)];

        var parts = new List<Rect> { new(c0, r0, w, h) };
        Rect? Wing(ChunkSide side, double at, int width, int depth)
        {
            // at — position of the wing along the side: 0 — at the beginning, 0.5 — in the middle, 1 — at the end.
            bool vertical = side is ChunkSide.Left or ChunkSide.Right;
            int len = vertical ? h : w;
            width = Math.Min(width, len);
            int start = (int)Math.Round((len - width) * at);
            var rect = side switch
            {
                ChunkSide.Left => new Rect(c0 - depth, r0 + start, depth, width),
                ChunkSide.Right => new Rect(c0 + w, r0 + start, depth, width),
                ChunkSide.Bottom => new Rect(c0 + start, r0 - depth, width, depth),
                _ => new Rect(c0 + start, r0 + h, width, depth),
            };
            int cl = Math.Max(rect.C, MinC), cr = Math.Min(rect.C + rect.Width - 1, MaxC);
            int rb = Math.Max(rect.R, MinR), rt = Math.Min(rect.R + rect.Height - 1, MaxR);
            if (cr - cl + 1 < 2 || rt - rb + 1 < 2) return null;
            return new Rect(cl, rb, cr - cl + 1, rt - rb + 1);
        }

        void AddWings(ChunkSide[] sides, int shape)
        {
            var wings = shape switch
            {
                0 => [Wing(sides[0], rng.Next(2), rng.Next(3, 5), rng.Next(3, 5))],                       // G
                1 => [Wing(sides[0], 0.5, rng.Next(3, 5), rng.Next(3, 5))],                              // T
                2 => [Wing(sides[0], 0, 3, rng.Next(3, 5)), Wing(sides[0], 1, 3, rng.Next(3, 5))],       // P
                3 => [Wing(sides[0], 0.5, 3, 3), Wing(ChunkExit.Opposite(sides[0]), 0.5, 3, 3)],         // cross
                _ => new Rect?[0],
            };
            // The wings do not overlap (P on the short side - the second wing disappears, G comes out).
            foreach (var wing in wings.OfType<Rect>())
                if (!wing.Cells().Any(x => parts.Any(p => p.Cells().Contains(x)))) parts.Add(wing);
        }

        var order = Enum.GetValues<ChunkSide>().OrderBy(_ => rng.Next()).ToArray();
        int shape = style == "temple" ? (rng.Next(2) == 0 ? 1 : 3) : rng.Next(HasHall(style) ? 4 : 5);
        AddWings(order, shape);
        for (int i = 1; i < order.Length && parts.Count == 1 && HasHall(style); i++) AddWings(order[i..], 0);

        if (parts.Count == 1 && !HasHall(style))
        {
            // Recess from the corner: body - L of two rectangles.
            int nw = rng.Next(3, Math.Max(4, w / 2)), nh = rng.Next(2, Math.Max(3, h - 2));
            bool right = rng.Next(2) == 0, top = rng.Next(2) == 0;
            int keepH = h - nh;
            var lower = top ? new Rect(c0, r0, w, keepH) : new Rect(c0, r0 + nh, w, keepH);
            var upper = top
                ? new Rect(right ? c0 : c0 + nw, r0 + keepH, w - nw, nh)
                : new Rect(right ? c0 : c0 + nw, r0, w - nw, nh);
            parts = [lower, upper];
        }
        return parts;
    }

    // ── Layout ───────────────────────────── ──────────────────────────────
    private static List<Region> Layout(List<Rect> parts, string? style, bool ground, bool basement, (int c, int r) front, Random rng)
    {
        var regions = new List<Region>();
        if (basement)
        {
            foreach (var part in parts) Split(part, rng, regions, hall: false, minArea: 12);
            foreach (var r in regions) { r.Role = Role.Storage; r.Name = L.W("Cellar"); }
            return regions;
        }
        if (style == "tower")
        {
            regions.Add(new Region { Cells = parts.SelectMany(p => p.Cells()).ToHashSet(), Role = Role.Tower, Name = ground ? L.W("Lower tower hall") : L.W("Tower hall") });
            return regions;
        }

        if (ground && HasHall(style))
        {
            // Hall - a building without a strip of utility rooms at the back wall (farthest from the entrance).
            var main = parts[0];
            var candidates = new List<ChunkSide>();
            if (main.Height >= 7) candidates.AddRange([ChunkSide.Bottom, ChunkSide.Top]);
            if (main.Width >= 11) candidates.AddRange([ChunkSide.Left, ChunkSide.Right]);
            ChunkSide back = candidates.Count == 0
                ? (Math.Abs(front.r - main.R) > Math.Abs(front.r - (main.R + main.Height - 1)) ? ChunkSide.Bottom : ChunkSide.Top)
                : candidates.MaxBy(s => Dist(SideCenter(main, s), front) + rng.NextDouble());
            Rect hallBox = main;
            if (candidates.Count > 0)
            {
                const int depth = 3;
                var (strip, rest) = back switch
                {
                    ChunkSide.Bottom => (new Rect(main.C, main.R, main.Width, depth), new Rect(main.C, main.R + depth, main.Width, main.Height - depth)),
                    ChunkSide.Top => (new Rect(main.C, main.R + main.Height - depth, main.Width, depth), new Rect(main.C, main.R, main.Width, main.Height - depth)),
                    ChunkSide.Left => (new Rect(main.C, main.R, depth, main.Height), new Rect(main.C + depth, main.R, main.Width - depth, main.Height)),
                    _ => (new Rect(main.C + main.Width - depth, main.R, depth, main.Height), new Rect(main.C, main.R, main.Width - depth, main.Height)),
                };
                hallBox = rest;
                // The strip is cut crosswise into rooms of 3–5 cells.
                bool alongX = back is ChunkSide.Bottom or ChunkSide.Top;
                int len = alongX ? strip.Width : strip.Height, pos = 0;
                string[] backNames = style switch
                {
                    "tavern" => [L.W("Kitchen"), L.W("Pantry"), L.W("Innkeeper's room")],
                    "shop" => [L.W("Storeroom"), L.W("Workshop"), L.W("Owner's quarters")],
                    "temple" => [L.W("Vestry"), L.W("Cell"), L.W("Cell")],
                    "manor" => [L.W("Kitchen"), L.W("Study"), L.W("Library")],
                    _ => [L.W("Armory"), L.W("Commander's room"), L.W("Pantry")],
                };
                Role[] backRoles = style switch
                {
                    "tavern" => [Role.Kitchen, Role.Storage, Role.Bedroom],
                    "shop" => [Role.Storage, Role.Workshop, Role.Bedroom],
                    "temple" => [Role.Vestry, Role.Bedroom, Role.Bedroom],
                    "manor" => [Role.Kitchen, Role.Study, Role.Study],
                    _ => [Role.Armory, Role.Study, Role.Storage],
                };
                for (int i = 0; pos < len; i++)
                {
                    int size = len - pos <= 5 ? len - pos : rng.Next(3, Math.Min(5, len - pos - 3) + 1);
                    var room = alongX ? new Rect(strip.C + pos, strip.R, size, strip.Height) : new Rect(strip.C, strip.R + pos, strip.Width, size);
                    regions.Add(new Region { Cells = room.Cells().ToHashSet(), Role = backRoles[Math.Min(i, 2)], Name = backNames[Math.Min(i, 2)] });
                    pos += size;
                }
            }
            regions.Insert(0, new Region
            {
                Cells = hallBox.Cells().ToHashSet(), Role = Role.Hall, Box = hallBox, Back = back,
                Name = style switch { "tavern" => L.W("Common room"), "shop" => L.W("Shop floor"), "temple" => L.W("Sanctuary"), "manor" => L.W("Hall"), _ => L.W("Barracks") },
            });
            foreach (var wing in parts.Skip(1))
            {
                int before = regions.Count;
                Split(wing, rng, regions, hall: false, minArea: 16);
                foreach (var r in regions.Skip(before)) { r.Role = Role.Living; r.Name = style == "temple" ? L.W("Chapel") : L.W("Room"); }
            }
            return regions;
        }

        // The upper floors of the hall buildings are a corridor with rooms; at home - several rooms.
        foreach (var part in parts)
        {
            int before = regions.Count;
            bool first = part == parts[0];
            Split(part, rng, regions, hall: first && style != "house", minArea: style == "house" ? 12 : 18, forceHall: HasHall(style) && first);
            foreach (var r in regions.Skip(before).Where(r => r.Role != Role.Corridor))
            {
                (r.Role, r.Name) = style switch
                {
                    "barracks" => (Role.Hall, L.W("Barracks")),
                    "tavern" => (Role.Bedroom, L.W("Guest room")),
                    "manor" => (Role.Bedroom, L.W("Bedroom")),
                    "house" => ground ? (Role.Living, L.W("Room")) : (Role.Bedroom, L.W("Bedroom")),
                    _ => (Role.Living, L.W("Room")),
                };
            }
        }
        if (ground && style is "house" or null && regions.Count(r => r.Role != Role.Corridor) > 1)
        {
            var rooms = regions.Where(r => r.Role != Role.Corridor).ToList();
            rooms[0].Name = L.W("Main room");
            rooms[1].Role = Role.Kitchen; rooms[1].Name = L.W("Kitchen");
        }
        return regions;
    }

    // BSP: the rectangle is cut into rooms of 3+ cells along the side. A long building - sometimes a corridor along it.
    private static void Split(Rect rect, Random rng, List<Region> rooms, bool hall, int minArea, bool forceHall = false)
    {
        bool horizontal = rect.Width >= rect.Height;
        int along = horizontal ? rect.Width : rect.Height, across = horizontal ? rect.Height : rect.Width;
        if (hall && along >= 8 && across >= (forceHall ? 5 : 7) && (forceHall || rng.Next(2) == 0))
        {
            // There is a corridor in the middle, with rows of rooms on both sides.
            int mid = across / 2;
            var (a, corridor, b) = horizontal
                ? (new Rect(rect.C, rect.R, rect.Width, mid), new Rect(rect.C, rect.R + mid, rect.Width, 1), new Rect(rect.C, rect.R + mid + 1, rect.Width, across - mid - 1))
                : (new Rect(rect.C, rect.R, mid, rect.Height), new Rect(rect.C + mid, rect.R, 1, rect.Height), new Rect(rect.C + mid + 1, rect.R, across - mid - 1, rect.Height));
            rooms.Add(new Region { Cells = corridor.Cells().ToHashSet(), Role = Role.Corridor, Name = L.W("Corridor") });
            SplitAlong(a, horizontal, rng, rooms);
            SplitAlong(b, horizontal, rng, rooms);
            return;
        }

        int area = rect.Width * rect.Height;
        bool canSplitW = rect.Width >= 6, canSplitH = rect.Height >= 6;
        bool wantSplit = area > minArea || (area > minArea / 2 && rng.Next(3) == 0);
        if (!wantSplit || (!canSplitW && !canSplitH)) { rooms.Add(new Region { Cells = rect.Cells().ToHashSet() }); return; }

        bool cutVertical = canSplitW && (!canSplitH || rect.Width > rect.Height || (rect.Width == rect.Height && rng.Next(2) == 0));
        if (cutVertical)
        {
            int cut = rng.Next(3, rect.Width - 2);
            Split(new Rect(rect.C, rect.R, cut, rect.Height), rng, rooms, false, minArea);
            Split(new Rect(rect.C + cut, rect.R, rect.Width - cut, rect.Height), rng, rooms, false, minArea);
        }
        else
        {
            int cut = rng.Next(3, rect.Height - 2);
            Split(new Rect(rect.C, rect.R, rect.Width, cut), rng, rooms, false, minArea);
            Split(new Rect(rect.C, rect.R + cut, rect.Width, rect.Height - cut), rng, rooms, false, minArea);
        }
    }

    // A row of rooms along the corridor: cut only across, 3-4 cells each.
    private static void SplitAlong(Rect rect, bool horizontal, Random rng, List<Region> rooms)
    {
        int len = horizontal ? rect.Width : rect.Height, pos = 0;
        while (pos < len)
        {
            int size = len - pos <= 4 ? len - pos : rng.Next(3, Math.Min(4, len - pos - 3) + 1);
            var room = horizontal ? new Rect(rect.C + pos, rect.R, size, rect.Height) : new Rect(rect.C, rect.R + pos, rect.Width, size);
            rooms.Add(new Region { Cells = room.Cells().ToHashSet() });
            pos += size;
        }
    }

    // ── Construction: rooms, doors, courtyard, windows, furniture ────────────────────────
    private sealed class Builder(MapChunk chunk, Random rng)
    {
        public readonly char[,] G = Init();
        private readonly List<Room> _rooms = [];
        private readonly List<Door> _doors = [];
        private readonly HashSet<(int, int)> _doorCells = [];
        private readonly HashSet<(int, int)> _windowCells = [];
        private readonly Dictionary<(int, int), Region> _regionOf = [];
        private (int c, int r)? _backDoorYard;
        // Furniture supplied: view (terrain code), cells (the first one is “head”), floor under them. While the
        // generation is in progress, the codes are in G (for connectivity checks), as a result - as separate
        // objects, and in the terrain - the floor.
        private readonly List<(char code, List<(int c, int r)> cells, List<char> floor)> _pieces = [];

        private static char[,] Init()
        {
            var g = new char[W, H];
            for (int c = 0; c < W; c++) for (int r = 0; r < H; r++) g[c, r] = ' ';
            return g;
        }

        public Result Result()
        {
            var furniture = new List<Furniture>();
            foreach (var (code, cells, floor) in _pieces)
            {
                for (int i = 0; i < cells.Count; i++) G[cells[i].c, cells[i].r] = floor[i];
                var kind = FurnitureCatalog.ByCode(code);
                if (kind == null) continue;
                furniture.Add(new Furniture { Kind = kind.Kind, Name = L.W(kind.Name), Image = kind.Image, Positions = [.. cells.Select(Field)] });
            }
            var rows = new List<string>(H);
            for (int r = 0; r < H; r++)
            {
                var line = new char[W];
                for (int c = 0; c < W; c++) line[c] = G[c, r];
                rows.Add(new string(line));
            }
            return new Result(_rooms, _doors, rows, furniture);
        }

        private List<int> Field((int c, int r) x) => [x.c + chunk.OriginCol + 1, x.r + chunk.OriginRow + 1];

        public void AddDoor((int c, int r) from, (int c, int r) to, bool isDoor, bool open = false, bool entry = false, bool window = false)
        {
            _doors.Add(new Door
            {
                From = Field(from),
                To = Field(to),
                IsDoor = isDoor,
                IsDoorOpen = isDoor ? open : null,
                Color = window ? [.. WindowColor] : isDoor ? [.. DoorColor] : null,
                IsEntry = entry ? true : null,
                IsWindow = window ? true : null,
            });
            if (window) { _windowCells.Add(from); return; }
            _doorCells.Add(from);
            _doorCells.Add(to);
        }

        public void AddRooms(List<Region> regions, List<int> floor)
        {
            foreach (var reg in regions)
            {
                foreach (var x in reg.Cells) _regionOf[x] = reg;
                _rooms.Add(new Room
                {
                    Name = reg.Name,
                    Passage = reg.Role == Role.Corridor ? true : null,
                    Color = Tint(reg.Role == Role.Hall && floor == WoodFloor ? HallFloor : floor),
                    Positions = [.. reg.Cells.OrderBy(x => x.r).ThenBy(x => x.c).Select(Field)],
                });
            }
        }

        // Doors between rooms: the hall/corridor is connected to all neighbors, the rest is a spanning tree +
        // occasionally a loop. Some of the internal doors are open; There is an opening from the tavern hall to
        // the kitchen.
        public void ConnectRooms(List<Region> regions, string? style)
        {
            var index = regions.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => x.i);
            var walls = new Dictionary<(int a, int b), List<((int c, int r) x, (int c, int r) y)>>();
            foreach (var reg in regions)
                foreach (var x in reg.Cells)
                    foreach (var y in new[] { (x.c + 1, x.r), (x.c, x.r + 1) })
                        if (_regionOf.TryGetValue(y, out var other) && other != reg && index.TryGetValue(other, out int b))
                        {
                            int a = index[reg];
                            var key = (Math.Min(a, b), Math.Max(a, b));
                            if (!walls.TryGetValue(key, out var list)) walls[key] = list = [];
                            list.Add((x, y));
                        }
            var used = new HashSet<(int a, int b)>();
            int hub = regions.FindIndex(r => r.Role is Role.Hall or Role.Corridor);
            if (hub >= 0) foreach (var key in walls.Keys.Where(k => k.a == hub || k.b == hub)) used.Add(key);
            var connected = new HashSet<int> { hub >= 0 ? hub : 0 };
            foreach (var key in used) { connected.Add(key.a); connected.Add(key.b); }
            var edges = walls.Keys.OrderBy(_ => rng.Next()).ToList();
            while (connected.Count < regions.Count)
            {
                var next = edges.Where(e => connected.Contains(e.a) != connected.Contains(e.b)).Take(1).ToList();
                if (next.Count == 0) break;
                used.Add(next[0]);
                connected.Add(next[0].a);
                connected.Add(next[0].b);
            }
            foreach (var e in edges.Where(e => !used.Contains(e) && rng.Next(4) == 0)) used.Add(e);
            foreach (var e in used)
            {
                var pairs = walls[e].Where(p => !Near(p.x) && !Near(p.y)).ToList();
                if (pairs.Count == 0) pairs = walls[e];
                var (x, y) = pairs[Math.Clamp(pairs.Count / 2 + rng.Next(-pairs.Count / 3, pairs.Count / 3 + 1), 0, pairs.Count - 1)];
                var ra = regions[e.a].Role; var rb = regions[e.b].Role;
                bool passage = (style == "tavern" && ((ra == Role.Hall && rb == Role.Kitchen) || (ra == Role.Kitchen && rb == Role.Hall)))
                    || ((ra == Role.Corridor || rb == Role.Corridor) && rng.Next(4) == 0);
                AddDoor(x, y, isDoor: !passage, open: rng.Next(5) < 2);
            }
        }

        // Cell by the door (the door itself or close to it).
        private bool Near((int c, int r) x) => _doorCells.Contains(x) || Around(x).Any(_doorCells.Contains);

        // Yard/street: grass, along the edges without a neighbor - trees and thicket, towards the dungeon - rock.
        public void Ground(IReadOnlyDictionary<ChunkSide, char> bands)
        {
            for (int c = 0; c < W; c++)
                for (int r = 0; r < H; r++)
                    G[c, r] = rng.Next(10) < 3 ? ',' : '.';
            foreach (var (side, code) in bands)
            {
                int len = side is ChunkSide.Left or ChunkSide.Right ? H : W;
                for (int i = 0; i < len; i++)
                {
                    var (c, r) = side switch
                    {
                        ChunkSide.Left => (0, i), ChunkSide.Right => (W - 1, i), ChunkSide.Bottom => (i, 0), _ => (i, H - 1),
                    };
                    G[c, r] = code != NaturalBand ? code : rng.Next(10) < 6 ? 'F' : 'T'; // there are no passable bushes on the edge - do not wall up the cell
                }
            }
        }

        public bool Blocked((int c, int r) x) => TerrainCatalog.Get(G[x.c, x.r]) is { BlocksMove: true } || OnBorder(x);

        // Entrance door: outline cell (preferably hall) on the side closest to the target (or to the street);
        // there is sometimes a back door near the building.
        public (int c, int r) AddEntrance(HashSet<(int c, int r)> inside, HashSet<(int c, int r)>? prefer, (int c, int r) target,
            bool backDoor, HashSet<(int c, int r)>? street = null)
        {
            var outer = inside.SelectMany(x => Around(x).Where(n => !inside.Contains(n) && InBounds(n) && !Blocked(n)).Select(n => (room: x, yard: n))).ToList();
            var preferred = prefer == null ? outer : outer.Where(p => prefer.Contains(p.room)).ToList();
            if (preferred.Count == 0) preferred = outer;
            double Score((int c, int r) y) => street is { Count: > 0 }
                ? street.Min(s => Math.Abs(s.c - y.c) + Math.Abs(s.r - y.r)) + rng.NextDouble()
                : Math.Abs(y.c - target.c) + Math.Abs(y.r - target.r) + rng.NextDouble();
            var main = preferred.MinBy(p => Score(p.yard));
            AddDoor(main.room, main.yard, isDoor: true);
            if (backDoor && rng.Next(2) == 0)
            {
                var back = outer.Where(p => !Near(p.room)).DefaultIfEmpty(main)
                    .MaxBy(p => Math.Abs(p.yard.c - main.yard.c) + Math.Abs(p.yard.r - main.yard.r) + rng.NextDouble());
                if (back != main)
                {
                    AddDoor(back.room, back.yard, isDoor: true);
                    _backDoorYard = back.yard;
                }
            }
            return main.yard;
        }

        // Courtyard paths: from each block exit to the front door, and from the back door too.
        public void Streets(List<(int c, int r)> anchors, (int c, int r) entrance, HashSet<(int c, int r)> inside)
        {
            var trail = new HashSet<(int, int)>();
            foreach (var a in anchors)
                foreach (var x in YardPath(a, [entrance], inside) ?? []) trail.Add(x);
            if (_backDoorYard is { } back)
                foreach (var x in YardPath(back, [entrance], inside) ?? []) trail.Add(x);
            foreach (var (c, r) in trail) G[c, r] = '=';
        }

        // Entrance from outside (where the hero came from) - passage beyond the edge of the block; LocationGrower.MarkWorldExit makes it the door of the world.
        public void ExternalExits(IReadOnlyList<ChunkExit> exits)
        {
            foreach (var exit in exits.Where(e => e.External))
            {
                var a = Local(exit.AnchorCell(chunk), chunk);
                var (dx, dy) = ChunkExit.Step(exit.Side);
                G[a.c, a.r] = '=';
                AddDoor(a, (a.c + dx, a.r + dy), isDoor: false, entry: true);
            }
        }

        // Trees and bushes are not on the path or near houses; the yard remains connected.
        public void Trees(HashSet<(int c, int r)> inside, (int c, int r) start)
        {
            var yard = Enumerable.Range(1, W - 2).SelectMany(c => Enumerable.Range(1, H - 2).Select(r => (c, r)))
                .Where(x => !inside.Contains(x) && G[x.c, x.r] is '.' or ',').ToList();
            int count = yard.Count / 16;
            for (int i = 0; i < count && yard.Count > 0; i++)
            {
                var x = yard[rng.Next(yard.Count)];
                if (Around8(x).Any(n => inside.Contains(n) || (InBounds(n) && G[n.c, n.r] == '='))) continue;
                char was = G[x.c, x.r];
                G[x.c, x.r] = rng.Next(3) == 0 ? '"' : 'T';
                if (!OutsideConnected(inside, start)) G[x.c, x.r] = was;
            }
        }

        // A closed ditch and a single ford; the outlets converge on the outer bank.
        public void Moat(HashSet<(int c, int r)> inside, (int c, int r) entrance, List<(int c, int r)> anchors)
        {
            int left = inside.Min(x => x.c) - 1, right = inside.Max(x => x.c) + 1;
            int bottom = inside.Min(x => x.r) - 1, top = inside.Max(x => x.r) + 1;
            bool Ring((int c, int r) x) => x.c >= left && x.c <= right && x.r >= bottom && x.r <= top
                && (x.c == left || x.c == right || x.r == bottom || x.r == top);
            bool Outside((int c, int r) x) => x.c < left || x.c > right || x.r < bottom || x.r > top;
            List<(int c, int r)> Path((int c, int r) start, Func<(int c, int r), bool> goal, bool outer)
            {
                var prev = new Dictionary<(int c, int r), (int c, int r)> { [start] = start };
                var q = new Queue<(int c, int r)>([start]);
                while (q.Count > 0)
                {
                    var x = q.Dequeue();
                    if (goal(x))
                    {
                        var path = new List<(int c, int r)> { x };
                        while (x != start) { x = prev[x]; path.Add(x); }
                        return path;
                    }
                    foreach (var n in Around(x))
                        if (InBounds(n) && !inside.Contains(n) && (!outer || Outside(n)) && prev.TryAdd(n, x)) q.Enqueue(n);
                }
                throw new InvalidOperationException("No approach to island");
            }
            var approach = Path(entrance, Outside, false);
            var end = approach[0];
            for (int c = 0; c < W; c++)
                for (int r = 0; r < H; r++)
                    if (!inside.Contains((c, r))) G[c, r] = Ring((c, r)) ? 'W' : '.';
            foreach (var x in approach) G[x.c, x.r] = Ring(x) ? '~' : '=';
            foreach (var anchor in anchors)
                foreach (var x in Path(anchor, x => x == end, true)) G[x.c, x.r] = '=';
        }

        // All passable cells outside houses are connected (by 4 neighbors).
        public bool OutsideConnected(HashSet<(int c, int r)> inside, (int c, int r) start)
        {
            bool Free((int c, int r) x) => !inside.Contains(x) && G[x.c, x.r] != ' ' && TerrainCatalog.Get(G[x.c, x.r]) is not { BlocksMove: true };
            if (!Free(start)) start = Enumerable.Range(0, W).SelectMany(c => Enumerable.Range(0, H).Select(r => (c, r))).FirstOrDefault(Free);
            int total = Enumerable.Range(0, W).Sum(c => Enumerable.Range(0, H).Count(r => Free((c, r))));
            var seen = new HashSet<(int, int)> { start };
            var queue = new Queue<(int c, int r)>([start]);
            while (queue.Count > 0)
                foreach (var n in Around(queue.Dequeue()))
                    if (InBounds(n) && Free(n) && seen.Add(n)) queue.Enqueue(n);
            return seen.Count == total;
        }

        // The shortest path through the yard (outside the houses, not along the edge of the block - just starts from it) to any of the goals.
        public List<(int c, int r)>? YardPath((int c, int r) from, ICollection<(int c, int r)> to, HashSet<(int c, int r)> inside)
        {
            var prev = new Dictionary<(int, int), (int, int)> { [from] = from };
            var queue = new Queue<(int c, int r)>([from]);
            while (queue.Count > 0)
            {
                var x = queue.Dequeue();
                if (to.Contains(x))
                {
                    var path = new List<(int, int)> { x };
                    while (x != from) { x = prev[x]; path.Add(x); }
                    return path;
                }
                foreach (var n in Around(x))
                    if (InBounds(n) && !OnBorder(n) && !inside.Contains(n) && G[n.c, n.r] != 'V' && prev.TryAdd(n, x)) queue.Enqueue(n);
            }
            return null;
        }

        // Upper floors/basements: access to the adjacent block of the same floor - a gallery from the edge of the block to the outline.
        public void Galleries(IReadOnlyList<ChunkExit> exits, HashSet<(int c, int r)> inside, IReadOnlyCollection<ChunkSide> seamDoorSides, bool basement)
        {
            foreach (var exit in exits)
            {
                var anchor = Local(exit.AnchorCell(chunk), chunk);
                var path = PathToFootprint(anchor, inside);
                if (path == null) continue;
                var gallery = path.Where(x => !inside.Contains(x)).ToList();
                if (gallery.Count > 0)
                {
                    _rooms.Add(new Room
                    {
                        Name = L.W("Gallery"),
                        Passage = true,
                        Color = Tint(basement ? StoneFloor : WoodFloor),
                        Positions = [.. gallery.Select(Field)],
                    });
                    AddDoor(gallery[^1], path[^1], isDoor: rng.Next(2) == 0, open: rng.Next(2) == 0);
                    foreach (var x in gallery) G[x.c, x.r] = basement ? 'q' : 'w';
                }
                if (seamDoorSides.Contains(exit.Side))
                {
                    var (dx, dy) = ChunkExit.Step(exit.Side);
                    AddDoor(anchor, (anchor.c + dx, anchor.r + dy), isDoor: false, entry: true);
                }
            }
        }

        public void Floor(HashSet<(int c, int r)> inside, bool basement)
        {
            foreach (var x in inside)
                G[x.c, x.r] = basement ? (rng.Next(12) == 0 ? 'k' : 'q')
                    : _regionOf.TryGetValue(x, out var reg) && reg.Role == Role.Hall && chunk.Style == "temple" ? 'p' : 'w';
        }

        // Windows in outer walls: about 3 squares apart, not at doors or next to each other.
        public void Windows(List<Region> regions, HashSet<(int c, int r)> inside)
        {
            foreach (var reg in regions)
            {
                if (reg.Role is Role.Storage) continue;
                var outer = reg.Cells.SelectMany(x => Around(x).Where(n => !inside.Contains(n)).Select(n => (x, n)))
                    .OrderBy(p => p.x.r).ThenBy(p => p.x.c).ToList();
                int step = rng.Next(3);
                foreach (var (x, n) in outer)
                {
                    if (step++ % 3 != 0) continue;
                    if (Near(x) || _doorCells.Contains(n) || _windowCells.Contains(x) || Around(x).Any(_windowCells.Contains)) continue;
                    AddDoor(x, n, isDoor: true, window: true);
                }
            }
        }

        // ── Furniture ───────────────────────────── ───────────────────────────── Along the walls (a cell with
        // a neighbor outside the room) there are wardrobes, beds, a fireplace, barrels; in the middle there are
        // tables. Not at doors and windows; the room remains coherent after each item. Hall - according to the
        // type of building.
        public void Furnish(Region reg, bool basement)
        {
            var cells = reg.Cells;
            if (cells.Count < 4 || reg.Role == Role.Corridor) return;
            bool Busy((int c, int r) x) => Near(x) || _windowCells.Contains(x);
            var wallSide = cells.Where(x => Around(x).Any(n => !cells.Contains(n)) && !Busy(x)).OrderBy(_ => rng.Next()).ToList();
            var middle = cells.Where(x => Around8(x).All(cells.Contains) && !Busy(x)).OrderBy(_ => rng.Next()).ToList();

            if (reg.Role == Role.Hall && reg.Box is { } box && reg.Back is { } back) { FurnishHall(reg, box, back, wallSide, middle); return; }

            char[] palette = basement ? ['X', 'X', 'H'] : reg.Role switch
            {
                Role.Kitchen => ['O', 'X', 'H', 'X'],
                Role.Storage => ['X', 'X', 'H', 'X'],
                Role.Bedroom => ['E', 'H', 'E'],
                Role.Workshop => ['H', 'X', 'H'],
                Role.Vestry => ['H', 'H'],
                Role.Armory => ['H', 'H', 'X'],
                Role.Study => ['H', 'H', 'O'],
                Role.Hall => ['E', 'E', 'H', 'E', 'E'],   // barracks upstairs
                Role.Tower => ['H', 'X'],
                _ => ['H', 'O', 'E'],
            };
            int items = Math.Min(wallSide.Count, 1 + cells.Count / (reg.Role == Role.Hall ? 4 : 8));
            bool hearth = false;
            for (int i = 0; i < items; i++)
            {
                char code = palette[i % palette.Length];
                if (code == 'O' && hearth) code = 'H';
                bool ok = code == 'E' ? TryBed(cells, wallSide[i]) : Try(cells, wallSide[i], code);
                if (ok && code == 'O') hearth = true;
            }
            if (middle.Count > 0 && reg.Role is not (Role.Storage or Role.Vestry or Role.Armory) && !basement) Try(cells, middle[0], 'Y');
        }

        private void FurnishHall(Region reg, Rect box, ChunkSide back, List<(int c, int r)> wallSide, List<(int c, int r)> middle)
        {
            var cells = reg.Cells;
            string? style = chunk.Style;
            bool alongX = back is ChunkSide.Bottom or ChunkSide.Top;
            int along = alongX ? box.Width : box.Height, depthMax = alongX ? box.Height : box.Width;
            // Hall cell: i - along the back wall, d - distance from it.
            (int c, int r) At(int i, int d) => back switch
            {
                ChunkSide.Bottom => (box.C + i, box.R + d),
                ChunkSide.Top => (box.C + i, box.R + box.Height - 1 - d),
                ChunkSide.Left => (box.C + d, box.R + i),
                _ => (box.C + box.Width - 1 - d, box.R + i),
            };
            int DistBack((int c, int r) x) => back switch
            {
                ChunkSide.Bottom => x.r - box.R,
                ChunkSide.Top => box.R + box.Height - 1 - x.r,
                ChunkSide.Left => x.c - box.C,
                _ => box.C + box.Width - 1 - x.c,
            };

            if (style is "tavern" or "shop")
            {
                // Counter/counter in a row against the back wall (the owner is behind it), with an aisle at one
                // end. Entirely, without breaks: we go through the length and position until the entire row
                // stands (not at the doors and windows).
                char code = style == "tavern" ? 'B' : 'L';
                bool placed = false;
                for (int len = Math.Clamp(along / 2, 3, 5); len >= 2 && !placed; len--)
                    foreach (int start in Enumerable.Range(1, Math.Max(1, along - len - 1)).OrderBy(_ => rng.Next()))
                    {
                        var run = Enumerable.Range(start, len).Select(i => At(i, 1)).ToList();
                        if (!TryAll(cells, run, code)) continue;
                        if (style == "tavern") Try(cells, At(start <= along / 2 ? 0 : along - 1, 0), 'X');
                        placed = true;
                        break;
                    }
            }
            if (style == "temple")
            {
                // Altar at the back wall in the middle, benches in rows with an aisle in the center.
                Try(cells, At(along / 2, 1), 'A');
                for (int d = 3; d < depthMax - 1; d += 2)
                    foreach (var (from, to) in new[] { (1, along / 2 - 1), (along / 2 + 1, along - 2) })
                    {
                        if (to < from) continue;
                        var run = Enumerable.Range(from, to - from + 1).Select(i => At(i, d)).ToList();
                        if (!TryAll(cells, run, 'N')) foreach (var x in run) Try(cells, x, 'N');
                    }
                return;
            }
            if (style == "barracks")
            {
                for (int i = 0; i < wallSide.Count; i += 2) { if (i == 0) Try(cells, wallSide[i], 'H'); else TryBed(cells, wallSide[i]); }
                if (middle.Count > 0) Try(cells, middle[0], 'Y');
                return;
            }
            // Tables in a grid in the hall (not in front of the counter), a fireplace near the side wall, cabinets.
            int phase = rng.Next(3);
            foreach (var x in middle.Where(x => DistBack(x) >= 3).OrderBy(x => x.c).ThenBy(x => x.r))
                if ((x.c + phase) % 3 == 0 && x.r % 2 == 0 && rng.Next(5) > 0) TryTable(cells, x);
            var sideWalls = wallSide.Where(x => DistBack(x) >= 2).ToList();
            if (sideWalls.Count > 0 && style != "shop") Try(cells, sideWalls[0], 'O');
            foreach (var x in sideWalls.Skip(1).Take(style == "shop" ? 4 : 2)) Try(cells, x, 'H');
        }

        // All cells at once or none (solid rack).
        private bool TryAll(HashSet<(int c, int r)> cells, List<(int c, int r)> run, char code)
        {
            if (run.Any(x => !cells.Contains(x) || TerrainCatalog.Get(G[x.c, x.r]) is { BlocksMove: true } || Near(x) || _windowCells.Contains(x)))
                return false;
            var was = run.Select(x => G[x.c, x.r]).ToList();
            foreach (var x in run) G[x.c, x.r] = code;
            if (Connected(cells)) { _pieces.Add((code, run, was)); return true; }
            for (int i = 0; i < run.Count; i++) G[run[i].c, run[i].r] = was[i];
            return false;
        }

        private bool Try(HashSet<(int c, int r)> cells, (int c, int r) x, char code)
        {
            if (!cells.Contains(x) || TerrainCatalog.Get(G[x.c, x.r]) is { BlocksMove: true }) return false;
            if (Near(x) || _windowCells.Contains(x)) return false;
            char was = G[x.c, x.r];
            G[x.c, x.r] = code;
            if (Connected(cells)) { _pieces.Add((code, [x], [was])); return true; }
            G[x.c, x.r] = was;
            return false;
        }

        // Bed 1x2: headboard (pillow) against the wall, foot part - deep into the room; it didn’t work out - one cell.
        private bool TryBed(HashSet<(int c, int r)> cells, (int c, int r) x)
        {
            foreach (var n in Around(x).Where(n => !cells.Contains(n)))
            {
                var foot = (x.c + (x.c - n.c), x.r + (x.r - n.r));
                if (TryAll(cells, [x, foot], 'E')) return true;
            }
            return Try(cells, x, 'E');
        }

        // Table with 2 squares in a row, otherwise one square.
        private bool TryTable(HashSet<(int c, int r)> cells, (int c, int r) x) =>
            TryAll(cells, [x, (x.c + 1, x.r)], 'Y') || Try(cells, x, 'Y');

        private bool Connected(HashSet<(int c, int r)> cells)
        {
            var free = cells.Where(x => TerrainCatalog.Get(G[x.c, x.r]) is not { BlocksMove: true }).ToHashSet();
            if (free.Count == 0) return false;
            var start = free.First();
            var seen = new HashSet<(int, int)> { start };
            var queue = new Queue<(int c, int r)>([start]);
            while (queue.Count > 0)
                foreach (var n in Around(queue.Dequeue()))
                    if (free.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            return seen.Count == free.Count;
        }

        private List<int> Tint(List<int> baseColor)
        {
            int d = rng.Next(-6, 7);
            return [.. baseColor.Select(v => Math.Clamp(v + d, 0, 255))];
        }
    }

    // Path from the block edge cell to the cell next to the outline (the last cell of the path is inside the outline).
    private static List<(int c, int r)>? PathToFootprint((int c, int r) from, HashSet<(int c, int r)> inside)
    {
        if (inside.Contains(from)) return [from];
        var prev = new Dictionary<(int, int), (int, int)> { [from] = from };
        var queue = new Queue<(int c, int r)>([from]);
        while (queue.Count > 0)
        {
            var x = queue.Dequeue();
            foreach (var n in Around(x))
            {
                if (!InBounds(n) || !prev.TryAdd(n, x)) continue;
                if (inside.Contains(n))
                {
                    var path = new List<(int c, int r)> { n };
                    var p = x;
                    path.Add(p);
                    while (p != from) { p = prev[p]; path.Add(p); }
                    path.Reverse();
                    return path;
                }
                queue.Enqueue(n);
            }
        }
        return null;
    }

    private static (int c, int r) SideCenter(Rect r, ChunkSide side) => side switch
    {
        ChunkSide.Bottom => (r.C + r.Width / 2, r.R),
        ChunkSide.Top => (r.C + r.Width / 2, r.R + r.Height - 1),
        ChunkSide.Left => (r.C, r.R + r.Height / 2),
        _ => (r.C + r.Width - 1, r.R + r.Height / 2),
    };

    private static int Dist((int c, int r) a, (int c, int r) b) => Math.Abs(a.c - b.c) + Math.Abs(a.r - b.r);

    private static (int c, int r)[] Around((int c, int r) x) => [(x.c + 1, x.r), (x.c - 1, x.r), (x.c, x.r + 1), (x.c, x.r - 1)];

    private static IEnumerable<(int c, int r)> Around8((int c, int r) x)
    {
        for (int dc = -1; dc <= 1; dc++)
            for (int dr = -1; dr <= 1; dr++)
                if (dc != 0 || dr != 0) yield return (x.c + dc, x.r + dr);
    }

    private static bool OnBorder((int c, int r) x) => x.c == 0 || x.r == 0 || x.c == W - 1 || x.r == H - 1;

    private static bool InBounds((int c, int r) x) => x.c >= 0 && x.r >= 0 && x.c < W && x.r < H;

    private static (int c, int r) Local((int col, int row) cell, MapChunk chunk) => (cell.col - chunk.OriginCol - 1, cell.row - chunk.OriginRow - 1);
}
