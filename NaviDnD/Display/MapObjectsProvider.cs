using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;

namespace NaviDnD;

public class MapObjectsProvider
{
    private readonly WorldState _settings;
    private readonly DisplayConfig _display;
    private readonly ColorSetting _colors;
    private readonly Storage _storage;
    private readonly AppConfig _config;

    private readonly IGridSymbolProvider _gridProvider = new SimpleGridSymbolProvider();
    private readonly IGridSymbolProvider _wallGridProvider = new WallGridSymbolProvider();

    private List<Door>? _doors = [];
    private readonly Dictionary<(int, int, int, int), List<Door>> _doorsByEdge = [];
    private (int minCol, int maxCol, int minRow, int maxRow)? _contentBounds;

    private static (int, int, int, int) EdgeKey(int x1, int y1, int x2, int y2) =>
        x1 < x2 || (x1 == x2 && y1 <= y2) ? (x1, y1, x2, y2) : (x2, y2, x1, y1);
    private HashSet<(int X, int Y)> _roomPositions = [];
    private Dictionary<(int X, int Y), int> _roomIndex = [];  // position → room index
    private int _heroRoomIndex = -1;

    private Dictionary<(int X, int Y), CellEntity> _objects = [];
    private Dictionary<(int X, int Y), CellEntity> _entities = [];
    private Dictionary<(int x, int y), List<int>> _areaColors = [];
    private Dictionary<(int X, int Y), List<int>> _roomColors = [];
    private Dictionary<(int R, int G, int B), int> _areaCellCounts = [];
    private readonly Dictionary<(int X, int Y), TerrainKind> _terrain = [];
    private readonly Dictionary<(int X, int Y), TerrainKind> _ground = [];        // terrain/floor without furniture
    private readonly Dictionary<(int X, int Y), Furniture> _furnitureOf = [];   // cell → piece of furniture
    private readonly HashSet<(int X, int Y)> _projected = [];                   // view from the top floor window
    private bool _heroOnTerrain;
    private int _heroFloor;

    public MapObjectsProvider(WorldState settings, DisplayConfig display, Storage storage, AppConfig config = null)
    {
        _settings = settings;
        _display = display;
        _colors = settings.Map.Colors;
        _storage = storage;
        _config = config ?? new AppConfig();

        // Static data: rooms, zones, doors - do not change with local movement
        if (settings.Map.Rooms != null && settings.Map.Rooms.Count > 0)
        {
            for (int roomIdx = 0; roomIdx < settings.Map.Rooms.Count; roomIdx++)
            {
                var room = settings.Map.Rooms[roomIdx];
                foreach (var pos in room.Positions)
                {
                    int x = pos[0], y = pos[1];
                    _roomPositions.Add((x, y));
                    _roomIndex[(x, y)] = roomIdx;
                    if (!_roomColors.ContainsKey((x, y)))
                        _roomColors[(x, y)] = room.Color;
                }
            }
        }

        _doors = settings.Map.Doors ?? [];
        // Doors along the wall (a pair of cells) - the review checks the wall at every step of each beam.
        foreach (var door in _doors)
            if (door.From is { Count: >= 2 } f && door.To is { Count: >= 2 } t)
            {
                var key = EdgeKey(f[0], f[1], t[0], t[1]);
                if (!_doorsByEdge.TryGetValue(key, out var at)) _doorsByEdge[key] = at = [];
                at.Add(door);
            }

        if (settings.Map.Area != null)
        {
            foreach (var area in settings.Map.Area)
            {
                var colorKey = (area.Color[0], area.Color[1], area.Color[2]);
                foreach (var pos in area.Positions)
                {
                    int x = pos[0], y = pos[1];
                    if (!_areaColors.ContainsKey((x, y)))
                    {
                        _areaColors[(x, y)] = area.Color;
                        _areaCellCounts[colorKey] = _areaCellCounts.GetValueOrDefault(colorKey) + 1;
                    }
                }
            }
        }

        // Terrain of an open area: the background of the cells is the same as that of the zones (the zone on top
        // of the terrain is more important), the grid lines between the cells take the same background.
        foreach (var chunk in settings.Map.Chunks ?? [])
        {
            if (chunk.Terrain is not { } rows) continue;
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < rows[r].Length; c++)
                {
                    if (TerrainCatalog.Get(rows[r][c]) is not { } kind) continue;
                    var pos = (chunk.OriginCol + c + 1, chunk.OriginRow + r + 1);
                    _terrain[pos] = kind;
                    _ground[pos] = kind;
                    // Dungeon/building floor - on top of room color (staircase outside rooms - with its own background).
                    if ((kind.Indoor && _roomPositions.Contains(pos)) || _areaColors.ContainsKey(pos)) continue;
                    _areaColors[pos] = kind.Bg;
                    var key = (kind.Bg[0], kind.Bg[1], kind.Bg[2]);
                    _areaCellCounts[key] = _areaCellCounts.GetValueOrDefault(key) + 1;
                }
        }

        Refresh();
    }

    // Only updates dynamic state: entities and visibility. Call before each render instead of creating a new
    // instance.
    public void Refresh()
    {
        _entities.Clear();
        _objects.Clear();

        foreach (var e in _settings.Map.Entities)
        {
            if (e.Deleted != true && e.Position is { Count: >= 2 } && (_config.RevealMap || e.Hidden != true))
                _entities[(e.Position[0], e.Position[1])] = e;
        }
        foreach (var o in _settings.Map.Objects)
        {
            if (o.Deleted != true && o.Position is { Count: >= 2 } && (_config.RevealMap || o.Hidden != true))
                _objects[(o.Position[0], o.Position[1])] = o;
        }
        // Without a tactical map (a hero on the world map), there may be no position - there is nothing to draw on the terrain.
        if (_settings.Hero?.Position is not { Count: >= 2 })
        {
            _heroRoomIndex = -1;
            return;
        }
        _entities[(_settings.Hero.Position[0], _settings.Hero.Position[1])] = _settings.Hero;

        var heroPos = (_settings.Hero.Position[0], _settings.Hero.Position[1]);
        _heroRoomIndex = _roomIndex.TryGetValue(heroPos, out var heroRoom) ? heroRoom : -1;
        _heroOnTerrain = _terrain.TryGetValue(heroPos, out var heroTerrain) && !heroTerrain.Indoor;
        _heroFloor = _settings.Map.FloorAt(heroPos.Item1, heroPos.Item2);

        _contentBounds = _settings.Map.ContentBounds();   // once per frame rather than per view pass/light source
        ApplyFurniture();
        ApplyOutsideView(heroPos.Item1, heroPos.Item2);
        ComputeHeroVisibility(heroPos.Item1, heroPos.Item2);
        RememberSeenObjects();
    }

    // ------------------------------------------------------------- Public methods for rendering
    // -----------------------------------------------------------------------------

    private (bool visible, bool explored) GetWallState(double x, double y, bool isHorizontal)
    {
        (int x1, int y1, int x2, int y2) cells;

        if (isHorizontal)
        {
            int xCoord = (int)x;
            cells = (xCoord, (int)(y - 0.5), xCoord, (int)(y + 0.5));
        }
        else
        {
            int yCoord = (int)y;
            cells = ((int)(x - 0.5), yCoord, (int)(x + 0.5), yCoord);
        }

        return EdgeState((cells.x1, cells.y1), (cells.x2, cells.y2));
    }

    // Visibility of the line/angle between cells: visible if there is a visible cell nearby that does NOT block
    // the view, or all neighboring ones are visible (border inside a thicket/rock). Otherwise, the line behind
    // the tree/rock would “shine through”: the tree itself is visible, but what is behind it is not. Also for
    // exploration (fog). Furniture and columns inside rooms (Indoor) do not hide the wall behind them - the
    // wall of the room is always visible when the cell is visible.
    private (bool visible, bool explored) EdgeState(params (int x, int y)[] cells)
    {
        bool Opaque((int x, int y) c) => _terrain.TryGetValue(c, out var k) && k.BlocksSight && !k.Indoor;
        bool visible = cells.Any(c => IsCellVisible(c.x, c.y) && !Opaque(c)) || cells.All(c => IsCellVisible(c.x, c.y));
        bool explored = cells.Any(c => IsCellExplored(c.x, c.y) && !Opaque(c)) || cells.All(c => IsCellExplored(c.x, c.y));
        return (visible, explored);
    }

    public void WriteLine(double x, double y, bool isHorisonal)
    {
        var (visible, explored) = GetWallState(x, y, isHorisonal);
        var delimetr = isHorisonal ? new string(_display.Delimetr, _display.CellWidth) : _display.Delimetr.ToString();
        if (!visible && !explored)
        {
            ColorHelper.WriteColored(delimetr, fgColor: MapBg, bgColor: MapBg);
            return;
        }

        // The edge of the open area near the void is not a wall, but the same void (without the blackened line of fog).
        if (GetDoorAtWall(x, y, isHorisonal) == null && (isHorisonal
                ? IsOpenEdge(((int)x, (int)(y - 0.5)), ((int)x, (int)(y + 0.5)))
                : IsOpenEdge(((int)(x - 0.5), (int)y), ((int)(x + 0.5), (int)y))))
        {
            ColorHelper.WriteColored(delimetr, fgColor: MapBg, bgColor: MapBg);
            return;
        }

        var bg = GetBackgroundForWall(x, y, isHorisonal);

        if (!visible && explored)
        {
            bg = ColorHelper.Darker(bg, 0.5);
        }

        int bridge = OriginBridgeLevel(x, y, isHorisonal);
        bg = bridge > 0 ? TintByLevel(bg, bridge)
            : isHorisonal
                ? ApplyTargetTintBetween(bg, ((int)x, (int)(y - 0.5)), ((int)x, (int)(y + 0.5)))
                : ApplyTargetTintBetween(bg, ((int)(x - 0.5), (int)y), ((int)(x + 0.5), (int)y));

        var door = GetDoorAtWall(x, y, isHorisonal);

        // If there is a door with a color, draw a door symbol
        if (door != null && door.IsWindow == true && door.Hidden != true)
        {
            // Window: dotted along the wall line, open - with an opening in the middle.
            bool openWindow = door.IsDoorOpen ?? false;
            string windowText = isHorisonal
                ? (openWindow ? "┄ ┄" : new string('┄', _display.CellWidth))
                : (openWindow ? "╎" : "┆");
            var windowColor = _display.HoveredDoor == door ? ColorHelper.Pale(WindowColor, 0.6) : WindowColor;
            if (!visible) windowColor = ColorHelper.Darker(windowColor, 0.5);
            ColorHelper.WriteColored(windowText, fgColor: windowColor, bgColor: bg);
        }
        else if (door != null && IsDoor(door) && door.Hidden != true && (_config.RevealMap || door.Hidden != true))
        {
            string? doorText = null;

            if (isHorisonal)
            {
                if (door.IsDoorOpen ?? false)
                {
                    doorText = IsRightDoorOfHorizontalPair(x, y)
                        ? new string(' ', _display.CellWidth - 1) + "■"
                        : "■" + new string(' ', _display.CellWidth - 1);
                }
                else
                {
                    doorText = new string('■', _display.CellWidth);
                }
            }
            else
            {
                if (door.IsDoorOpen ?? false)
                    doorText = IsBottomDoorOfVerticalPair(x, y) ? "▄" : "▀";
                else
                    doorText = "█";
            }

            var doorColor = GetDoorColor(door);
            if (_display.HoveredDoor == door)
                doorColor = ColorHelper.Pale(doorColor, 0.75);

            ColorHelper.WriteColored(doorText, fgColor: doorColor, bgColor: bg);
        }
        else
        {
            var provider = GetProviderForWall(x, y, isHorisonal, door);
            var fg = provider == _wallGridProvider
                ? ColorHelper.Darker(bg, 0.35)
                : ColorHelper.Darker(bg);

            var lineText = isHorisonal ? new string(provider.Horizontal, _display.CellWidth) : provider.Vertical.ToString();
            // A stand/bench of several cells is solid: the line between them is drawn with its own beam.
            var (c1, c2) = isHorisonal
                ? (((int)x, (int)(y - 0.5)), ((int)x, (int)(y + 0.5)))
                : (((int)(x - 0.5), (int)y), ((int)(x + 0.5), (int)y));
            if (provider == _gridProvider && _terrain.TryGetValue(c1, out var k1) && k1.Join is { } join && SamePiece(c1, c2))
            {
                var (jfg, jbg) = TerrainColors(k1, GetBackgroundForWall(x, y, isHorisonal));
                if (!visible) { jfg = ColorHelper.Darker(jfg, 0.5); jbg = ColorHelper.Darker(jbg, 0.5); }
                string jointText = !isHorisonal ? join[0].ToString()
                    : HasRowNeighbour(c1.Item1, c1.Item2) ? new string(join[0], _display.CellWidth) : $" {join[1]} ";
                ColorHelper.WriteColored(jointText, fgColor: jfg, bgColor: jbg);
                return;
            }
            // The divider inside the floor (not the wall) is an extension of the tile.
            bool tiled = provider == _gridProvider && (isHorisonal
                ? IsTiledFloor((int)x, (int)(y - 0.5)) && IsTiledFloor((int)x, (int)(y + 0.5))
                : IsTiledFloor((int)(x - 0.5), (int)y) && IsTiledFloor((int)(x + 0.5), (int)y));
            if (tiled)
                WriteTiled(lineText, isHorisonal ? 4 * (int)x - 3 : (int)(4 * x) - 2, (int)(2 * y), fg, bg,
                    isHorisonal ? TileKind((int)x, (int)(y - 0.5)) : TileKind((int)(x - 0.5), (int)y));
            else
                ColorHelper.WriteColored(lineText, fgColor: fg, bgColor: bg);
        }
    }

    public void WriteLineHorisontal(double x, double y) => WriteLine(x, y, true);

    public void WriteLineVertical(double x, double y) => WriteLine(x, y, false);

    private bool IsCellExplored(int x, int y)
    {
        if (_config.RevealMap) return true;
        if (IsVoid(x, y)) return false;
        return _storage.ExploredCells.Contains((x, y));
    }

    // The emptiness of a block location (outside of rooms, zones and terrain): never "seen" or "explored" -
    // drawn by the background of the screen, not a blackened cell of fog. The walls/doors at its edge are
    // visible from the adjacent cell. The other floors (MapChunk.Z ≠ the hero’s floor) are also empty: only the
    // hero’s floor is on the screen.
    private bool IsVoid(int x, int y) =>
        _settings.Map.Chunks is { Count: > 0 } && !_projected.Contains((x, y))
        && ((!_roomPositions.Contains((x, y)) && !_areaColors.ContainsKey((x, y))) || _settings.Map.FloorAt(x, y) != _heroFloor);

    // The line/angle at the junction of an open area with a void (there is a void among the cells, there are no
    // rooms) - is drawn as a void. The edge of the dungeon rooms is a real wall, we don’t touch it.
    private bool IsOpenEdge(params (int x, int y)[] cells) =>
        cells.Any(c => IsVoid(c.x, c.y)) && !cells.Any(c => _roomPositions.Contains(c) && !IsVoid(c.x, c.y));
    private static string ObjectKey(CellEntity o) => Storage.ObjectKey(o);

    // The items that the hero sees now are in memory (Storage.SeenObjects); after loading the save - everything
    // that lies on the already studied cells.
    private void RememberSeenObjects()
    {
        bool seed = _storage.SeenObjects == null;
        var seen = _storage.SeenObjects ??= [];
        foreach (var ((x, y), o) in _objects)
            if (_storage.VisibleCells.Contains((x, y)) || (seed && _storage.ExploredCells.Contains((x, y))))
                seen.Add(ObjectKey(o));
    }

    public void WriteCellEntity(int x, int y)
    {
        bool visible = IsCellVisible(x, y);
        bool explored = IsCellExplored(x, y);
        var delimetrText = new string(_display.Delimetr, _display.CellWidth);

        if (!visible && !explored)
        {
            // not seen at all
            ColorHelper.WriteColored(delimetrText, fgColor: MapBg, bgColor: MapBg);
            return;
        }

        var bg = GetBackgroundColor(x, y);

        if (!visible && explored)
        {
            // 👇 SEEN → draw “in the fog” (the guidance is highlighted here too - the cell has been studied)
            var grayBg = ApplyTargetTint(x, y, ColorHelper.Darker(bg, 0.5));
            if (_display.HoveredCell == (x, y)) grayBg = ColorHelper.Pale(grayBg, 0.22);
            // The terrain in the fog is a dull pattern (the forest/rocks are remembered, no creatures are visible).
            string fogText = delimetrText;
            List<int> fogFg = grayBg, fogBg = grayBg;
            if (_terrain.TryGetValue((x, y), out var fogKind))
            {
                fogText = JoinedGlyph(fogKind, x, y) ?? TerrainCatalog.Glyph(fogKind, x, y);
                if (fogKind.Indoor)
                {
                    // We count the colors according to the original floor and extinguish them entirely - otherwise the bright furniture would not dim in the fog.
                    var (lf, lb) = TerrainColors(fogKind, bg);
                    lb = PieceBg(fogKind, x, y, bg, lb);
                    fogFg = ColorHelper.Darker(lf, 0.5);
                    fogBg = ReferenceEquals(lb, bg) ? grayBg : ColorHelper.Darker(lb, 0.5);
                }
                else
                {
                    (fogFg, fogBg) = TerrainColors(fogKind, grayBg);
                    fogFg = ColorHelper.Darker(fogKind.Fg, 0.5);
                }
            }
            // An object that the hero has already seen is remembered in the fog - as a dim symbol against the
            // background of the fog. The creatures are not: the hero does not know where they are now.
            if (_objects.TryGetValue((x, y), out var seenObj) && _storage.SeenObjects?.Contains(ObjectKey(seenObj)) == true)
            {
                ColorHelper.WriteColored(seenObj.Symbol, fgColor: ColorHelper.Darker(ColorHelper.Saturate(seenObj.Color), 0.55), bgColor: fogBg);
                return;
            }
            if (fogKind != null && TryWriteSingleBed(fogKind, x, y, bg, fogBg, dim: true)) return;
            if (IsTiledFloor(x, y)) WriteTiled(fogText, 4 * x - 3, 2 * y, fogFg, fogBg, TileKind(x, y));
            else ColorHelper.WriteColored(fogText, fgColor: fogFg, bgColor: fogBg);
            return;
        }

        bool isHovered = _display.HoveredCell == (x, y);
        if (isHovered) bg = ColorHelper.Pale(bg, 0.22);
        bg = ApplyTargetTint(x, y, bg);

        // 👇 normal visible logic
        if (_entities.TryGetValue((x, y), out var entity) || _objects.TryGetValue((x, y), out entity))
        {
            var paledBg = ColorHelper.Pale(bg);
            bool isHero = entity == _settings.Hero;
            var cellBg = isHero ? ColorHelper.MixWith(paledBg, [0, 200, 80], 0.18) : paledBg;
            ColorHelper.WriteColored(entity.Symbol,
                fgColor: ColorHelper.Saturate(entity.Color),
                bgColor: cellBg);
        }
        else
        {
            string text = delimetrText;
            List<int> fg = _colors.MapForeground, cellBg = bg;
            if (_terrain.TryGetValue((x, y), out var kind))
            {
                text = JoinedGlyph(kind, x, y) ?? TerrainCatalog.Glyph(kind, x, y);
                (fg, cellBg) = TerrainColors(kind, bg);
                cellBg = PieceBg(kind, x, y, bg, cellBg);
                if (isHovered && !kind.Indoor) fg = ColorHelper.Pale(fg, 0.22);
                if (TryWriteSingleBed(kind, x, y, bg, cellBg, dim: false)) return;
            }
            if (IsTiledFloor(x, y)) WriteTiled(text, 4 * x - 3, 2 * y, fg, cellBg, TileKind(x, y));
            else ColorHelper.WriteColored(text, fgColor: fg, bgColor: cellBg);
        }
    }

    // Terrain colors: open area - custom; dungeon floor - mixed with the color of the room below it.
    private static (List<int> fg, List<int> bg) TerrainColors(TerrainKind kind, List<int> cellBg) => kind.Indoor
        ? (ColorHelper.MixWith(cellBg, kind.Fg, kind.FgMix), kind.BgMix > 0 ? ColorHelper.MixWith(cellBg, kind.Bg, kind.BgMix) : cellBg)
        : (kind.Fg, cellBg);

    // ── Dungeon floor tiles ────────────────────── ─────────────────────── The background of the floor of the
    // rooms of the block location is a small checkerboard pattern: every 2 characters horizontally and each
    // line of the screen alternates a light/dark shade of the room color. The pattern goes entirely along the
    // cells and the dividers between them (not along the walls/doors) and is tied to the coordinates of the map
    // (X, Y - symbol/line in the “world” grid: cell c - symbols 4c-3..4c-1, line x - symbol 4x-2; cell line r -
    // 2r, lines y - 2y).
    private const double TileShade = 0.015;

    // Wooden floor of buildings - boards: the same checkerboard, but 1 character wide and more noticeable.
    private const double PlankShade = 0.01;

    // Tiles - in a man-made dungeon (dungeon), boards - in rooms of buildings/settlements (except for stone
    // floors); in a cave - without a pattern.
    private bool IsTiledFloor(int x, int y) => TileKind(x, y) > 0;

    // 0 - no pattern, 1 - boards (1 symbol), 2 - dungeon tile (2 symbols).
    private int TileKind(int x, int y)
    {
        if (!_roomPositions.Contains((x, y))) return 0;
        return _settings.Map.ChunkAt(x, y)?.Theme switch
        {
            "dungeon" => 2,
            MapChunk.Building or MapChunk.Village => _terrain.TryGetValue((x, y), out var k) && k.Code is 'q' or 'k' or 'p' ? 0 : 1,
            _ => 0,
        };
    }

    private void WriteTiled(string text, int x0, int y, List<int> fg, List<int> bg, int kind)
    {
        int shift = kind == 1 ? 0 : 1;
        double shade = kind == 1 ? PlankShade : TileShade;
        for (int i = 0; i < text.Length;)
        {
            bool light = ((((x0 + i) >> shift) + y) & 1) == 1;
            int j = i;
            while (j < text.Length && (((((x0 + j) >> shift) + y) & 1) == 1) == light) j++;
            var tileBg = ColorHelper.MixWith(bg, light ? [255, 255, 255] : [0, 0, 0], shade);
            ColorHelper.WriteColored(text[i..j], fgColor: fg, bgColor: tileBg);
            i = j;
        }
    }

    // Symbol of a stand/bench next to neighbors of the same type: in a row - a horizontal beam, in a column - a vertical one.
    private string? JoinedGlyph(TerrainKind kind, int x, int y)
    {
        if (kind.Join is not { } join) return null;
        if (HasRowNeighbour(x, y))
        {
            if (kind.RowGlyphs is not { } row) return new string(join[0], _display.CellWidth);
            bool left = SamePiece((x, y), (x - 1, y)), right = SamePiece((x, y), (x + 1, y));
            return left && right ? row[1] : right ? row[0] : row[2];
        }
        if (kind.RowGlyphs != null) return null; // table in a column - each cell has its own tabletop
        if (SamePiece((x, y), (x, y - 1)) || SamePiece((x, y), (x, y + 1))) return $" {join[1]} ";
        return null;
    }

    // ── Furniture ─────────────────────────────── ─────────────────────────────── Items (MapConfig.Furniture))
    // are placed on the floor with each update - the AI could move/break.
    private void ApplyFurniture()
    {
        foreach (var pos in _furnitureOf.Keys)
            if (_ground.TryGetValue(pos, out var g)) _terrain[pos] = g;
            else _terrain.Remove(pos);
        _furnitureOf.Clear();
        foreach (var f in _settings.Map.Furniture ?? [])
        {
            if (f.Deleted == true || FurnitureCatalog.Terrain(f) is not { } kind) continue;
            foreach (var p in f.Positions ?? [])
            {
                if (p.Count < 2) continue;
                _terrain[(p[0], p[1])] = kind;
                _furnitureOf[(p[0], p[1])] = f;
            }
        }
    }

    // ── View from the windows of the upper floor ──────────────────── ───────────────────── The floor lies on
    // the field separately from the first, with emptiness around it. Empty cells around are drawn according to
    // the map of the first floor under the building (the same block shift): grass, paths, trees, creatures on
    // the ground, neighboring houses - roofs. Visible only through the windows (the walls block the view), the
    // distance is the same as on the street; you can't go there.
    private void ApplyOutsideView(int heroX, int heroY)
    {
        foreach (var p in _projected) { _terrain.Remove(p); _areaColors.Remove(p); }
        _projected.Clear();
        var map = _settings.Map;
        if (map.ChunkAt(heroX, heroY) is not { Z: > 0, GroundX: int gx, GroundY: int gy } chunk) return;
        int dx = (gx - chunk.X) * MapChunk.Cols, dy = (gy - chunk.Y) * MapChunk.Rows;
        var roof = TerrainCatalog.Get('Z')!;
        int r = Math.Max(1, OutdoorVisionFt() / 5);
        var groundRooms = new HashSet<(int, int)>((map.Rooms ?? []).SelectMany(room => room.Positions ?? [])
            .Where(q => q.Count >= 2).Select(q => (q[0], q[1])));
        for (int x = Math.Max(1, heroX - r); x <= Math.Min(MapConfig.MaxSize, heroX + r); x++)
            for (int y = Math.Max(1, heroY - r); y <= Math.Min(MapConfig.MaxSize, heroY + r); y++)
            {
                var pos = (x, y);
                if (_roomPositions.Contains(pos) || _areaColors.ContainsKey(pos) || _terrain.ContainsKey(pos)) continue;
                if (map.ChunkAt(x, y) is { } other && other.Z != chunk.Z) continue;
                var g = (x + dx, y + dy);
                if (map.FloorAt(g.Item1, g.Item2) != 0) continue;
                var kind = groundRooms.Contains(g) ? roof : map.GroundAt(g.Item1, g.Item2) is { Indoor: false } k ? k : null;
                if (kind == null) continue;
                _terrain[pos] = kind;
                _areaColors[pos] = kind.Bg;
                _projected.Add(pos);
            }
        if (_projected.Count == 0) return;
        // Creatures and objects on the ground are in their places in the top view.
        foreach (var e in (map.Entities ?? []).Cast<CellEntity>().Concat(map.Objects ?? []))
        {
            if (e.Deleted == true || (e.Hidden == true && !_config.RevealMap) || e.Position is not { Count: >= 2 } ep) continue;
            var pos = (ep[0] - dx, ep[1] - dy);
            if (!_projected.Contains(pos)) continue;
            if (e is LivingEntity) _entities[pos] = e; else _objects.TryAdd(pos, e);
        }
    }

    // Two cells are parts of one object (drawn in one piece). Old maps without Furniture: next to the same type
    // with Join (the furniture lay with letters in terrain).
    private bool SamePiece((int x, int y) a, (int x, int y) b)
    {
        bool fa = _furnitureOf.TryGetValue(a, out var pa), fb = _furnitureOf.TryGetValue(b, out var pb);
        if (fa || fb) return fa && fb && pa == pb;
        return _terrain.TryGetValue(a, out var ka) && ka.Join != null && _terrain.TryGetValue(b, out var kb) && kb.Code == ka.Code;
    }

    private bool HasRowNeighbour(int x, int y) => SamePiece((x, y), (x - 1, y)) || SamePiece((x, y), (x + 1, y));

    // Item cell background: there is a pillow at the “head” (first cell) of the bed.
    private List<int> PieceBg(TerrainKind kind, int x, int y, List<int> cellBg, List<int> pieceBg)
    {
        if (!_furnitureOf.TryGetValue((x, y), out var f) || FurnitureCatalog.Get(f.Kind)?.HeadBg is not { } head) return pieceBg;
        if (f.Positions is not { Count: > 1 } ps || ps[0].Count < 2 || ps[0][0] != x || ps[0][1] != y) return pieceBg;
        return ColorHelper.MixWith(cellBg, head, Math.Max(kind.BgMix, 0.6));
    }

    // Bed for one cell: pillow - one symbol on the side of the wall, blanket - two.
    private bool TryWriteSingleBed(TerrainKind kind, int x, int y, List<int> baseBg, List<int> blanketBg, bool dim)
    {
        if (FurnitureCatalog.ByCode(kind.Code)?.HeadBg is not { } head) return false;
        bool single = _furnitureOf.TryGetValue((x, y), out var f)
            ? f.Positions?.Count == 1
            : !HasRowNeighbour(x, y) && !SamePiece((x, y), (x, y - 1)) && !SamePiece((x, y), (x, y + 1));
        if (!single) return false;
        var pillow = ColorHelper.MixWith(baseBg, head, Math.Max(kind.BgMix, 0.6));
        if (dim) pillow = ColorHelper.Darker(pillow, 0.5);
        int room = _roomIndex.GetValueOrDefault((x, y), -1);
        bool WallAt(int cx) => _roomIndex.GetValueOrDefault((cx, y), -2) != room;
        bool pillowRight = !WallAt(x - 1) && WallAt(x + 1);
        if (pillowRight)
        {
            ColorHelper.WriteColored(new string(' ', _display.CellWidth - 1), fgColor: blanketBg, bgColor: blanketBg);
            ColorHelper.WriteColored(" ", fgColor: pillow, bgColor: pillow);
        }
        else
        {
            ColorHelper.WriteColored(" ", fgColor: pillow, bgColor: pillow);
            ColorHelper.WriteColored(new string(' ', _display.CellWidth - 1), fgColor: blanketBg, bgColor: blanketBg);
        }
        return true;
    }

    // The corner of the grid inside a 2x2 object (all four cells are parts of it) is also solid.
    private bool TryWritePieceCorner(double x, double y, bool visible)
    {
        var a = ((int)(x - 0.5), (int)(y - 0.5));
        var cells = new[] { ((int)(x - 0.5), (int)(y + 0.5)), ((int)(x + 0.5), (int)(y - 0.5)), ((int)(x + 0.5), (int)(y + 0.5)) };
        if (!cells.All(c => SamePiece(a, c)) || !_terrain.TryGetValue(a, out var kind) || kind.Join is not { } join) return false;
        var (fg, pbg) = TerrainColors(kind, GetBackgroundForCorner(x, y));
        if (!visible) { fg = ColorHelper.Darker(fg, 0.5); pbg = ColorHelper.Darker(pbg, 0.5); }
        ColorHelper.WriteColored(join[0].ToString(), fgColor: fg, bgColor: pbg);
        return true;
    }

    // The cell terrain (for the legend and tooltips) is the same as what is drawn.
    public TerrainKind? TerrainAt(int x, int y) => _terrain.GetValueOrDefault((x, y));

    // Highlight select_target: selected (3) > area under cursor (2) > valid cells (1).
    private int TargetLevel(int x, int y)
    {
        var sel = _display.TargetSelection;
        if (sel == null) return 0;
        if (sel.Selected.Contains((x, y))) return 3;
        if (sel.Preview.Contains((x, y))) return 2;
        if (sel.Selectable.Contains((x, y))) return 1;
        return 0;
    }

    private static List<int> TintByLevel(List<int> bg, int level) => level switch
    {
        3 => ColorHelper.MixWith(bg, [220, 50, 50], 0.55),
        2 => ColorHelper.MixWith(bg, [230, 120, 40], 0.4),
        1 => ColorHelper.MixWith(bg, [210, 190, 90], 0.18),
        _ => bg
    };

    private List<int> ApplyTargetTint(int x, int y, List<int> bg) => TintByLevel(bg, TargetLevel(x, y));

    // Lines and angles between cells - according to the weakest illumination of neighboring cells: inside the
    // area they are painted along with the cells (the figure is solid, similar to a circle/cone), on the border
    // - not.
    private List<int> ApplyTargetTintBetween(List<int> bg, params (int x, int y)[] cells) =>
        _display.TargetSelection == null ? bg : TintByLevel(bg, cells.Min(c => FrameLevel(c.x, c.y)));

    // Corner - 3 highlighted cells out of 4 are enough: on the diagonal edge (cone, circle), the inner corners
    // of the “ladder” are painted over, and the edge looks smooth, not jagged.
    private List<int> ApplyTargetTintCorner(List<int> bg, params (int x, int y)[] cells) =>
        _display.TargetSelection == null ? bg
            : TintByLevel(bg, cells.Select(c => FrameLevel(c.x, c.y)).OrderDescending().ElementAt(2));

    // For the frame, the beginning of the figure (the hero at the cone/line) is “maximally highlighted”: the result is determined by the neighbor.
    private int FrameLevel(int x, int y) =>
        _display.TargetSelection?.Origin == (x, y) ? 3 : TargetLevel(x, y);

    // Cone/line exactly diagonally: the cell diagonal from the hero is highlighted more than both cells next to
    // it (when selected, those are usually just “valid”). The usual rules do not paint such a joint in the
    // color of the figure, and it “comes off” from the hero. Bridge: the corner between the hero and the cell
    // and its two separators (horizontal and vertical), converging at this corner. horizontal: null - angle,
    // true/false - horizontal/vertical line.
    private int OriginBridgeLevel(double x, double y, bool? horizontal)
    {
        if (_display.TargetSelection?.Origin is not var (ox, oy)) return 0;
        foreach (int sx in (int[])[-1, 1])
        foreach (int sy in (int[])[-1, 1])
        {
            int level = TargetLevel(ox + sx, oy + sy);
            if (level == 0 || TargetLevel(ox + sx, oy) >= level || TargetLevel(ox, oy + sy) >= level) continue;

            bool match = horizontal switch
            {
                null  => x == ox + sx * 0.5 && y == oy + sy * 0.5,
                true  => x == ox + sx && y == oy + sy * 0.5,
                false => x == ox + sx * 0.5 && y == oy + sy
            };
            if (match) return level;
        }
        return 0;
    }

    private (bool visible, bool explored) GetCornerState(double x, double y)
    {
        int xLeft = (int)(x - 0.5);
        int xRight = (int)(x + 0.5);
        int yBottom = (int)(y - 0.5);
        int yTop = (int)(y + 0.5);

        var cells = new[]
        {
            (xLeft, yBottom),
            (xLeft, yTop),
            (xRight, yBottom),
            (xRight, yTop)
        };

        return EdgeState(cells);
    }

    public void WriteCorner(double x, double y, CornerType type)
    {
        var (visible, explored) = GetCornerState(x, y);

        if (!visible && !explored)
        {
            ColorHelper.WriteColored(_display.Delimetr.ToString(), fgColor: MapBg, bgColor: MapBg);
            return;
        }

        if (TryWriteDoorPassageCorner(x, y, visible, explored))
            return;

        if (TryWritePieceCorner(x, y, visible))
            return;

        if (IsOpenEdge(((int)(x - 0.5), (int)(y - 0.5)), ((int)(x - 0.5), (int)(y + 0.5)),
                ((int)(x + 0.5), (int)(y - 0.5)), ((int)(x + 0.5), (int)(y + 0.5))))
        {
            ColorHelper.WriteColored(_display.Delimetr.ToString(), fgColor: MapBg, bgColor: MapBg);
            return;
        }

        var wallsFlags = new List<int>();

        void AddH(double hx, double hy, bool right) => wallsFlags.Add(GetWallValue(hx, hy, true, right || IsRightDoorOfHorizontalPair(hx, hy)));
        void AddV(double vx, double vy, bool down) => wallsFlags.Add(GetWallValue(vx, vy, false, down || IsBottomDoorOfVerticalPair(vx, vy)));

        switch (type)
        {
            case CornerType.TopLeft:
                wallsFlags.Add(0);
                AddH(x + 0.5, y, true);
                AddV(x, y - 0.5, true);
                wallsFlags.Add(0);
                break;

            case CornerType.TopRight:
                wallsFlags.Add(0);
                wallsFlags.Add(0);
                AddV(x, y - 0.5, true);
                AddH(x - 0.5, y, false);
                break;

            case CornerType.BottomLeft:
                AddV(x, y + 0.5, false);
                AddH(x + 0.5, y, true);
                wallsFlags.Add(0);
                wallsFlags.Add(0);
                break;

            case CornerType.BottomRight:
                AddV(x, y + 0.5, false);
                wallsFlags.Add(0);
                wallsFlags.Add(0);
                AddH(x - 0.5, y, false);
                break;

            case CornerType.TopMiddle:
                wallsFlags.Add(0);
                AddH(x + 0.5, y, true);
                AddV(x, y - 0.5, true);
                AddH(x - 0.5, y, false);
                break;

            case CornerType.BottomMiddle:
                AddV(x, y + 0.5, false);
                AddH(x + 0.5, y, true);
                wallsFlags.Add(0);
                AddH(x - 0.5, y, false);
                break;

            case CornerType.LeftMiddle:
                AddV(x, y + 0.5, false);
                AddH(x + 0.5, y, true);
                AddV(x, y - 0.5, true);
                wallsFlags.Add(0);
                break;

            case CornerType.RightMiddle:
                AddV(x, y + 0.5, false);
                wallsFlags.Add(0);
                AddV(x, y - 0.5, true);
                AddH(x - 0.5, y, false);
                break;

            case CornerType.Cross:
                AddV(x, y + 0.5, false);
                AddH(x + 0.5, y, true);
                AddV(x, y - 0.5, true);
                AddH(x - 0.5, y, false);
                break;
        }

        var wallsFlagsStr = string.Join(string.Empty, wallsFlags);

        IGridSymbolProvider provider;
        if (wallsFlags.All(x => x != 1))
            provider = _wallGridProvider;
        else if (wallsFlags.All(x => x != 2))
            provider = _gridProvider;
        else
        {
            if (GridHelper.HasAdjacentTwos(wallsFlags))
                wallsFlags = wallsFlags.Select(x => x == 1 ? 0 : x).ToList();

            if (wallsFlags.Count(x => x == 1) == 3)
                wallsFlags = GridHelper.Transform1112List(wallsFlags);

            wallsFlagsStr = string.Join(string.Empty, wallsFlags);
            provider = _wallGridProvider;
        }

        char symbol = provider.GetCorner(wallsFlagsStr);
        var bg = GetBackgroundForCorner(x, y);

        if (!visible && explored)
        {
            bg = ColorHelper.Darker(bg, 0.5);
        }

        int bridge = OriginBridgeLevel(x, y, null);
        bg = bridge > 0 ? TintByLevel(bg, bridge)
            : ApplyTargetTintCorner(bg,
                ((int)(x - 0.5), (int)(y - 0.5)), ((int)(x - 0.5), (int)(y + 0.5)),
                ((int)(x + 0.5), (int)(y - 0.5)), ((int)(x + 0.5), (int)(y + 0.5)));

        var fg = provider == _wallGridProvider
            ? ColorHelper.Darker(bg, 0.35)
            : ColorHelper.Darker(bg, symbol == '+' ? 0.92 : 0.7);

        bool tiled = provider == _gridProvider
            && IsTiledFloor((int)(x - 0.5), (int)(y - 0.5)) && IsTiledFloor((int)(x - 0.5), (int)(y + 0.5))
            && IsTiledFloor((int)(x + 0.5), (int)(y - 0.5)) && IsTiledFloor((int)(x + 0.5), (int)(y + 0.5));
        if (tiled) WriteTiled(symbol.ToString(), (int)(4 * x) - 2, (int)(2 * y), fg, bg, TileKind((int)(x - 0.5), (int)(y - 0.5)));
        else ColorHelper.WriteColored(symbol.ToString(), fgColor: fg, bgColor: bg);
    }

    // True when the horizontal door at (x, y) is the right member of a double-door pair.
    private bool IsRightDoorOfHorizontalPair(double x, double y)
    {
        var leftNeighbor = GetDoorAtWall(x - 1, y, true);
        return leftNeighbor != null
            && IsDoor(leftNeighbor) && leftNeighbor.IsWindow != true
            && (leftNeighbor.Hidden != true || _config.RevealMap)
            && !IsWallPresent(x - 0.5, y + 0.5, false)
            && !IsWallPresent(x - 0.5, y - 0.5, false);
    }

    // True when the vertical door at (x, y) is the bottom member of a double-door pair.
    private bool IsBottomDoorOfVerticalPair(double x, double y)
    {
        var topNeighbor = GetDoorAtWall(x, y + 1, false);
        return topNeighbor != null
            && IsDoor(topNeighbor) && topNeighbor.IsWindow != true
            && (topNeighbor.Hidden != true || _config.RevealMap)
            && !IsWallPresent(x - 0.5, y + 0.5, true)
            && !IsWallPresent(x + 0.5, y + 0.5, true);
    }

    // Returns true and writes the corner character when two adjacent doors on the same line
    // form a shared corridor with no separating wall between them on either side.
    private bool TryWriteDoorPassageCorner(double x, double y, bool visible, bool explored)
    {
        var bg = GetBackgroundForCorner(x, y);
        if (!visible && explored) bg = ColorHelper.Darker(bg, 0.5);

        // Two horizontal doors side by side: no vertical wall above or below the junction
        var leftDoor = GetDoorAtWall(x - 0.5, y, true);
        var rightDoor = GetDoorAtWall(x + 0.5, y, true);
        if (leftDoor != null && rightDoor != null
            && IsDoor(leftDoor) && IsDoor(rightDoor) && leftDoor.IsWindow != true && rightDoor.IsWindow != true
            && (leftDoor.Hidden != true || _config.RevealMap)
            && (rightDoor.Hidden != true || _config.RevealMap)
            && !IsWallPresent(x, y + 0.5, false)
            && !IsWallPresent(x, y - 0.5, false))
        {
            bool anyOpen = (leftDoor.IsDoorOpen ?? false) || (rightDoor.IsDoorOpen ?? false);
            if (anyOpen)
            {
                ColorHelper.WriteColored(_gridProvider.GetCorner("1111").ToString(),
                    fgColor: ColorHelper.Darker(bg), bgColor: bg);
            }
            else
            {
                ColorHelper.WriteColored("■", fgColor: GetDoorColor(leftDoor), bgColor: bg);
            }
            return true;
        }

        // Two vertical doors stacked: no horizontal wall to the left or right of the junction
        var bottomDoor = GetDoorAtWall(x, y - 0.5, false);
        var topDoor = GetDoorAtWall(x, y + 0.5, false);
        if (bottomDoor != null && topDoor != null
            && IsDoor(bottomDoor) && IsDoor(topDoor) && bottomDoor.IsWindow != true && topDoor.IsWindow != true
            && (bottomDoor.Hidden != true || _config.RevealMap)
            && (topDoor.Hidden != true || _config.RevealMap)
            && !IsWallPresent(x - 0.5, y, true)
            && !IsWallPresent(x + 0.5, y, true))
        {
            bool anyOpen = (bottomDoor.IsDoorOpen ?? false) || (topDoor.IsDoorOpen ?? false);
            if (anyOpen)
            {
                ColorHelper.WriteColored(_gridProvider.GetCorner("1111").ToString(),
                    fgColor: ColorHelper.Darker(bg), bgColor: bg);
            }
            else
            {
                ColorHelper.WriteColored("█", fgColor: GetDoorColor(bottomDoor), bgColor: bg);
            }
            return true;
        }

        return false;
    }

    // ----------------------------------------------------------------------------- Auxiliary Methods
    // -----------------------------------------------------------------------------

    private List<int> MapBg => _colors.MapBackground ?? _display.MainBackground;

    // Distance in feet according to the rule D&D: first diagonal = 5, all subsequent ones = 10. Formula:
    // straight steps * 5 + (diagonal > 0 ? 5 + (diagonal - 1) * 10 : 0)
    private static int GridDistanceFeet(int dx, int dy)
    {
        int adx = Math.Abs(dx), ady = Math.Abs(dy);
        int diag     = Math.Min(adx, ady);
        int straight = Math.Abs(adx - ady);
        return straight * 5 + (diag > 0 ? 5 + (diag - 1) * 10 : 0);
    }

    private bool IsValidCell(int x, int y) => x >= 1 && x <= (_settings.Map.Cols) && y >= 1 && y <= _settings.Map.Rows;

    // Out-of-bounds is considered visible so that the outer frame of the map is always rendered
    private bool IsCellVisible(int x, int y)
    {
        if (_config.RevealMap) return true;
        if (IsVoid(x, y)) return false;
        if (_heroRoomIndex < 0 && !_heroOnTerrain) return true;
        return _storage.VisibleCells.Contains((x, y));
    }

    // Which cells should be checked for visibility: the location field is up to 100x100, so not the entire
    // field, but the frame of the map contents (+1 on walls) ∩ radius (radius in feet ≥ 5 × distance in cells).
    private (int x0, int x1, int y0, int y1) ScanBox(int cx, int cy, int radiusFt)
    {
        int x0 = 1, x1 = _settings.Map.Cols, y0 = 1, y1 = _settings.Map.Rows;
        if (_contentBounds is { } b)
        {
            x0 = Math.Max(x0, b.minCol - 1); x1 = Math.Min(x1, b.maxCol + 1);
            y0 = Math.Max(y0, b.minRow - 1); y1 = Math.Min(y1, b.maxRow + 1);
        }
        if (radiusFt > 0)
        {
            int r = radiusFt / 5;
            x0 = Math.Max(x0, cx - r); x1 = Math.Min(x1, cx + r);
            y0 = Math.Max(y0, cy - r); y1 = Math.Min(y1, cy + r);
        }
        return (x0, x1, y0, y1);
    }

    private void ComputeHeroVisibility(int heroX, int heroY)
    {
        _storage.VisibleCells.Clear();
        _storage.VisibleCells.Add((heroX, heroY));

        int vision = _heroOnTerrain ? OutdoorVisionFt() : _settings.Hero?.VisionFt ?? -1;

        // vision == 0: the hero sees nothing (only his own cell)
        if (vision == 0)
        {
            _storage.ExploredCells.UnionWith(_storage.VisibleCells);
            return;
        }

        var (vx0, vx1, vy0, vy1) = ScanBox(heroX, heroY, vision);
        for (int x = vx0; x <= vx1; x++)
            for (int y = vy0; y <= vy1; y++)
            {
                if (x == heroX && y == heroY) continue;
                if (vision > 0 && GridDistanceFeet(x - heroX, y - heroY) > vision) continue;
                if (HasLineOfSight(heroX, heroY, x, y))
                    _storage.VisibleCells.Add((x, y));
            }

        // Remove intermediate cells added inside HasLineOfSight that are outside the radius
        if (vision > 0)
            _storage.VisibleCells.RemoveWhere(c => GridDistanceFeet(c.Item1 - heroX, c.Item2 - heroY) > vision);
        if (_projected.Count > 0)
        {
            int outdoor = OutdoorVisionFt();
            _storage.VisibleCells.RemoveWhere(c => _projected.Contains(c) && GridDistanceFeet(c.Item1 - heroX, c.Item2 - heroY) > outdoor);
        }

        // Light sources add cells beyond the hero's vision, but only those to which the hero has direct
        // visibility (LOS without taking into account range)
        AddLightSourcesVisibility(heroX, heroY);

        _storage.ExploredCells.UnionWith(_storage.VisibleCells);
    }

    // Open area: viewing range - depending on the light (time of day) and the hero’s vision. At night - dark
    // vision (or a couple of cells in moonlight); light sources are added separately.
    private int OutdoorVisionFt()
    {
        var hero = _settings.Hero;
        int light = _settings.Time.PartOfDay switch
        {
            PartsOfDay.Night => Math.Max(hero?.DarkvisionFt ?? 0, 10),
            PartsOfDay.Morning or PartsOfDay.Evening => 60,
            _ => 120,
        };
        return hero?.VisionFt is > 0 and var v ? Math.Min(v, light) : light;
    }

    private void AddLightSourcesVisibility(int heroX, int heroY)
    {
        var sources = _entities.Values.Concat(_objects.Values)
            .Where(e => e.Light is { On: true })
            .ToList();

        if (sources.Count == 0) return;

        // We collect the cells of each source into a separate set, then combine them. The general set cannot be
        // used with RemoveWhere - deleting one source along the radius erases cells illuminated by other
        // sources.
        var litCells = new HashSet<(int, int)>();
        foreach (var source in sources)
        {
            int lx = source.Position[0], ly = source.Position[1];
            int ft = source.Light!.Ft;

            var sourceLit = new HashSet<(int, int)>();
            var (lx0, lx1, ly0, ly1) = ScanBox(lx, ly, ft);
            for (int x = lx0; x <= lx1; x++)
                for (int y = ly0; y <= ly1; y++)
                {
                    if (ft > 0 && GridDistanceFeet(x - lx, y - ly) > ft) continue;
                    if (HasLineOfSightPure(lx, ly, x, y))
                        sourceLit.Add((x, y));
                }

            if (ft > 0)
                sourceLit.RemoveWhere(c => GridDistanceFeet(c.Item1 - lx, c.Item2 - ly) > ft);

            litCells.UnionWith(sourceLit);
        }

        // We add only clearly illuminated cells, to which the hero has LOS.. We use HasLineOfSightPure -
        // without side effects, so as not to add intermediate cells on the path that the torch does not
        // illuminate.
        foreach (var (cx, cy) in litCells)
        {
            if (_storage.VisibleCells.Contains((cx, cy))) continue;
            if (HasLineOfSightPure(heroX, heroY, cx, cy))
                _storage.VisibleCells.Add((cx, cy));
        }
    }

    // Clean check of LOS without side effects on VisibleCells. Used to calculate illumination from light
    // sources.
    private bool HasLineOfSightPure(int fromX, int fromY, int toX, int toY)
        => MovementCalculator.HasLineOfSight(_settings, fromX, fromY, toX, toY);

    // DDA raycasting: throws a beam from (fromX, fromY) to (toX, toY)), at each step checks the wall on the
    // intersected edge of the cell. IsWallPresent returns false for open doors, so visibility automatically
    // passes through passages to adjacent rooms.
    private bool HasLineOfSight(int fromX, int fromY, int toX, int toY)
    {
        double dx = toX - fromX;
        double dy = toY - fromY;

        int stepX = dx > 0 ? 1 : -1;
        int stepY = dy > 0 ? 1 : -1;

        int cx = fromX, cy = fromY;

        double tMaxX = dx == 0 ? double.MaxValue : (cx + 0.5 * stepX - fromX) / dx;
        double tMaxY = dy == 0 ? double.MaxValue : (cy + 0.5 * stepY - fromY) / dy;

        double tDeltaX = dx == 0 ? double.MaxValue : Math.Abs(1.0 / dx);
        double tDeltaY = dy == 0 ? double.MaxValue : Math.Abs(1.0 / dy);

        while (cx != toX || cy != toY)
        {
            if (Math.Abs(tMaxX - tMaxY) < 1e-9) // beam exactly to the corner between 4 cells
            {
                bool wallX = IsWallPresent(cx + 0.5 * stepX, cy, isHorizontal: false);
                bool wallY = IsWallPresent(cx, cy + 0.5 * stepY, isHorizontal: true);

                // Strict rule: if both walls from the current cell cover the corner - the beam is blocked
                if (wallX && wallY) return false;

                bool wallXY = IsWallPresent(cx + stepX, cy + 0.5 * stepY, isHorizontal: true);
                bool wallYX = IsWallPresent(cx + 0.5 * stepX, cy + stepY, isHorizontal: false);

                bool pathViaX = !wallX && !wallXY;
                bool pathViaY = !wallY && !wallYX;

                if (!pathViaX && !pathViaY) return false;

                // Adding cells through which the beam actually passed
                if (pathViaX) _storage.VisibleCells.Add((cx + stepX, cy));
                if (pathViaY) _storage.VisibleCells.Add((cx, cy + stepY));

                // Move to the target diagonal square
                if (cx != toX) cx += stepX;
                if (cy != toY) cy += stepY;
                _storage.VisibleCells.Add((cx, cy)); // add target cell

                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
            }
            else if (tMaxX < tMaxY)
            {
                if (IsWallPresent(cx + 0.5 * stepX, cy, isHorizontal: false))
                    return false;
                cx += stepX;
                _storage.VisibleCells.Add((cx, cy)); // cell after step by X
                tMaxX += tDeltaX;
            }
            else
            {
                if (IsWallPresent(cx, cy + 0.5 * stepY, isHorizontal: true))
                    return false;
                cy += stepY;
                _storage.VisibleCells.Add((cx, cy)); // cell after step by Y
                tMaxY += tDeltaY;
            }

            // Terrain that blocks the view (thicket, rocks): the cell itself is visible, but not behind it.
            if ((cx != toX || cy != toY) && _terrain.TryGetValue((cx, cy), out var kind) && kind.BlocksSight && !_projected.Contains((cx, cy)))
                return false;
        }

        return true;
    }

    // windowIsWall - for drawing corners: the window is in the wall, the lines are adjacent to it (for viewing, the window is an opening).
    private bool IsWallPresent(double x, double y, bool isHorizontal, bool checkExplored = false, bool checkDoor = false, bool windowIsWall = false)
    {
        // Determine two adjacent cells on both sides of the wall
        (int x1, int y1, int x2, int y2) cells;
        if (isHorizontal)
        {
            int xCoord = (int)x;
            int yLow = (int)(y - 0.5);
            int yHigh = (int)(y + 0.5);
            cells = (xCoord, yLow, xCoord, yHigh);
        }
        else
        {
            int xLeft = (int)(x - 0.5);
            int xRight = (int)(x + 0.5);
            int yCoord = (int)y;
            cells = (xLeft, yCoord, xRight, yCoord);
        }

        // If there is a door between these cells → no wall
        if (HasDoorBetween(cells.x1, cells.y1, cells.x2, cells.y2, checkDoor, windowIsWall))
            return false;

        return IsRoomEdge(x, y, isHorizontal, checkExplored);
    }

    private bool HasDoorBetween(int x1, int y1, int x2, int y2, bool checkDoor, bool windowIsWall = false)
    {
        if (!_doorsByEdge.TryGetValue(EdgeKey(x1, y1, x2, y2), out var doors)) return false;

        foreach (var door in doors)
        {
            {
                // Hidden = true → acts as solid wall regardless of door type
                if (door.Hidden == true) return false;
                if (windowIsWall && door.IsWindow == true) return false;
                // Passage (isDoor=false) → always open for LOS
                // Physical door → open for LOS only when IsDoorOpen=true
                return !IsDoor(door) || (!checkDoor && ((door.IsDoorOpen ?? false) || door.IsWindow == true));
            }
        }

        return false;
    }

    private Door? GetDoorAtWall(double x, double y, bool isHorizontal)
    {
        if (_doors == null || _doors.Count == 0) return null;

        (int x1, int y1, int x2, int y2) cells;
        if (isHorizontal)
        {
            int xCoord = (int)x;
            int yLow = (int)(y - 0.5);
            int yHigh = (int)(y + 0.5);
            cells = (xCoord, yLow, xCoord, yHigh);
        }
        else
        {
            int xLeft = (int)(x - 0.5);
            int xRight = (int)(x + 0.5);
            int yCoord = (int)y;
            cells = (xLeft, yCoord, xRight, yCoord);
        }

        return _doorsByEdge.TryGetValue(EdgeKey(cells.x1, cells.y1, cells.x2, cells.y2), out var doors) ? doors[0] : null;
    }

    /// Returns the door at this wall position if it is a hoverable door (IsDoor=true, Color set, not hidden).
    public Door? GetHoveredDoor(int col, int row, bool isHorizontal)
    {
        var door = isHorizontal
            ? GetDoorAtWall(col, row - 0.5, true)
            : GetDoorAtWall(col + 0.5, row, false);
        if (door == null) return null;
        if (!IsDoor(door)) return null;
        if (door.Color == null) return null;
        if (door.Hidden == true && !_config.RevealMap) return null;
        return door;
    }

    // IsDoor field is source of truth; falls back to Color check for backward compat with old saves. The window
    // is also a “door” (border with the state), but paired doors/openings do not touch it.
    private static bool IsDoor(Door door) => door.IsDoor ?? (door.Color?.Count == 3);

    private static readonly List<int> WindowColor = [150, 196, 226];

    private List<int> GetDoorColor(Door door) => door.Color ?? _display.Door;

    private bool IsRoomEdge(double x, double y, bool isHorizontal, bool checkExplored =  false)
    {
        (int x1, int y1, int x2, int y2) cells;
        if (isHorizontal)
        {
            int xCoord = (int)x;
            int yLow = (int)(y - 0.5);
            int yHigh = (int)(y + 0.5);
            cells = (xCoord, yLow, xCoord, yHigh);
        }
        else
        {
            int xLeft = (int)(x - 0.5);
            int xRight = (int)(x + 0.5);
            int yCoord = (int)y;
            cells = (xLeft, yCoord, xRight, yCoord);
        }

        if (!_config.RevealMap && checkExplored && !_storage.ExploredCells.Contains((cells.x1, cells.y1)) && !_storage.ExploredCells.Contains((cells.x2, cells.y2)))
        {
            return false;
        }

        bool isRoom1 = _roomPositions.Contains((cells.x1, cells.y1));
        bool isRoom2 = _roomPositions.Contains((cells.x2, cells.y2));

        // If one is in the room, the other is not → wall
        if (isRoom1 != isRoom2)
        {
            return true;
        }

        // If both are in rooms → check whether these are different rooms (by index, not by color)
        if (isRoom1 && isRoom2)
        {
            var idx1 = _roomIndex.GetValueOrDefault((cells.x1, cells.y1), -1);
            var idx2 = _roomIndex.GetValueOrDefault((cells.x2, cells.y2), -1);
            // Different rooms if indexes are different
            return idx1 != idx2 && idx1 >= 0 && idx2 >= 0;
        }

        return false;
    }

    private int GetWallValue(double x, double y, bool isHorizontal, bool checkDoor)
    {
        return IsWallPresent(x, y, isHorizontal, true, checkDoor, windowIsWall: true) ? 2 : 1;
    }

    private IGridSymbolProvider GetProviderForWall(double x, double y, bool isHorizontal, Door? door)
    {
        return IsWallPresent(x, y, isHorizontal) || (door != null && door.Hidden == true) ? _wallGridProvider : _gridProvider;
    }

    private List<int> DominantAreaColor(List<int> a, List<int> b)
    {
        int countA = _areaCellCounts.GetValueOrDefault((a[0], a[1], a[2]));
        int countB = _areaCellCounts.GetValueOrDefault((b[0], b[1], b[2]));
        return countA >= countB ? a : b;
    }

    private List<int> GetBackgroundForWall(double x, double y, bool isHorizontal)
    {
        (int x1, int y1, int x2, int y2) cells;
        if (isHorizontal)
        {
            int xCoord = (int)x;
            int yLow = (int)(y - 0.5);
            int yHigh = (int)(y + 0.5);
            cells = (xCoord, yLow, xCoord, yHigh);
        }
        else
        {
            int xLeft = (int)(x - 0.5);
            int xRight = (int)(x + 0.5);
            int yCoord = (int)y;
            cells = (xLeft, yCoord, xRight, yCoord);
        }

        var areaColor1 = IsValidCell(cells.x1, cells.y1) ? _areaColors.GetValueOrDefault((cells.x1, cells.y1)) : null;
        var areaColor2 = IsValidCell(cells.x2, cells.y2) ? _areaColors.GetValueOrDefault((cells.x2, cells.y2)) : null;

        if (!IsValidCell(cells.x1, cells.y1) && areaColor2 != null)
            return areaColor2;
        if (!IsValidCell(cells.x2, cells.y2) && areaColor1 != null)
            return areaColor1;

        if (areaColor1 != null && areaColor2 != null)
            return areaColor1.SequenceEqual(areaColor2) ? areaColor1 : DominantAreaColor(areaColor1, areaColor2);

        bool inRoom1 = IsValidCell(cells.x1, cells.y1) && _roomPositions.Contains((cells.x1, cells.y1));
        bool inRoom2 = IsValidCell(cells.x2, cells.y2) && _roomPositions.Contains((cells.x2, cells.y2));

        if (inRoom1)
            return _roomColors.GetValueOrDefault((cells.x1, cells.y1), MapBg);
        if (inRoom2)
            return _roomColors.GetValueOrDefault((cells.x2, cells.y2), MapBg);

        return MapBg;
    }

    private List<int> GetBackgroundForCorner(double x, double y)
    {
        int xLeft = (int)(x - 0.5);
        int xRight = (int)(x + 0.5);
        int yBottom = (int)(y - 0.5);
        int yTop = (int)(y + 0.5);

        var corners = new[] { (xLeft, yBottom), (xLeft, yTop), (xRight, yBottom), (xRight, yTop) };

        var areaCounts = new Dictionary<(int, int, int), (List<int> color, int localCount)>();
        List<int>? roomColor = null;
        bool areaOnRoom = false;

        foreach (var (cx, cy) in corners)
        {
            if (!IsValidCell(cx, cy)) continue;
            if (_areaColors.TryGetValue((cx, cy), out var ac))
            {
                var key = (ac[0], ac[1], ac[2]);
                areaCounts[key] = areaCounts.TryGetValue(key, out var existing)
                    ? (existing.color, existing.localCount + 1)
                    : (ac, 1);
            }
            if (_roomPositions.Contains((cx, cy)))
            {
                roomColor ??= _roomColors.GetValueOrDefault((cx, cy));
                if (_areaColors.ContainsKey((cx, cy))) areaOnRoom = true;
            }
        }

        // The corner of the room at the edge of the yard/area is the color of the room, as are the lines of the
        // walls nearby (otherwise the outer corner of the building would be painted grass and stick out from
        // the wall).
        if (roomColor != null && !areaOnRoom) return roomColor;

        // Clear majority (3 out of 4) is its color; otherwise, it is dominant on the map. Otherwise, a narrow
        // strip (a path among different types of grass) “outweighed” its neighbors, and the corners above/below
        // it were painted in its color.
        var best = areaCounts.Values
            .OrderByDescending(v => v.localCount >= 3)
            .ThenByDescending(v => _areaCellCounts.GetValueOrDefault((v.color[0], v.color[1], v.color[2])))
            .FirstOrDefault();

        int totalAreaCells = areaCounts.Values.Sum(v => v.localCount);
        if (totalAreaCells >= 3) return best.color;
        return roomColor ?? MapBg;
    }

    private List<int> GetBackgroundColor(int x, int y)
    {
        if (_areaColors.TryGetValue((x, y), out var areaColor))
            return areaColor;
        if (_roomPositions.Contains((x, y)))
            return _roomColors.GetValueOrDefault((x, y), MapBg);
        return MapBg;
    }
}
