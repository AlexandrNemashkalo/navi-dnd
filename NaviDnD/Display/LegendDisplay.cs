using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;
using System.Linq;
using System.Runtime.CompilerServices;

namespace NaviDnD.Display;

public class LegendDisplay
{
    private readonly Storage _storage;
    private readonly DisplayConfig _display;
    private readonly AppConfig _config;

    public LegendDisplay(Storage storage, DisplayConfig display, AppConfig config)
    {
        _storage = storage;
        _display = display;
        _config = config;
    }

    // Separator index: totalRows - CombatReservedLines - 1, where totalRows = rows*2 + 3.
    // На экране Мира блока боя нет — и разделителя над ним тоже (-1).
    public int SeparatorLineIndex => _display.MapLevel == MapLevel.World ? -1
        : _display.ViewRows(_storage.WorldState.Map) * 2 + 2 - _display.CombatReservedLines;

    /// <summary>
    /// Builds legend lines for side-panel display.
    /// Each Action writes the legend content without borders or newlines.
    /// </summary>
    // Элемент легенды: его строки, приоритет (при переполнении первыми уходят низкие) и «закреплён»
    // (под курсором/выбран — не убирается никогда).
    private sealed record LegendItem(List<Action> Lines, int Priority, bool Pinned);

    private const int PriorityKeep = 9, PriorityHero = 5, PriorityEntity = 4, PriorityRoom = 3, PriorityArea = 2, PriorityTerrain = 1;

    // Легенда текущей локации: «Место», герой, существа/объекты, комнаты/зоны — список с прокруткой (колесо мыши над
    // легендой, полоса справа ┃│ — как у описания мира), рельеф — блоком внизу, к блоку боя. Рельефу места мало
    // (меньше 4 строк списку) — он уступает списку. Выбранное (Tab/наведение) прокручивается в окно само.
    private int _lastLegendSelected = int.MinValue;

    private void AddLocationLegendLines(List<Action> lines, WorldState settings, int legendWidth)
    {
        var items = new List<LegendItem>();
        BuildLocationLegendItems(items, settings, legendWidth - 1);   // правый столбец — под полосу прокрутки

        int capacity = SeparatorLineIndex;
        var bottom = items.Where(i => i.Priority == PriorityTerrain).SelectMany(i => i.Lines).ToList();
        if (bottom.Count <= 1) bottom.Clear(); // остался только разделитель без строк рельефа
        var top = items.Where(i => i.Priority != PriorityTerrain).ToList();
        // «Место» и герой (с пустой строкой после него) — закреплены сверху, не прокручиваются.
        int fixedCount = top.TakeWhile(i => i.Priority >= PriorityHero).Count();
        var head = top.Take(fixedCount).SelectMany(i => i.Lines).ToList();
        top = top.Skip(fixedCount).ToList();
        var topLines = top.SelectMany(i => i.Lines).ToList();
        int topCap = capacity - bottom.Count - head.Count;
        if (bottom.Count > 0 && topLines.Count > topCap && topCap < 4) { bottom.Clear(); topCap = capacity - head.Count; }
        topCap = Math.Max(1, topCap);
        lines.AddRange(head);

        // Выбор сменился — выбранная запись (не «Место»/герой: они всегда сверху) в окно прокрутки.
        if (_display.SelectedMapItemIndex != _lastLegendSelected)
        {
            _lastLegendSelected = _display.SelectedMapItemIndex;
            int at = 0;
            foreach (var item in top)
            {
                if (item.Pinned && item.Priority < PriorityHero)   // выбранное существо/комната/зона, не разделитель
                {
                    if (at < _display.LegendScroll) _display.LegendScroll = at;
                    else if (at + item.Lines.Count > _display.LegendScroll + topCap) _display.LegendScroll = at + item.Lines.Count - topCap;
                    break;
                }
                at += item.Lines.Count;
            }
        }
        int scroll = _display.LegendScroll = Math.Clamp(_display.LegendScroll, 0, Math.Max(0, topLines.Count - topCap));
        var bar = TextArea.Bar(topCap, topLines.Count, scroll);
        var frame = MouseUiHelper.FrameColor(_display);
        var thumb = ColorHelper.Pale(_display.MainForeground, 0.3);
        for (int i = 0; i < topCap && scroll + i < topLines.Count; i++)
        {
            var line = topLines[scroll + i];
            bool? on = bar?[i];
            lines.Add(() =>
            {
                int start = Console.CursorLeft;
                line();
                if (on is not { } isThumb) return;
                // Полоса — в последнем столбце легенды (снимок CaptureOutput пишет в строку — курсор не движется).
                if (Console.Out is not StringWriter)
                {
                    int used = Console.CursorLeft - start;
                    if (used < legendWidth - 1) Console.Write(new string(' ', legendWidth - 1 - used));
                }
                ColorHelper.WriteColored(isThumb ? "┃" : "│", isThumb ? thumb : frame, _display.MainBackground);
            });
        }
        // Вертикальная линия колонок рельефа упирается в сплошные разделители — без стыков ┬/┴ (SeparatorJunction = null).
        while (lines.Count + bottom.Count < capacity)
            lines.Add(() => Console.Write(new string(' ', legendWidth)));
        if (bottom.Count > 0 && lines.Count < capacity) TerrainSeparatorIndex = lines.Count; // первая строка блока
        lines.AddRange(bottom);
        if (lines.Count > capacity) lines.RemoveRange(capacity, lines.Count - capacity);
    }

    // Карта мира: (мир, видимые места по приоритету) — даёт MapDisplay (камера мира — его).
    public Func<(WorldMap world, List<int> places)>? WorldLegend { get; set; }

    private void AddWorldLegendLines(List<Action> lines, int legendWidth)
    {
        if (WorldLegend == null) return;
        var (world, places) = WorldLegend();
        var dim = new List<int> { 110, 115, 125 };
        var bg = _display.MainBackground;
        int capacity = _display.ViewRows(_storage.WorldState.Map) * 2 + 3;
        void Line(string text, List<int> color) =>
            lines.Add(() => ColorHelper.WriteColored((" " + text).PadRight(legendWidth)[..legendWidth], color, bg));

        // Фильтр в две строки: «■ Все [F6]  ■ Поселения [F7]» / «■ Приключения [F8]  ■ Природа [F9]»
        // (■ ярко — показано, тускло — скрыто; подсказка клавиши — после названия), затем пустая строка.
        int half = legendWidth / 2;
        bool all = WorldMapView.Categories.All(c => _display.WorldFilter.Contains(c.key));
        void Toggle(string label, string fkey, bool on, int width)
        {
            Console.Write(" ");
            ColorHelper.WriteColored("■ ", on ? ColorHelper.Pale(_display.MainForeground, 0.5) : ColorHelper.Darker(_display.MainForeground, 0.4), bg);
            ColorHelper.WriteColored(label + " ", on ? _display.MainForeground : dim, bg);
            MouseUiHelper.WriteKeyHint($"[{fkey}]", _display);
            int rest = width - 1 - 2 - label.Length - 1 - fkey.Length - 2;
            if (rest > 0) Console.Write(new string(' ', rest));
        }
        var cats = WorldMapView.Categories;
        lines.Add(() =>
        {
            Toggle(L.T("Все"), "F6", all, half);
            Toggle(cats[0].label, cats[0].fkey, _display.WorldFilter.Contains(cats[0].key), legendWidth - half);
        });
        lines.Add(() =>
        {
            Toggle(cats[1].label, cats[1].fkey, _display.WorldFilter.Contains(cats[1].key), half);
            Toggle(cats[2].label, cats[2].fkey, _display.WorldFilter.Contains(cats[2].key), legendWidth - half);
        });
        lines.Add(() => Console.Write(new string(' ', legendWidth)));
        // Где герой: место (или рельеф) и чья земля.
        var ws = _storage.WorldState;
        if (GameWorld.HeroTile(ws) is { } ht)
        {
            string here = ws.World?.Place is { Length: > 0 } pl ? pl : L.T(WorldBiomes.Get(world.BiomeAt(ht.x, ht.y)).Name).ToLowerInvariant();
            int own = world.OwnerAt(ht.x, ht.y);
            string land = own >= 0 && own < world.Kingdoms.Count ? world.Kingdoms[own].Name : L.T("ничьи земли");
            lines.Add(() =>
            {
                Console.Write(" ");
                // Тот же значок, что у героя на карте мира.
                ColorHelper.WriteColored(WorldMapView.MarkerGlyph.ToString(), WorldMapView.MarkerColor, bg);
                string t = " " + L.F("Вы: {0} · {1}", here, land);
                t = t.Length > legendWidth - 2 ? t[..(legendWidth - 3)] + "…" : t.PadRight(legendWidth - 2);
                ColorHelper.WriteColored(t, _display.MainForeground, bg);
            });
        }
        foreach (var k in world.Kingdoms)
            lines.Add(() =>
            {
                Console.Write(" ");
                ColorHelper.WriteColored("██", k.Color, bg);
                ColorHelper.WriteColored((" " + k.Name).PadRight(legendWidth - 3)[..(legendWidth - 3)], _display.MainForeground, bg);
            });
        lines.Add(() => Console.Write(new string(' ', legendWidth)));

        // Список мест в окне — в две колонки « ● Название │ ○ Название », по строкам; выбранное (Tab) всегда в
        // видимой части списка.
        int room = capacity - lines.Count - 3;
        int fit = Math.Max(0, room) * 2;
        int selPos = places.IndexOf(_display.SelectedWorldPlace);
        int skip = fit > 0 && selPos >= fit ? (selPos - fit) / 2 * 2 + 2 : 0;
        var shown = places.Skip(skip).Take(fit).ToList();
        int leftWidth = (legendWidth - 1) / 2, rightWidth = legendWidth - 1 - leftWidth;
        void Cell(int i, int width)
        {
            var p = world.Places[i];
            var (icon, color) = WorldMapView.Icon(p.Type);
            bool hovered = i == _display.HoveredWorldPlace || i == _display.SelectedWorldPlace;
            Console.Write(" ");
            ColorHelper.WriteColored(icon.ToString(), color, bg);
            int w = width - 3;   // пробел, значок, последний символ — «←»
            string text = " " + p.Name;
            text = text.Length > w ? text[..(w - 1)] + "…" : text.PadRight(w);
            ColorHelper.WriteColored(text, hovered ? ColorHelper.Pale(_display.MainForeground, 0.6) : _display.MainForeground, bg);
            Console.Write(hovered ? "←" : " ");
        }
        for (int n = 0; n < shown.Count; n += 2)
        {
            int left = shown[n], right = n + 1 < shown.Count ? shown[n + 1] : -1;
            lines.Add(() =>
            {
                Cell(left, leftWidth);
                ColorHelper.WriteColored("│", MouseUiHelper.FrameColor(_display), bg); // цвет как у всех рамок (BorderDrawer)
                if (right >= 0) Cell(right, rightWidth);
                else Console.Write(new string(' ', rightWidth));
            });
        }
        if (places.Count > skip + shown.Count || skip > 0) Line(L.F("… ещё {0}", places.Count - shown.Count), dim);
        while (lines.Count < capacity - 3) lines.Add(() => Console.Write(new string(' ', legendWidth)));
        // Время — день и часть дня (путешествие сдвигает их на дни).
        var now = _storage.WorldState.Time;
        Line(L.F("Время: День {0} · {1}", now.Day, GameTime.PartOfDayText(now.PartOfDay)), _display.MainForeground);
        // Путь: цель кликом по карте, [F10] — в путь, [F5] — темп.
        string pace = L.T(TravelService.PaceName((TravelService.Pace)_display.TravelPace));
        if (_display.WorldTarget != null && _display.WorldRouteInfo is { } info)
        {
            double days = TravelService.DaysAt(info.days, (TravelService.Pace)_display.TravelPace);
            Line(L.F("Путь: {0} — {1}, дорогой {2:P0}", info.dest, TravelService.DaysLabel(days), info.road), _display.MainForeground);
            Line(L.F("[F10] в путь · [F12] темп: {0}", pace), ColorHelper.Pale(_display.MainForeground, 0.3));
        }
        else
        {
            Line(L.T("Клик по карте — цель пути · Tab — места"), dim);
            Line(L.F("[F12] темп: {0}", pace), dim);
        }
    }

    private bool InViewport(List<List<int>>? positions)
    {
        if (positions == null) return false;
        int c0 = _display.CameraCol, r0 = _display.CameraRow;
        int c1 = c0 + _display.ViewCols(_storage.WorldState.Map) - 1, r1 = r0 + _display.ViewRows(_storage.WorldState.Map) - 1;
        var map = _storage.WorldState.Map;
        int floor = HeroFloor();
        return positions.Any(p => p.Count >= 2 && p[0] >= c0 && p[0] <= c1 && p[1] >= r0 && p[1] <= r1 && map.FloorAt(p[0], p[1]) == floor);
    }

    // Этаж героя — легенда показывает только его (остальные этажи на поле лежат в других местах).
    private int HeroFloor() => _storage.WorldState.Hero?.Position is { Count: >= 2 } hp
        ? _storage.WorldState.Map.FloorAt(hp[0], hp[1]) : 0;

    private void BuildLocationLegendItems(List<LegendItem> items, WorldState settings, int legendWidth)
    {
        // Hero line
        if (settings.Hero != null)
        {
            var heroState = new List<(string text, List<int>? color)> { (" - ", null), ($"❤ {settings.Hero.Hp}", null) };
            var heroEffectSegs = EffectNameSegments(settings.Hero.Effects);
            if (heroEffectSegs.Count > 0)
            {
                heroState.Add((" · ", null));
                heroState.AddRange(heroEffectSegs);
            }
            items.Add(new LegendItem(MakeLegendLines(
                settings.Hero.Symbol,
                settings.Hero.Name,
                heroState,
                settings.Hero.Color,
                legendWidth,
                selected: _display.SelectedMapItemIndex == 0
            ), PriorityHero, true));
        }

        // Entity/object lines
        var groups = settings.Map.Entities?
            .Union(settings.Map.Objects)
            .Where(x => IsShown(x))
            .GroupBy(e => new { e.Symbol, e.Name, State = EffectsLabel(e) })
            .OrderBy(g => g.Key.Symbol)
            .ToList();

        if (groups != null && groups.Count > 0)
        {
            int entityOffset = settings.Hero != null ? 1 : 0;
            int groupIdx = 0;
            foreach (var group in groups)
            {
                bool selected = (groupIdx + entityOffset) == _display.SelectedMapItemIndex;
                var firstEntity = group.First();
                string coverageSuffix = CoveragePositionSuffix(group, settings);
                var effectSegs = EffectNameSegments(firstEntity.Effects);
                var state = new List<(string text, List<int>? color)>();
                if (effectSegs.Count > 0)
                {
                    state.Add((" - ", null));
                    state.AddRange(effectSegs);
                    if (coverageSuffix.Length > 0) state.Add((coverageSuffix, null));
                }
                else if (coverageSuffix.Length > 0)
                {
                    state.Add((" - ", null));
                    state.Add((coverageSuffix.TrimStart(), null));
                }
                items.Add(new LegendItem(MakeLegendLines(
                    group.Key.Symbol,
                    group.Key.Name,
                    state,
                    firstEntity.Color,
                    legendWidth,
                    selected
                ), PriorityEntity, selected));
                groupIdx++;
            }
        }
        items.Add(new LegendItem([() => Console.Write(new string(' ', legendWidth))], PriorityKeep, true));

        // Комнаты/зоны/рельеф — только то, что сейчас в окне карты (легенда — расшифровка экрана).
        // Коридоры/лазы не подписываются — их и так видно.
        if (settings.Map.Rooms != null && settings.Map.Rooms.Count > 0)
        {
            var rooms = settings.Map.Rooms
                .Where(x =>
                    x.Name is not ("Corridor" or "Коридор" or "Лаз") &&
                    (_config.RevealMap || RoomExplored(x)) && InViewport(x.Positions)
                );
            foreach (var room in rooms)
            {
                var minx = room.Positions.Min(x => x[0]);
                var maxx = room.Positions.Max(x => x[0]);
                var miny = room.Positions.Min(x => x[1]);
                var maxy = room.Positions.Max(x => x[1]);
                var pos = $" [{minx}-{maxx},{miny}-{maxy}]";
                bool hovered = HoveredIn(room.Positions);
                items.Add(new LegendItem(MakeLegendLines("███", room.Name + pos, room.Color, legendWidth, hovered), PriorityRoom, hovered));
            }
        }

        // Area lines
        if (settings.Map.Area != null && settings.Map.Area.Count > 0)
        {
            foreach (var area in settings.Map.Area.Where(a => (_config.RevealMap || AreaExplored(a)) && InViewport(a.Positions)))
            {
                var areaLabel = area.StepCostFt > 5 ? $"{area.Name} ×2" : area.Name;
                bool hovered = HoveredIn(area.Positions);
                items.Add(new LegendItem(MakeLegendLines("███", areaLabel, area.Color, legendWidth, hovered), PriorityArea, hovered));
            }
        }

        // Рельеф — одна строка на вид из исследованных клеток окна карты (не каждая клетка).
        if (settings.Map.Chunks?.Any(c => c.Terrain != null) == true)
        {
            var seen = new HashSet<char>();
            int c0 = _display.CameraCol, r0 = _display.CameraRow, floor = HeroFloor();
            for (int c = c0; c < c0 + _display.ViewCols(settings.Map); c++)
                for (int r = r0; r < r0 + _display.ViewRows(settings.Map); r++)
                    if ((_config.RevealMap || _storage.ExploredCells.Contains((c, r))) && settings.Map.FloorAt(c, r) == floor && settings.Map.TerrainAt(c, r) is { } k)
                        seen.Add(k.Code);
            var hoveredKind = _display.HoveredCell is { } h ? settings.Map.TerrainAt(h.col, h.row) : null;
            // Компактно: две колонки «символ название» (у трудной местности «×2»), без длинных описаний.
            var kinds = TerrainCatalog.Kinds.Values.Where(k => seen.Contains(k.Code) && TerrainCatalog.InLegend(k)).ToList();
            // Место под горизонтальный разделитель над блоком рельефа (рисует MapDisplay — с ├ ┬ ┤ в стыках
            // с рамками); при переполнении уходит последним из рельефа.
            if (kinds.Count > 0)
                items.Add(new LegendItem([() => { }], PriorityTerrain, false));
            for (int i = 0; i < kinds.Count; i += 2)
            {
                var pair = kinds.Skip(i).Take(2).ToList();
                items.Add(new LegendItem([() => WriteTerrainPair(pair, hoveredKind, legendWidth)], PriorityTerrain, pair.Contains(hoveredKind!)));
            }
        }
    }

    // Строка легенды с двумя видами рельефа: « ♣  : Деревья      │ ∙∙∙ : Кусты ×2     ←».
    // Стрелка наведения — в последнем символе колонки (у разделителя / у рамки легенды).
    private void WriteTerrainPair(List<TerrainKind> pair, TerrainKind? hovered, int legendWidth)
    {
        var bg = _display.MainBackground;
        int inner = legendWidth - 1;           // после ведущего пробела
        int leftWidth = (inner - 1) / 2;       // -1 — разделитель
        int rightWidth = inner - 1 - leftWidth;
        Console.Write(" ");
        WriteTerrainColumn(pair[0], hovered, leftWidth, bg);
        // Разделитель на всю высоту блока — и в последней строке с одним видом (нечётное количество).
        ColorHelper.WriteColored("│", MouseUiHelper.FrameColor(_display), bg); // цвет как у всех рамок (BorderDrawer)
        Console.Write(" ");
        if (pair.Count > 1) WriteTerrainColumn(pair[1], hovered, rightWidth - 1, bg);
        else Console.Write(new string(' ', rightWidth - 1));
    }

    // «ГГГ : Название» шириной width; последний символ — «←» под курсором, иначе пробел.
    private static void WriteTerrainColumn(TerrainKind kind, TerrainKind? hovered, int width, List<int> bg)
    {
        bool isHovered = kind == hovered;
        var color = kind.LegendColor ?? kind.Fg;
        if (isHovered) color = ColorHelper.Pale(color, 0.25);
        string name = L.T(kind.Name) + (kind.StepCostFt > 5 ? $" ×{kind.StepCostFt / 5}" : "");
        int nameWidth = width - kind.LegendGlyph.Length - 3 - 1;
        if (name.Length > nameWidth) name = name[..Math.Max(0, nameWidth - 1)] + "…";
        // У кровати — подушка (первый символ светлым).
        if (FurnitureCatalog.ByCode(kind.Code)?.HeadBg is { } head && kind.LegendGlyph.Length > 1)
        {
            ColorHelper.WriteColored(kind.LegendGlyph[..1], isHovered ? ColorHelper.Pale(head, 0.25) : head, bg);
            ColorHelper.WriteColored(kind.LegendGlyph[1..], color, bg);
        }
        else ColorHelper.WriteColored(kind.LegendGlyph, color, bg);
        Console.Write(" : ");
        ColorHelper.WriteColored(name, color, bg);
        int pad = width - kind.LegendGlyph.Length - 3 - name.Length - 1;
        if (pad > 0) Console.Write(new string(' ', pad));
        Console.Write(isHovered ? "←" : " ");
    }

    // Клетка под курсором мыши — в этой комнате/зоне: её строка в легенде подсвечивается.
    private bool HoveredIn(List<List<int>>? positions) =>
        _display.HoveredCell is { } h && positions != null && positions.Any(p => p.Count >= 2 && p[0] == h.col && p[1] == h.row);

    public List<Action> GetLegendLines()
    {
        var lines = new List<Action>();
        var settings = _storage.WorldState;
        int legendWidth = _display.GetLegendWidth(_display.ViewCols(settings.Map));
        SeparatorJunction = null;
        TerrainSeparatorIndex = -1;

        // Экран Мира: мир, королевства, места в окне карты; без блока боя.
        if (_display.MapLevel == MapLevel.World)
        {
            PathLineIndex = MoveLineIndex = -1;
            AddWorldLegendLines(lines, legendWidth);
            return lines;
        }

        AddLocationLegendLines(lines, settings, legendWidth);

        // Pad content area up to separator; slot at SeparatorLineIndex is skipped by MapDisplay.
        int separatorIndex = SeparatorLineIndex;
        while (lines.Count < separatorIndex)
            lines.Add(() => Console.Write(new string(' ', legendWidth)));
        lines.Add(() => { }); // placeholder at separatorIndex — never consumed

        // 6 combat lines (after separator)
        CombatLineIndex = lines.Count;
        foreach (var combatLine in BuildCombatLines(legendWidth))
            lines.Add(combatLine);

        // Последние строки блока боя: «Путь», «Движение», «Место», «Время» (см. BuildCombatLines).
        PathLineIndex = lines.Count - 4;
        MoveLineIndex = lines.Count - 3;
        return lines;
    }

    // Позиция (в ширине легенды) стыка вертикальной линии колонок рельефа с разделителем над блоком боя
    // (┴ в BorderDrawer.SeparatorLine) или null — рельефа над разделителем нет.
    public int? SeparatorJunction { get; private set; }

    // Строка легенды с горизонтальным разделителем над блоком рельефа (├──┬──┤ — MapDisplay) или -1.
    public int TerrainSeparatorIndex { get; private set; } = -1;

    // Строка «Порядок боя» и кнопка «[F10] КОНЕЦ ХОДА» на ней (смещение от начала строки легенды, -1 — нет кнопки:
    // не бой или ход не героя).
    public int CombatLineIndex { get; private set; } = -1;
    public (int x0, int x1) EndTurnButton { get; private set; } = (-1, -1);
    public static string EndTurnText => "[F10] " + L.T("КОНЕЦ ХОДА");

    // Ход героя в бою — кнопка конца хода активна.
    public bool IsHeroTurn => _storage.WorldState.Combat is { Active: true } c && c.CurrentTurn == _storage.WorldState.Hero?.Symbol;

    // Индексы строк легенды, которые меняются при наведении на клетку (MapDisplay.RedrawLegendLine).
    public int PathLineIndex { get; private set; } = -1;
    public int MoveLineIndex { get; private set; } = -1;

    private static readonly List<List<int>> _actionColors =
    [
        [  70, 200,  70 ],  // id:0 Действие
        [ 160, 100,  50 ],  // id:1 Бон. действие
        [ 220,  70,  70 ],  // id:2 Реакция
    ];
    private static readonly string[] _actionSymbols = ["●", "■", "▲"];

    private List<Action> BuildCombatLines(int width)
    {
        var dim    = new List<int> { 100, 105, 115 };
        var bright = new List<int> { 220, 225, 240 };
        var spent  = new List<int> {  65,  68,  78 };
        var spdOn  = new List<int> { 215, 215, 225 };
        var spdOff = new List<int> {  65,  68,  78 };
        var fg     = _display.MainForeground;
        var bg     = _display.MainBackground;

        string label1 = L.T("Порядок боя").PadRight(15);
        string label2 = L.T("Движение:").PadRight(15);
        string label7 = L.T("Время:").PadRight(15);

        var hero = _storage.WorldState.Hero;
        var time = _storage.WorldState.Time;

        int speedMax  = hero?.SpeedMax ?? 0;
        int speedLeft = Math.Max(0, hero?.SpeedLeft ?? 0);
        int sqTotal   = Math.Max(speedLeft, speedMax) / 5;
        int sqWhite   = speedMax > 0 ? speedLeft / 5 : 0;
        int sqGray    = speedMax > 0 ? sqTotal - sqWhite : 0;
        string sqWhiteStr = sqWhite > 0 ? new string('■', sqWhite) : "";
        string sqGrayStr  = sqGray  > 0 ? new string('■', sqGray)  : "";
        string speedNum   = speedMax > 0 ? $" {speedLeft}/{speedMax}" : "";

        string timeStr = L.F("День {0} · {1} · Раунд {2}", time.Day, GameTime.PartOfDayText(time.PartOfDay), time.TotalRounds);


        var actions = hero?.Actions?.Where(a => a.Deleted != true).ToList() ?? [];

        var lines = new List<Action>
        {
            // инициатива
            () =>
            {
                int lineStart = Console.CursorLeft;
                Console.Write(" ");
                ColorHelper.WriteColored(label1, dim, bg);
                var combat = _storage.WorldState.Combat;
                if (combat?.Active == true && combat.Initiative is { Count: > 0 })
                {
                    foreach (var entry in combat.Initiative.Where(e => e.Deleted != true))
                    {
                        bool isCurrent = entry.Symbol == combat.CurrentTurn;
                        string sym = entry.Symbol?.Length == 3 ? entry.Symbol : "???";
                        var color = GetSymbolColor(entry.Symbol);
                        ColorHelper.WriteColored(isCurrent ? $"⚔️{sym}" : $" {sym} ", color, bg);
                    }
                    // Справа — кнопка конца хода (вместо текста «завершаю ход» мастеру), если влезает.
                    // Снимок легенды (MapDisplay.CaptureOutput) пишет в строку — курсор не движется, ширина по тексту.
                    int used = Console.Out is StringWriter ? 1 + label1.Length + 5 * combat.Initiative.Count(e => e.Deleted != true)
                                                           : Console.CursorLeft - lineStart;
                    int at = width - EndTurnText.Length - 1;
                    if (IsHeroTurn && used < at)
                    {
                        Console.Write(new string(' ', at - used));
                        ColorHelper.WriteColored("[F10] ", dim, bg);
                        ColorHelper.WriteColored(L.T("КОНЕЦ ХОДА"), bright, bg);
                        EndTurnButton = (at, at + EndTurnText.Length - 1);
                    }
                    else EndTurnButton = (-1, -1);
                }
                else
                {
                    EndTurnButton = (-1, -1);
                    ColorHelper.WriteColored("—", dim, bg);
                }
            },
        };

        // динамические строки действий
        for (int i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            var color = i < _actionColors.Count ? _actionColors[i] : bright;
            string sym = i < _actionSymbols.Length ? _actionSymbols[i] : "●";
            string label = PadActionLabel(L.T(a.Name));
            int activeCount = a.Value;
            int spentCount  = a.MaxValue - a.Value;

            lines.Add(() =>
            {
                Console.Write(" ");
                ColorHelper.WriteColored(label, dim, bg);
                for (int j = 0; j < activeCount; j++)
                {
                    if (j > 0) Console.Write(" ");
                    ColorHelper.WriteColored(sym, color, bg);
                }
                if (activeCount > 0 && spentCount > 0) Console.Write(" ");
                for (int j = 0; j < spentCount; j++)
                {
                    if (j > 0) Console.Write(" ");
                    ColorHelper.WriteColored(sym, spent, bg);
                }
                if (a.MaxValue == 0) ColorHelper.WriteColored("—", dim, bg);
            });
        }

        // Путь до клетки под курсором (в ход героя) + сколько квадратов движения он съест.
        var hover = _display.HoverPath;
        var path = hover is { ShowPath: true } ? hover : null;
        int pathSquares = path?.CostFt is int pc ? Math.Min(pc / 5, sqWhite) : 0;
        bool notEnough = path?.CostFt is int need && need > speedLeft || path is { CostFt: null };
        var pathColor = new List<int> { 230, 170, 80 };
        var badColor  = new List<int> { 225, 95, 85 };

        // строка пути (пустая, если не наводим) + движение + время — итого CombatReservedLines всего
        lines.Add(() =>
        {
            if (hover == null) return;
            Console.Write(" ");
            if (path == null)
            {
                ColorHelper.WriteColored(L.T("Дистанция:").PadRight(15), dim, bg);
                ColorHelper.WriteColored(L.F("{0} фт", hover.DistanceFt), fg, bg);
                return;
            }
            ColorHelper.WriteColored(L.T("Путь:").PadRight(15), dim, bg);
            string target = path.TargetSymbol != null ? L.F("до {0}", path.TargetSymbol) + " · " : "";
            if (path.CostFt is not int cost)
                ColorHelper.WriteColored(target + L.T("не дойти"), badColor, bg);
            else if (cost == 0)
                ColorHelper.WriteColored(target + L.T("рядом"), fg, bg);
            else if (cost > speedLeft)
                ColorHelper.WriteColored(target + L.F("{0} фт · не хватит {1}", cost, cost - speedLeft), badColor, bg);
            else
                ColorHelper.WriteColored(target + L.F("{0} фт · останется {1}", cost, speedLeft - cost), fg, bg);
        });
        lines.Add(() =>
        {
            Console.Write(" ");
            ColorHelper.WriteColored(label2, dim, bg);
            if (sqWhite > 0)
            {
                // Квадраты, которые уйдут на путь, — в конце белой части; не хватает — вся белая часть красная.
                int keep = notEnough ? 0 : sqWhite - pathSquares;
                if (keep > 0) ColorHelper.WriteColored(new string('■', keep), spdOn, bg);
                if (sqWhite - keep > 0)
                    ColorHelper.WriteColored(new string('■', sqWhite - keep), path == null ? spdOn : notEnough ? badColor : pathColor, bg);
            }
            if (sqGray  > 0) ColorHelper.WriteColored(sqGrayStr,  spdOff, bg);
            if (speedMax == 0) ColorHelper.WriteColored("—", dim, bg);
            if (speedNum.Length > 0) ColorHelper.WriteColored(speedNum, dim, bg);
        });
        // Где герой: этаж (если есть этажи) и комната / «снаружи» — над временем.
        lines.Add(() =>
        {
            Console.Write(" ");
            ColorHelper.WriteColored(L.T("Место:").PadRight(15), dim, bg);
            ColorHelper.WriteColored(HeroPlace(width - 16), fg, bg);
        });
        lines.Add(() =>
        {
            Console.Write(" ");
            ColorHelper.WriteColored(label7, dim, bg);
            ColorHelper.WriteColored(timeStr, fg, bg);
        });

        return lines;
    }

    private string HeroPlace(int maxLen)
    {
        var settings = _storage.WorldState;
        if (settings.Hero?.Position is not { Count: >= 2 } hp) return "—";
        string place = settings.Map.RoomAt(hp[0], hp[1]) is { Deleted: not true } room ? room.Name : L.T("снаружи");
        if (settings.Map.HasFloors) place = $"{MapConfig.FloorLabel(settings.Map.FloorAt(hp[0], hp[1]))} · {place}";
        return place.Length > maxLen ? place[..Math.Max(0, maxLen - 1)] + "…" : place;
    }

    private List<int> GetSymbolColor(string? symbol)
    {
        var world = _storage.WorldState;
        if (world.Hero?.Symbol == symbol && world.Hero.Color != null)
            return world.Hero.Color;
        var entity = world.Map.Entities?.FirstOrDefault(e => e.Symbol == symbol && e.Deleted != true);
        if (entity?.Color != null) return entity.Color;
        return _display.MainForeground;
    }

    private static string PadActionLabel(string name)
    {
        string raw = name + ": ";
        return raw.Length <= 15 ? raw.PadRight(15) : raw[..15];
    }

    // На сетке рисуется только ОДИН символ на клетку — сущности всегда перекрывают объекты
    // (см. MapObjectsProvider.WriteCellEntity: сперва проверяются entities, потом objects), а
    // герой перекрывает всё на своей клетке. Если объект оказался в одной клетке с героем или
    // другой сущностью, его символ никогда не виден на карте — легенда всё равно должна его
    // перечислять (сам факт видимости уже проверен фильтром выше), но без координат игрок не
    // поймёт, где именно он находится.
    private string CoveragePositionSuffix(IEnumerable<CellEntity> groupMembers, WorldState settings)
    {
        var members = groupMembers as IList<CellEntity> ?? groupMembers.ToList();
        if (members.Any(x => x is LivingEntity)) return ""; // сущности сюда не подпадают

        var positions = members
            .Where(x => x.Position?.Count >= 2)
            .Select(x => (col: x.Position[0], row: x.Position[1]))
            .Distinct()
            .ToList();
        if (positions.Count != 1) return ""; // несколько разных позиций в группе — неоднозначно
        var (col, row) = positions[0];

        bool coveredByHero = settings.Hero?.Position is [int hc, int hr] && hc == col && hr == row;
        // Только видимым сейчас существом: иначе пометка у предмета в тумане выдавала, что на клетке кто-то стоит.
        bool coveredByEntity = settings.Map.Entities?.Any(e =>
            e.Deleted != true && e.Position?.Count >= 2 && e.Position[0] == col && e.Position[1] == row
            && (_config.RevealMap || (e.Hidden != true && _storage.VisibleCells.Contains((col, row))))) == true;

        return (coveredByHero || coveredByEntity) ? $" [{col},{row}]" : "";
    }

    // Заменяет прежнее свободнотекстовое поле State: имена активных статус-эффектов сущности,
    // через запятую — используется как ключ группировки (чтобы, например, отравленный орк не
    // сливался в легенде со здоровым). Для отображения см. EffectNameSegments — каждое имя красится
    // в свой StatusEffect.Color.
    private static string EffectsLabel(CellEntity e) =>
        e.Effects == null ? "" : string.Join(", ", e.Effects.Where(f => f.Deleted != true).Select(f => f.Name));

    // Имена активных эффектов как отдельные цветные сегменты (через ", "), для MakeLegendLines.
    private static List<(string text, List<int>? color)> EffectNameSegments(List<StatusEffect>? effects)
    {
        var segs = new List<(string, List<int>?)>();
        var active = effects?.Where(f => f.Deleted != true).ToList();
        if (active == null) return segs;
        for (int i = 0; i < active.Count; i++)
        {
            if (i > 0) segs.Add((", ", null));
            segs.Add((active[i].Name, active[i].Color));
        }
        return segs;
    }

    private bool RoomExplored(Room room)
    {
        var countExp = room.Positions.Count(p => _storage.ExploredCells.Contains((p[0], p[1])));
        return countExp > room.Positions.Count / 2;
    }

    private bool AreaExplored(Area area)
    {
        return area.Positions.Any(p => _storage.ExploredCells.Contains((p[0], p[1])));
    }

    public (string? image, string? name, List<int>? color) GetSelectedEntityImageAndName()
    {
        var e = ResolveSelectedEntity();
        return e != null ? (e.Image, e.Name, e.Color) : (null, null, null);
    }

    // Для карточки монстра (MouseUiHelper.SetSelectedMonster) — нужна сама сущность (MonsterKey),
    // не только тройка картинка/имя/цвет. Объекты (не LivingEntity) сюда не попадают — null.
    public LivingEntity? GetSelectedLivingEntity() => ResolveSelectedEntity() as LivingEntity;

    // Выбранный элемент карты (герой/существо/объект) — для дистанции в подписи правой панели.
    public CellEntity? GetSelectedEntity() => ResolveSelectedEntity();

    private CellEntity? ResolveSelectedEntity()
    {
        var settings = _storage.WorldState;
        if (_display.SelectedMapItemIndex < 0) return null;

        if (_display.SelectedMapItemIndex == 0 && settings.Hero != null)
            return settings.Hero;

        int entityOffset = settings.Hero != null ? 1 : 0;
        int targetIdx = _display.SelectedMapItemIndex - entityOffset;

        var groups = settings.Map.Entities?
            .Union(settings.Map.Objects)
            .Where(x => IsShown(x))
            .GroupBy(e => new { e.Symbol, e.Name, State = EffectsLabel(e) })
            .OrderBy(g => g.Key.Symbol)
            .ToList();

        if (groups != null && targetIdx >= 0 && targetIdx < groups.Count)
            return groups[targetIdx].First();

        return null;
    }

    // В легенде и для наведения: что герой видит сейчас; предметы — ещё и запомненные в тумане (существа — нет:
    // где они сейчас, неизвестно).
    private bool IsShown(CellEntity x) =>
        _config.RevealMap || (x.Hidden != true && (_storage.VisibleCells.Contains((x.Position[0], x.Position[1])) || _storage.IsRemembered(x)));

    public int GetSelectableIndexForCell(int col, int row)
    {
        var settings = _storage.WorldState;

        if (settings.Hero?.Position is [int hc, int hr] && hc == col && hr == row)
            return 0;

        int entityOffset = settings.Hero != null ? 1 : 0;

        var groups = settings.Map.Entities?
            .Union(settings.Map.Objects ?? [])
            .Where(x => IsShown(x))
            .GroupBy(e => new { e.Symbol, e.Name, State = EffectsLabel(e) })
            .OrderBy(g => g.Key.Symbol)
            .ToList();

        if (groups != null)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Any(e => e.Position[0] == col && e.Position[1] == row))
                    return entityOffset + i;
            }
        }

        return -1;
    }

    public int GetSelectableMapItemCount()
    {
        var settings = _storage.WorldState;
        int heroCount = settings.Hero != null ? 1 : 0;
        int entityCount = (settings.Map.Entities ?? [])
            .Union(settings.Map.Objects ?? [])
            .Where(x => x.Deleted != true && (IsShown(x)))
            .GroupBy(e => new { e.Symbol, e.Name, State = EffectsLabel(e) })
            .Count();
        return heroCount + entityCount;
    }

    // symbol+name в цвете сущности (ярче если selected); state — список цветных сегментов
    // (null-цвет сегмента = цвет строки renderColor) — так у каждого статус-эффекта может быть
    // свой StatusEffect.Color, не совпадающий с цветом самой сущности.
    private static List<Action> MakeLegendLines(
        string symbol, string name, List<(string text, List<int>? color)> state,
        List<int> color, int legendWidth, bool selected = false)
    {
        var renderColor = selected ? ColorHelper.Pale(color, 0.25) : color;
        int prefixLen = symbol.Length + 3;
        // -1: отступ слева перед текстом легенды (Console.Write(" ") ниже).
        int contentWidth = legendWidth - prefixLen - 1;
        string indent = new string(' ', prefixLen);

        var segments = new List<(string text, List<int> color)> { (name, renderColor) };
        foreach (var (text, segColor) in state)
            segments.Add((text, segColor ?? renderColor));

        string fullContent = string.Concat(segments.Select(s => s.text));
        var wrapped = contentWidth > 0 ? TextWrapper.WrapText(fullContent, contentWidth) : [fullContent];
        if (wrapped.Count == 0) wrapped.Add(string.Empty);

        var result = new List<Action>();
        int globalPos = 0;
        for (int i = 0; i < wrapped.Count; i++)
        {
            string line = wrapped[i];
            bool isFirst = i == 0;
            int lineStart = globalPos;
            globalPos += line.Length;

            result.Add(() =>
            {
                Console.Write(" ");
                if (isFirst)
                {
                    ColorHelper.WriteColored(symbol, renderColor);
                    Console.Write(" : ");
                }
                else
                {
                    Console.Write(indent);
                }
                WriteSegmentsInRange(segments, lineStart, line.Length);
                int pad = contentWidth - line.Length;
                if (selected && isFirst && pad > 0)
                {
                    Console.Write(new string(' ', pad - 1));
                    Console.Write("←");
                }
                else if (pad > 0)
                {
                    Console.Write(new string(' ', pad));
                }
            });
        }
        return result;
    }

    // Пишет только ту часть каждого сегмента, что попадает в [rangeStart, rangeStart+rangeLen) —
    // используется, чтобы применить построчный wrap к содержимому, состоящему из разноцветных кусков.
    private static void WriteSegmentsInRange(List<(string text, List<int> color)> segments, int rangeStart, int rangeLen)
    {
        int rangeEnd = rangeStart + rangeLen;
        int pos = 0;
        foreach (var (text, segColor) in segments)
        {
            int segStart = pos;
            int segEnd = pos + text.Length;
            pos = segEnd;
            if (segEnd <= rangeStart || segStart >= rangeEnd) continue;

            int sliceStart = Math.Max(segStart, rangeStart) - segStart;
            int sliceEnd = Math.Min(segEnd, rangeEnd) - segStart;
            string slice = text[sliceStart..sliceEnd];
            if (slice.Length > 0)
                ColorHelper.WriteColored(slice, segColor);
        }
    }

    // Для комнат и area — без разделения имени и состояния
    private static List<Action> MakeLegendLines(string prefix, string content, List<int> color, int legendWidth, bool selected = false)
    {
        int prefixLen = prefix.Length + 3;
        // -1: отступ слева перед текстом легенды (Console.Write(" ") ниже).
        int contentWidth = legendWidth - prefixLen - 1;
        string indent = new string(' ', prefixLen);

        var wrapped = contentWidth > 0
            ? TextWrapper.WrapText(content, contentWidth)
            : [content];

        if (wrapped.Count == 0) wrapped.Add(string.Empty);

        var renderColor = selected ? ColorHelper.Pale(color, 0.25) : color;
        var result = new List<Action>();
        for (int i = 0; i < wrapped.Count; i++)
        {
            string line = wrapped[i];
            bool isFirst = i == 0;
            result.Add(() =>
            {
                Console.Write(" ");
                if (isFirst)
                {
                    ColorHelper.WriteColored(prefix, renderColor);
                    Console.Write(" : ");
                }
                else
                {
                    Console.Write(indent);
                }
                ColorHelper.WriteColored(line, renderColor);
                int pad = contentWidth - line.Length;
                if (selected && isFirst && pad > 0)
                {
                    Console.Write(new string(' ', pad - 1));
                    Console.Write("←");
                }
                else if (pad > 0)
                {
                    Console.Write(new string(' ', pad));
                }
            });
        }
        return result;
    }
}
