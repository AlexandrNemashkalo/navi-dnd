namespace NaviDnD.Data.Models;

public class MapConfig
{
    private int _cols = 20;
    private int _rows = 15;

    // Карта может быть больше экрана (камера, DisplayConfig.CameraCol/Row) — верхняя граница только
    // против явного мусора от ИИ, а не размер экрана.
    public int Cols
    {
        get => _cols;
        set => _cols = Math.Clamp(value, 1, MaxSize);
    }

    public int Rows
    {
        get => _rows;
        set => _rows = Math.Clamp(value, 1, MaxSize);
    }

    // Локация не больше 100×100 клеток — дальше камеру тянуть нельзя (DisplayConfig.CameraCol/Row
    // ограничены размером карты, MapDisplay.ClampCamera).
    public const int MaxSize = 100;

    public ColorSetting Colors { get; set; } = new ColorSetting();
    public List<LivingEntity>? Entities { get; set; } = [];
    public List<CellEntity>? Objects { get; set; } = [];
    public List<Room>? Rooms { get; set; } = [];
    public List<Door>? Doors { get; set; } = [];
    public List<Area>? Area { get; set; } = [];
    public List<TriggerCell>? TriggerCells { get; set; } = [];
    public string? DefaultImage { get; set; }

    // Блоки локации (генерируются кодом, растут по мере исследования). null — карта старого формата:
    // одна карта без продолжения (сохранения до блочной генерации).
    public List<MapChunk>? Chunks { get; set; }

    // Мебель на одной или нескольких клетках (BuildingGenerator; ИИ патчит по id). Физика клетки — TerrainAt.
    public List<Furniture>? Furniture { get; set; }

    // Предмет мебели на клетке (не удалённый), или null. Кэш перестраивается, когда меняется список/клетки.
    public Furniture? FurnitureAt(int col, int row)
    {
        if (Furniture is not { Count: > 0 } list) return null;
        int stamp = Stamp(list.Count, list[0].Positions, list[^1].Positions, list[0].Deleted == true, list[^1].Deleted == true);
        var cache = _furnitureByCell;
        if (cache == null || !ReferenceEquals(_furnitureCacheFor, list) || _furnitureStamp != stamp)
        {
            cache = [];   // строится в локальную — кэш читают и из потока музыки (MusicDirector)
            foreach (var f in list)
                if (f.Deleted != true)
                    foreach (var p in f.Positions ?? [])
                        if (p.Count >= 2) cache[(p[0], p[1])] = f;
            _furnitureByCell = cache;
            _furnitureCacheFor = list;
            _furnitureStamp = stamp;
        }
        return cache.GetValueOrDefault((col, row));
    }

    private Dictionary<(int, int), Furniture>? _furnitureByCell;
    private List<Furniture>? _furnitureCacheFor;
    private int _furnitureStamp;

    // Лестницы между этажами (LocationGrower): шаг на клетку A переносит героя на B и обратно.
    // Служебные данные движка — ИИ их не патчит (в отличие от entities/objects, которые он заменяет).
    public List<StairLink>? Stairs { get; set; }

    // Клетка, куда ведёт лестница с (col, row), или null.
    public (int col, int row)? StairTarget(int col, int row)
    {
        foreach (var s in Stairs ?? [])
        {
            if (s.A is not { Count: >= 2 } a || s.B is not { Count: >= 2 } b) continue;
            if (a[0] == col && a[1] == row) return (b[0], b[1]);
            if (b[0] == col && b[1] == row) return (a[0], a[1]);
        }
        return null;
    }

    // Этаж клетки (по её блоку); вне блоков — 0.
    public int FloorAt(int col, int row) => ChunkAt(col, row)?.Z ?? 0;

    // В локации есть этажи (здания, подвалы).
    public bool HasFloors => Chunks?.Any(c => c.Z != 0) == true;

    // «1 этаж», «2 этаж», «подвал», «подвал 2».
    public static string FloorName(int z) => z >= 0 ? $"{z + 1} этаж" : z == -1 ? "подвал" : $"подвал {-z}";

    // Сдвиг всего содержимого карты: комнаты, двери, зоны, существа, объекты, клетки-триггеры.
    public void Translate(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        void Shift(List<int>? p) { if (p is { Count: >= 2 }) { p[0] += dx; p[1] += dy; } }
        foreach (var room in Rooms ?? []) foreach (var p in room.Positions ?? []) Shift(p);
        foreach (var area in Area ?? []) foreach (var p in area.Positions ?? []) Shift(p);
        foreach (var door in Doors ?? []) { Shift(door.From); Shift(door.To); }
        foreach (var e in Entities ?? []) Shift(e.Position);
        foreach (var o in Objects ?? []) Shift(o.Position);
        foreach (var t in TriggerCells ?? []) Shift(t.Position);
        foreach (var f in Furniture ?? []) foreach (var p in f.Positions ?? []) Shift(p);
    }

    // Блок локации, в который попадает клетка, или null.
    public MapChunk? ChunkAt(int col, int row)
    {
        if (Chunks is not { Count: > 0 } chunks || col < 1 || row < 1) return null;
        if (_chunkByGrid == null || !ReferenceEquals(_chunkCacheFor, chunks) || _chunkCacheCount != chunks.Count)
        {
            var grid = new Dictionary<(int, int), MapChunk>();   // в локальную — кэш читают и из потока музыки
            foreach (var c in chunks) grid[(c.X, c.Y)] = c;
            _chunkByGrid = grid;
            _chunkCacheFor = chunks;
            _chunkCacheCount = chunks.Count;
        }
        return _chunkByGrid.GetValueOrDefault(((col - 1) / MapChunk.Cols, (row - 1) / MapChunk.Rows));
    }

    // Рельеф клетки (открытая местность, пол подземелья) или null — пустота/без рельефа. Мебель на клетке
    // перекрывает пол: её вид и задаёт физику (проходимость, обзор, укрытие).
    public TerrainKind? TerrainAt(int col, int row)
    {
        if (FurnitureAt(col, row) is { } furniture && FurnitureCatalog.Terrain(furniture) is { } fk) return fk;
        return GroundAt(col, row);
    }

    // Пол/рельеф клетки без мебели.
    public TerrainKind? GroundAt(int col, int row)
    {
        if (ChunkAt(col, row) is not { Terrain: { Count: > 0 } t } chunk) return null;
        int r = row - chunk.OriginRow - 1, cc = col - chunk.OriginCol - 1;
        if (r >= t.Count || cc >= t[r].Length) return null;
        return TerrainCatalog.Get(t[r][cc]);
    }

    // ── Индексы клеток ─────────────────────────────────────────────────────────────────────────────────
    // Путь (Дейкстра по всей локации), обзор и движение спрашивают «чья клетка / есть ли проход» тысячи раз
    // за кадр — перебор всех клеток всех комнат на каждый вопрос тормозил тактическую карту. Индексы
    // строятся один раз и пересобираются, когда список заменили (патч ИИ, рост локации) или сдвинули клетки
    // (ShiftAll): отметка — число элементов и клетки первого/последнего.

    // Комната клетки (первая по списку, как прежний перебор) или null.
    public Room? RoomAt(int col, int row)
    {
        if (Rooms is not { Count: > 0 } list) return null;
        int stamp = Stamp(list.Count, list[0].Positions, list[^1].Positions);
        var cache = _roomByCell;
        if (cache == null || !ReferenceEquals(_roomCacheFor, list) || _roomStamp != stamp)
        {
            cache = [];
            foreach (var r in list)
                foreach (var p in r.Positions ?? [])
                    if (p.Count >= 2) cache.TryAdd((p[0], p[1]), r);
            _roomByCell = cache;
            _roomCacheFor = list;
            _roomStamp = stamp;
        }
        return cache.GetValueOrDefault((col, row));
    }

    // Зоны клетки по порядку списка (удалённые — тоже: фильтр у вызывающего); пусто — нет зон.
    public IReadOnlyList<Area> AreasAt(int col, int row)
    {
        if (Area is not { Count: > 0 } list) return [];
        int stamp = Stamp(list.Count, list[0].Positions, list[^1].Positions, list[0].Deleted == true, list[^1].Deleted == true);
        var cache = _areasByCell;
        if (cache == null || !ReferenceEquals(_areaCacheFor, list) || _areaStamp != stamp)
        {
            cache = [];
            foreach (var a in list)
                foreach (var p in a.Positions ?? [])
                    if (p.Count >= 2)
                    {
                        if (!cache.TryGetValue((p[0], p[1]), out var at)) cache[(p[0], p[1])] = at = [];
                        if (!at.Contains(a)) at.Add(a);
                    }
            _areasByCell = cache;
            _areaCacheFor = list;
            _areaStamp = stamp;
        }
        return cache.TryGetValue((col, row), out var areas) ? areas : [];
    }

    // Двери/проходы между двумя соседними клетками (в любую сторону) по порядку списка; пусто — нет.
    public IReadOnlyList<Door> DoorsBetween(int x1, int y1, int x2, int y2)
    {
        if (Doors is not { Count: > 0 } list) return [];
        var f0 = list[0].From; var fl = list[^1].From;
        int stamp = Stamp(list.Count, f0 == null ? null : [f0], fl == null ? null : [fl]);
        var cache = _doorsByEdge;
        if (cache == null || !ReferenceEquals(_doorCacheFor, list) || _doorStamp != stamp)
        {
            cache = [];
            foreach (var d in list)
                if (d.From is { Count: >= 2 } f && d.To is { Count: >= 2 } t)
                {
                    var key = EdgeKey(f[0], f[1], t[0], t[1]);
                    if (!cache.TryGetValue(key, out var at)) cache[key] = at = [];
                    at.Add(d);
                }
            _doorsByEdge = cache;
            _doorCacheFor = list;
            _doorStamp = stamp;
        }
        return cache.TryGetValue(EdgeKey(x1, y1, x2, y2), out var doors) ? doors : [];
    }

    private static (int, int, int, int) EdgeKey(int x1, int y1, int x2, int y2) =>
        x1 < x2 || (x1 == x2 && y1 <= y2) ? (x1, y1, x2, y2) : (x2, y2, x1, y1);

    private static int Stamp(int count, List<List<int>>? first, List<List<int>>? last, bool firstDeleted = false, bool lastDeleted = false)
    {
        static int Cells(List<List<int>>? ps) =>
            ps is { Count: > 0 } && ps[0] is { Count: >= 2 } p ? unchecked(ps.Count * 7 + p[0] * 131 + p[1] * 7919) : 0;
        return unchecked(((count * 31 + Cells(first)) * 31 + Cells(last)) * 4 + (firstDeleted ? 1 : 0) + (lastDeleted ? 2 : 0));
    }

    private Dictionary<(int, int), Room>? _roomByCell;
    private List<Room>? _roomCacheFor;
    private int _roomStamp;
    private Dictionary<(int, int), List<Area>>? _areasByCell;
    private List<Area>? _areaCacheFor;
    private int _areaStamp;
    private Dictionary<(int, int, int, int), List<Door>>? _doorsByEdge;
    private List<Door>? _doorCacheFor;
    private int _doorStamp;

    private Dictionary<(int, int), MapChunk>? _chunkByGrid;
    private List<MapChunk>? _chunkCacheFor;
    private int _chunkCacheCount;

    // Рамка, где на карте хоть что-то есть (комнаты, зоны, рельеф, существа, объекты); null — карта пуста.
    // Видимость считается только внутри неё, а не по всему полю MaxSize×MaxSize.
    public (int minCol, int maxCol, int minRow, int maxRow)? ContentBounds()
    {
        var terrainCorners = (Chunks ?? []).Where(c => c.Terrain != null).SelectMany(c => new List<List<int>>
        {
            new() { c.OriginCol + 1, c.OriginRow + 1 },
            new() { c.OriginCol + MapChunk.Cols, c.OriginRow + MapChunk.Rows },
        });
        var cells = (Rooms ?? []).SelectMany(r => r.Positions ?? [])
            .Concat((Area ?? []).SelectMany(a => a.Positions ?? []))
            .Concat((Entities ?? []).Select(e => e.Position))
            .Concat((Objects ?? []).Select(o => o.Position))
            .Concat(terrainCorners)
            .Where(p => p is { Count: >= 2 })
            .ToList();
        if (cells.Count == 0) return null;
        return (cells.Min(p => p[0]), cells.Max(p => p[0]), cells.Min(p => p[1]), cells.Max(p => p[1]));
    }

    // Новая карта (генератор рисует её от (1,1)) ставится в центр поля локации MaxSize×MaxSize —
    // вокруг остаётся место во все стороны (камера тянется, позже — пристройка блоков).
    // Возвращает сдвиг — им же надо сдвинуть всё, что лежит вне MapConfig (герой, исследованные клетки).
    public (int dx, int dy) CenterInField()
    {
        Cols = MaxSize;
        Rows = MaxSize;
        if (ContentBounds() is not { } b) return (0, 0);
        int dx = (MaxSize - (b.maxCol - b.minCol + 1)) / 2 + 1 - b.minCol;
        int dy = (MaxSize - (b.maxRow - b.minRow + 1)) / 2 + 1 - b.minRow;
        Translate(dx, dy);
        return (dx, dy);
    }
}

// Лестница: две клетки на соседних этажах (A — нижняя, B — верхняя).
public class StairLink
{
    public List<int> A { get; set; } = [];
    public List<int> B { get; set; } = [];
}

public class ColorSetting
{
    public List<int>? MapBackground { get; set; } = [0, 0, 0];
    public List<int> MapForeground { get; set; } = [255, 255, 255];
}
