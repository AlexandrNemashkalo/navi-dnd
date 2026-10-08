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
    private Dictionary<(int X, int Y), int> _roomIndex = [];  // позиция → индекс комнаты
    private int _heroRoomIndex = -1;

    private Dictionary<(int X, int Y), CellEntity> _objects = [];
    private Dictionary<(int X, int Y), CellEntity> _entities = [];
    private Dictionary<(int x, int y), List<int>> _areaColors = [];
    private Dictionary<(int X, int Y), List<int>> _roomColors = [];
    private Dictionary<(int R, int G, int B), int> _areaCellCounts = [];
    private readonly Dictionary<(int X, int Y), TerrainKind> _terrain = [];
    private readonly Dictionary<(int X, int Y), TerrainKind> _ground = [];        // рельеф/пол без мебели
    private readonly Dictionary<(int X, int Y), Furniture> _furnitureOf = [];   // клетка → предмет мебели
    private readonly HashSet<(int X, int Y)> _projected = [];                   // вид из окна верхнего этажа
    private bool _heroOnTerrain;
    private int _heroFloor;

    public MapObjectsProvider(WorldState settings, DisplayConfig display, Storage storage, AppConfig config = null)
    {
        _settings = settings;
        _display = display;
        _colors = settings.Map.Colors;
        _storage = storage;
        _config = config ?? new AppConfig();

        // Статичные данные: комнаты, зоны, двери — не меняются при локальном движении
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
        // Двери по стене (пара клеток) — обзор проверяет стену на каждом шаге каждого луча.
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

        // Рельеф открытой местности: фон клеток — как у зон (зона поверх рельефа важнее), линии сетки
        // между клетками берут тот же фон.
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
                    // Пол подземелья/здания — поверх цвета комнаты (лестница вне комнат — со своим фоном).
                    if ((kind.Indoor && _roomPositions.Contains(pos)) || _areaColors.ContainsKey(pos)) continue;
                    _areaColors[pos] = kind.Bg;
                    var key = (kind.Bg[0], kind.Bg[1], kind.Bg[2]);
                    _areaCellCounts[key] = _areaCellCounts.GetValueOrDefault(key) + 1;
                }
        }

        Refresh();
    }

    // Обновляет только динамическое состояние: сущности и видимость.
    // Вызывать перед каждой отрисовкой вместо создания нового экземпляра.
    public void Refresh()
    {
        _entities.Clear();
        _objects.Clear();

        foreach (var e in _settings.Map.Entities)
        {
            if (_config.RevealMap || e.Hidden != true)
                _entities[(e.Position[0], e.Position[1])] = e;
        }
        foreach (var o in _settings.Map.Objects)
        {
            if (_config.RevealMap || o.Hidden != true)
                _objects[(o.Position[0], o.Position[1])] = o;
        }
        // Без тактической карты (герой на карте мира) позиции может не быть — рисовать на местности нечего.
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

        _contentBounds = _settings.Map.ContentBounds();   // раз на кадр, а не на каждый проход обзора/источник света
        ApplyFurniture();
        ApplyOutsideView(heroPos.Item1, heroPos.Item2);
        ComputeHeroVisibility(heroPos.Item1, heroPos.Item2);
        RememberSeenObjects();
    }

    // ----------------------------------------------------------------------
    // Публичные методы для отрисовки
    // ----------------------------------------------------------------------

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

    // Видимость линии/угла между клетками: видна, если рядом видимая клетка, НЕ закрывающая обзор, — или
    // видны все соседние (граница внутри чащи/скалы). Иначе линия за деревом/скалой «просвечивала» бы:
    // само дерево видно, но то, что за ним, — нет. Так же для исследованности (туман). Мебель и колонны
    // внутри комнат (Indoor) стену за собой не прячут — стена комнаты видна всегда, когда видна клетка.
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

        // Край открытой местности у пустоты — не стена, а та же пустота (без почерневшей линии тумана).
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

        // Если есть дверь с цветом - рисуем символ двери
        if (door != null && door.IsWindow == true && door.Hidden != true)
        {
            // Окно: пунктир по линии стены, открытое — с проёмом посередине.
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
            // Стойка/скамья из нескольких клеток — сплошная: линия между ними рисуется её же брусом.
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
            // Разделитель внутри пола (не стена) — продолжение плитки.
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

    // Пустота блочной локации (вне комнат, зон и рельефа): никогда не «видна» и не «исследована» —
    // рисуется фоном экрана, а не почерневшей клеткой тумана. Стены/двери на её краю видны по соседней клетке.
    // Другие этажи (MapChunk.Z ≠ этажу героя) — тоже пустота: на экране только этаж героя.
    private bool IsVoid(int x, int y) =>
        _settings.Map.Chunks is { Count: > 0 } && !_projected.Contains((x, y))
        && ((!_roomPositions.Contains((x, y)) && !_areaColors.ContainsKey((x, y))) || _settings.Map.FloorAt(x, y) != _heroFloor);

    // Линия/угол на стыке открытой местности с пустотой (среди клеток есть пустота, комнат нет) — рисуется
    // пустотой. У комнат подземелья край — настоящая стена, его не трогаем.
    private bool IsOpenEdge(params (int x, int y)[] cells) =>
        cells.Any(c => IsVoid(c.x, c.y)) && !cells.Any(c => _roomPositions.Contains(c) && !IsVoid(c.x, c.y));
    private static string ObjectKey(CellEntity o) => Storage.ObjectKey(o);

    // Предметы, которые герой видит сейчас, — в память (Storage.SeenObjects); после загрузки сохранения — всё, что
    // лежит на уже изученных клетках.
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
            // вообще не видели
            ColorHelper.WriteColored(delimetrText, fgColor: MapBg, bgColor: MapBg);
            return;
        }

        var bg = GetBackgroundColor(x, y);

        if (!visible && explored)
        {
            // 👇 БЫЛО ВИДНО → рисуем "в тумане" (наведение подсвечивается и здесь — клетка изучена)
            var grayBg = ApplyTargetTint(x, y, ColorHelper.Darker(bg, 0.5));
            if (_display.HoveredCell == (x, y)) grayBg = ColorHelper.Pale(grayBg, 0.22);
            // Рельеф в тумане — тусклым рисунком (лес/скалы помнятся, существ не видно).
            string fogText = delimetrText;
            List<int> fogFg = grayBg, fogBg = grayBg;
            if (_terrain.TryGetValue((x, y), out var fogKind))
            {
                fogText = JoinedGlyph(fogKind, x, y) ?? TerrainCatalog.Glyph(fogKind, x, y);
                if (fogKind.Indoor)
                {
                    // Цвета считаем по исходному полу и тушим целиком — иначе яркая мебель в тумане не тускнела.
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
            // Предмет, который герой уже видел, помнится и в тумане — тусклым символом на фоне тумана. Существа — нет:
            // где они сейчас, герой не знает.
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

        // 👇 обычная видимая логика
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

    // Цвета рельефа: открытая местность — свои; пол подземелья — смешан с цветом комнаты под ним.
    private static (List<int> fg, List<int> bg) TerrainColors(TerrainKind kind, List<int> cellBg) => kind.Indoor
        ? (ColorHelper.MixWith(cellBg, kind.Fg, kind.FgMix), kind.BgMix > 0 ? ColorHelper.MixWith(cellBg, kind.Bg, kind.BgMix) : cellBg)
        : (kind.Fg, cellBg);

    // ── Плитка пола подземелья ─────────────────────────────────────────────
    // Фон пола комнат блочной локации — мелкая шахматка: каждые 2 символа по горизонтали и каждая строка
    // экрана чередуют светлый/тёмный оттенок цвета комнаты. Узор идёт сплошь по клеткам и разделителям
    // между ними (не по стенам/дверям) и привязан к координатам карты (X, Y — символ/строка в «мировой»
    // сетке: клетка c — символы 4c-3..4c-1, линия x — символ 4x-2; строка клетки r — 2r, линии y — 2y).
    private const double TileShade = 0.015;

    // Деревянный пол зданий — доски: та же шахматка, но шириной в 1 символ и заметнее.
    private const double PlankShade = 0.01;

    // Плитка — в рукотворном подземелье (dungeon), доски — в комнатах зданий/поселений (кроме каменного пола);
    // в пещере — без узора.
    private bool IsTiledFloor(int x, int y) => TileKind(x, y) > 0;

    // 0 — без узора, 1 — доски (1 символ), 2 — плитка подземелья (2 символа).
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

    // Символ стойки/скамьи по соседям того же вида: в ряд — горизонтальный брус, в столбик — вертикальный.
    private string? JoinedGlyph(TerrainKind kind, int x, int y)
    {
        if (kind.Join is not { } join) return null;
        if (HasRowNeighbour(x, y))
        {
            if (kind.RowGlyphs is not { } row) return new string(join[0], _display.CellWidth);
            bool left = SamePiece((x, y), (x - 1, y)), right = SamePiece((x, y), (x + 1, y));
            return left && right ? row[1] : right ? row[0] : row[2];
        }
        if (kind.RowGlyphs != null) return null; // стол столбиком — каждая клетка своей столешницей
        if (SamePiece((x, y), (x, y - 1)) || SamePiece((x, y), (x, y + 1))) return $" {join[1]} ";
        return null;
    }

    // ── Мебель ──────────────────────────────────────────────────────────────
    // Предметы (MapConfig.Furniture) накладываются на пол при каждом обновлении — ИИ мог сдвинуть/сломать.
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

    // ── Вид из окон верхнего этажа ─────────────────────────────────────────
    // Этаж лежит на поле отдельно от первого, вокруг него — пустота. Пустые клетки вокруг рисуются по карте
    // первого этажа под зданием (тот же сдвиг блока): трава, тропы, деревья, существа на земле, соседние дома —
    // крышами. Видны только через окна (стены режут обзор), дальность — как на улице; пройти туда нельзя.
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
        // Существа и предметы на земле — на своих местах в виде сверху.
        foreach (var e in (map.Entities ?? []).Cast<CellEntity>().Concat(map.Objects ?? []))
        {
            if (e.Deleted == true || (e.Hidden == true && !_config.RevealMap) || e.Position is not { Count: >= 2 } ep) continue;
            var pos = (ep[0] - dx, ep[1] - dy);
            if (!_projected.Contains(pos)) continue;
            if (e is LivingEntity) _entities[pos] = e; else _objects.TryAdd(pos, e);
        }
    }

    // Две клетки — части одного предмета (рисуются сплошь). Старые карты без Furniture: соседний тот же вид
    // с Join (мебель лежала буквами в рельефе).
    private bool SamePiece((int x, int y) a, (int x, int y) b)
    {
        bool fa = _furnitureOf.TryGetValue(a, out var pa), fb = _furnitureOf.TryGetValue(b, out var pb);
        if (fa || fb) return fa && fb && pa == pb;
        return _terrain.TryGetValue(a, out var ka) && ka.Join != null && _terrain.TryGetValue(b, out var kb) && kb.Code == ka.Code;
    }

    private bool HasRowNeighbour(int x, int y) => SamePiece((x, y), (x - 1, y)) || SamePiece((x, y), (x + 1, y));

    // Фон клетки предмета: у «головы» (первой клетки) кровати — подушка.
    private List<int> PieceBg(TerrainKind kind, int x, int y, List<int> cellBg, List<int> pieceBg)
    {
        if (!_furnitureOf.TryGetValue((x, y), out var f) || FurnitureCatalog.Get(f.Kind)?.HeadBg is not { } head) return pieceBg;
        if (f.Positions is not { Count: > 1 } ps || ps[0].Count < 2 || ps[0][0] != x || ps[0][1] != y) return pieceBg;
        return ColorHelper.MixWith(cellBg, head, Math.Max(kind.BgMix, 0.6));
    }

    // Кровать на одну клетку: подушка — один символ со стороны стены, одеяло — два.
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

    // Угол сетки внутри предмета 2×2 (все четыре клетки — его части) — тоже сплошь.
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

    // Рельеф клетки (для легенды и подсказок) — тот же, что рисуется.
    public TerrainKind? TerrainAt(int x, int y) => _terrain.GetValueOrDefault((x, y));

    // Подсветка select_target: выбранные (3) > область под курсором (2) > допустимые клетки (1).
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

    // Линии и углы между клетками — по самой слабой подсветке соседних клеток: внутри области
    // они красятся вместе с клетками (фигура сплошная, похожа на круг/конус), на границе — нет.
    private List<int> ApplyTargetTintBetween(List<int> bg, params (int x, int y)[] cells) =>
        _display.TargetSelection == null ? bg : TintByLevel(bg, cells.Min(c => FrameLevel(c.x, c.y)));

    // Угол — достаточно 3 подсвеченных клеток из 4: на диагональном краю (конус, круг) внутренние
    // углы «лесенки» закрашиваются, и край выглядит гладким, а не зубчатым.
    private List<int> ApplyTargetTintCorner(List<int> bg, params (int x, int y)[] cells) =>
        _display.TargetSelection == null ? bg
            : TintByLevel(bg, cells.Select(c => FrameLevel(c.x, c.y)).OrderDescending().ElementAt(2));

    // Для рамки начало фигуры (герой у конуса/линии) — «максимально подсвечено»: итог определяет сосед.
    private int FrameLevel(int x, int y) =>
        _display.TargetSelection?.Origin == (x, y) ? 3 : TargetLevel(x, y);

    // Конус/линия ровно по диагонали: диагональная от героя клетка подсвечена сильнее обеих клеток рядом с
    // ней (при выборе те обычно просто «допустимые»). Обычные правила такой стык не красят в цвет фигуры,
    // и она «отрывается» от героя. Мостик: угол
    // между героем и клеткой и два её разделителя (горизонтальный и вертикальный), сходящиеся в этом углу.
    // horizontal: null — угол, true/false — горизонтальная/вертикальная линия.
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
            : ColorHelper.Darker(bg);

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

    // ----------------------------------------------------------------------
    // Вспомогательные методы
    // ----------------------------------------------------------------------

    private List<int> MapBg => _colors.MapBackground ?? _display.MainBackground;

    // Дистанция в футах по правилу D&D: первая диагональ = 5, все последующие = 10.
    // Формула: прямые шаги * 5 + (диагональные > 0 ? 5 + (диагональные - 1) * 10 : 0)
    private static int GridDistanceFeet(int dx, int dy)
    {
        int adx = Math.Abs(dx), ady = Math.Abs(dy);
        int diag     = Math.Min(adx, ady);
        int straight = Math.Abs(adx - ady);
        return straight * 5 + (diag > 0 ? 5 + (diag - 1) * 10 : 0);
    }

    private bool IsValidCell(int x, int y) => x >= 1 && x <= (_settings.Map.Cols) && y >= 1 && y <= _settings.Map.Rows;

    // Out-of-bounds считается видимым, чтобы внешняя рамка карты всегда рендерилась
    private bool IsCellVisible(int x, int y)
    {
        if (_config.RevealMap) return true;
        if (IsVoid(x, y)) return false;
        if (_heroRoomIndex < 0 && !_heroOnTerrain) return true;
        return _storage.VisibleCells.Contains((x, y));
    }

    // Какие клетки вообще проверять на видимость: поле локации до 100×100, поэтому не всё поле, а
    // рамка содержимого карты (+1 на стены) ∩ радиус (радиус в футах ≥ 5 × расстояние в клетках).
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

        // vision == 0: герой ничего не видит (только своя клетка)
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

        // Убираем промежуточные клетки, добавленные внутри HasLineOfSight, которые вышли за радиус
        if (vision > 0)
            _storage.VisibleCells.RemoveWhere(c => GridDistanceFeet(c.Item1 - heroX, c.Item2 - heroY) > vision);
        if (_projected.Count > 0)
        {
            int outdoor = OutdoorVisionFt();
            _storage.VisibleCells.RemoveWhere(c => _projected.Contains(c) && GridDistanceFeet(c.Item1 - heroX, c.Item2 - heroY) > outdoor);
        }

        // Источники света добавляют клетки за пределами зрения героя,
        // но только те, до которых герой имеет прямую видимость (LOS без учёта дальности)
        AddLightSourcesVisibility(heroX, heroY);

        _storage.ExploredCells.UnionWith(_storage.VisibleCells);
    }

    // Открытая местность: дальность обзора — от света (время суток) и зрения героя. Ночью — тёмное
    // зрение (или пара клеток в лунном свете); источники света добавляются отдельно.
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

        // Собираем клетки каждого источника в отдельный набор, затем объединяем.
        // Общий набор нельзя использовать с RemoveWhere — удаление по радиусу одного источника
        // стирает клетки, освещённые другими источниками.
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

        // Добавляем только явно освещённые клетки, до которых герой имеет LOS.
        // Используем HasLineOfSightPure — без побочных эффектов, чтобы не добавлять
        // промежуточные клетки пути, которые факел не освещает.
        foreach (var (cx, cy) in litCells)
        {
            if (_storage.VisibleCells.Contains((cx, cy))) continue;
            if (HasLineOfSightPure(heroX, heroY, cx, cy))
                _storage.VisibleCells.Add((cx, cy));
        }
    }

    // Чистая проверка LOS без побочных эффектов на VisibleCells.
    // Используется для расчёта освещения от источников света.
    private bool HasLineOfSightPure(int fromX, int fromY, int toX, int toY)
        => MovementCalculator.HasLineOfSight(_settings, fromX, fromY, toX, toY);

    // DDA raycasting: бросает луч из (fromX, fromY) в (toX, toY),
    // на каждом шаге проверяет стену на пересекаемом ребре ячейки.
    // IsWallPresent возвращает false для открытых дверей, поэтому
    // видимость автоматически проходит через проходы в соседние комнаты.
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
            if (Math.Abs(tMaxX - tMaxY) < 1e-9) // луч точно в угол между 4 ячейками
            {
                bool wallX = IsWallPresent(cx + 0.5 * stepX, cy, isHorizontal: false);
                bool wallY = IsWallPresent(cx, cy + 0.5 * stepY, isHorizontal: true);

                // Строгое правило: если обе стены от текущей ячейки закрывают угол — луч заблокирован
                if (wallX && wallY) return false;

                bool wallXY = IsWallPresent(cx + stepX, cy + 0.5 * stepY, isHorizontal: true);
                bool wallYX = IsWallPresent(cx + 0.5 * stepX, cy + stepY, isHorizontal: false);

                bool pathViaX = !wallX && !wallXY;
                bool pathViaY = !wallY && !wallYX;

                if (!pathViaX && !pathViaY) return false;

                // Добавляем клетки, через которые реально прошёл луч
                if (pathViaX) _storage.VisibleCells.Add((cx + stepX, cy));
                if (pathViaY) _storage.VisibleCells.Add((cx, cy + stepY));

                // Переходим в целевую диагональную клетку
                if (cx != toX) cx += stepX;
                if (cy != toY) cy += stepY;
                _storage.VisibleCells.Add((cx, cy)); // добавляем целевую клетку

                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
            }
            else if (tMaxX < tMaxY)
            {
                if (IsWallPresent(cx + 0.5 * stepX, cy, isHorizontal: false))
                    return false;
                cx += stepX;
                _storage.VisibleCells.Add((cx, cy)); // клетка после шага по X
                tMaxX += tDeltaX;
            }
            else
            {
                if (IsWallPresent(cx, cy + 0.5 * stepY, isHorizontal: true))
                    return false;
                cy += stepY;
                _storage.VisibleCells.Add((cx, cy)); // клетка после шага по Y
                tMaxY += tDeltaY;
            }

            // Рельеф, закрывающий обзор (чаща, скалы): саму клетку видно, что за ней — нет.
            if ((cx != toX || cy != toY) && _terrain.TryGetValue((cx, cy), out var kind) && kind.BlocksSight && !_projected.Contains((cx, cy)))
                return false;
        }

        return true;
    }

    // windowIsWall — для рисования углов: окно стоит в стене, линии к нему примыкают (для обзора окно — проём).
    private bool IsWallPresent(double x, double y, bool isHorizontal, bool checkExplored = false, bool checkDoor = false, bool windowIsWall = false)
    {
        // Определяем две соседние клетки по обе стороны стены
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

        // Если есть дверь между этими клетками → нет стены
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

    // IsDoor field is source of truth; falls back to Color check for backward compat with old saves.
    // Окно — тоже «дверь» (граница с состоянием), но парные двери/проёмы его не касаются.
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

        // Если один в комнате, другой нет → стена
        if (isRoom1 != isRoom2)
        {
            return true;
        }

        // Если оба в комнатах → проверяем разные ли это комнаты (по индексу, не по цвету)
        if (isRoom1 && isRoom2)
        {
            var idx1 = _roomIndex.GetValueOrDefault((cells.x1, cells.y1), -1);
            var idx2 = _roomIndex.GetValueOrDefault((cells.x2, cells.y2), -1);
            // Разные комнаты, если индексы разные
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

        // Угол комнаты на краю двора/местности — цвета комнаты, как и линии стен рядом (иначе внешний угол
        // здания красился в траву и выбивался из стены).
        if (roomColor != null && !areaOnRoom) return roomColor;

        // Явное большинство (3 из 4) — его цвет; иначе — преобладающий на карте. Иначе узкая полоса (тропа
        // среди разных видов травы) «перевешивала» соседей, и углы над/под ней красились в её цвет.
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
