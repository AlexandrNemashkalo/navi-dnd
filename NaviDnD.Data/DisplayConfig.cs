using NaviDnD.Data.Models;

namespace NaviDnD.Data;

public enum CharacterSubTab { Inventory, Abilities, Effects, Spells }

// Что показывает цикл карты: локацию (экран F1) или карту мира (экран F4). Локация может быть больше
// экрана — её смотрят камерой (CameraCol/Row), отдельного «района» нет. Мир пока — тестовая карта.
public enum MapLevel { World, Location }

public class DisplayConfig
{
    public const int LegendPanelWidth = 58; //38;
    private const int BaseCols = 20;

    // Отступ слева перед рамкой (BorderDrawer.WriteLeftMargin) — для симметрии с обязательным
    // столбцом-предохранителем справа (см. ConsoleSetup.SetConsoleConfig: без него автоперенос на
    // последней колонке буфера ломает SafeNewLine). Единая точка правки — используется во всех
    // расчётах абсолютных экранных колонок (клики мышью, попадание в ячейки карты и т.д.).
    public const int LeftMargin = 1;

    public int CellWidth { get; init; } = 3;
    public char Delimetr { get; init; } = ' ';
    public int MaxHistoryLines { get; init; } = 21;

    // Прокрутка списка легенды тактической карты (строк сверху), колесо мыши над легендой.
    public int LegendScroll { get; set; }

    // Высота карты (15 строк, см. DungeonGenerator.MaxRows / HardcodedCaveGenerator.Rows) при текущей ширине клетки.
    // Служит "полом" для MaxHigh, пока карта ещё не отрисована ни разу (например, в редакторе персонажа при создании игры) —
    // без этого карточка персонажа/способностей в редакторе была бы ниже, чем во время игры.
    public const int FixedMapContentHigh = 34;

    public int MapHigh { get; set; }
    public int HeroHigh { get; set; }
    public int AbilityHigh { get; set; }
    public int MaxHigh => Math.Max(Math.Max(MapHigh, HeroHigh), Math.Max(AbilityHigh, FixedMapContentHigh));

    public List<int> Door { get; init; } = [128, 128, 128 ];
    public  List<int> MainBackground { get; init; }= [18, 18, 22];
    public List<int> MainForeground { get; init; } = [160, 210, 160];
    public List<int> SystemCommandHistory { get; init; } = [120, 120, 130];

    public bool ArrowKeysMovement { get; set; } = false;
    public int CombatReservedLines { get; init; } = 8;   // порядок боя, 3 действия, путь, движение, место, время

    public int DialogRightPanelWidth { get; set; } = 36;

    public int DialogScrollOffset { get; set; } = 0;
    public int InventoryScrollOffset { get; set; } = 0;
    public int AbilityScrollOffset { get; set; } = 0;
    public int AbilityPageOffset { get; set; } = 0;
    public int EffectsPageOffset { get; set; } = 0;
    public int SpellsPageOffset { get; set; } = 0;

    public CharacterSubTab CharacterSubTab { get; set; } = CharacterSubTab.Inventory;
    public MapLevel MapLevel { get; set; } = MapLevel.Location;

    // ── Камера карты локации ──────────────────────────────────────────────────
    // Карта может быть больше экрана: показывается окно не больше ViewportCols×ViewportRows клеток.
    // CameraCol/CameraRow — координаты карты левой нижней клетки окна. Вся раскладка экрана (ширина,
    // легенда, рамки) считается от размера окна (ViewCols/ViewRows), а не всей карты.
    public const int ViewportCols = 20;
    public const int ViewportRows = 15;
    public int CameraCol { get; set; } = 1;
    public int CameraRow { get; set; } = 1;
    // Окно всегда ViewportCols×ViewportRows: локация — поле до MapConfig.MaxSize×MaxSize, по которому
    // ходит камера; клетки за пределами сгенерированной карты — просто пустота (не исследованы).
    public int ViewCols(MapConfig map) => ViewportCols;
    public int ViewRows(MapConfig map) => ViewportRows;

    // Клетка окна (1..ViewCols, 1..ViewRows; ConsoleMouseReader.ScreenToMapCell) → клетка карты.
    public (int col, int row)? ScreenCellToWorld((int col, int row)? cell) =>
        cell is { } c ? (c.col + CameraCol - 1, c.row + CameraRow - 1) : null;
    // Камера карты мира: центр (в клетках мира) — таскается мышью; масштаб колесом/Ctrl ±
    // (0 — подробно, клетка = 2 символа; 1..4 — клеток мира на «пиксель» полублока, WorldMapView).
    public double WorldCenterX { get; set; } = -1;
    public double WorldCenterY { get; set; } = -1;
    public int WorldZoomStep { get; set; } = 3;

    // Путешествие по карте мира: выбранная цель (клетка), маршрут до неё (клетки) и темп (0 — медленный,
    // 1 — обычный, 2 — быстрый). Цель выбирается кликом по карте, [F10] — в путь, [F12] — темп.
    public (int x, int y)? WorldTarget { get; set; }
    public List<(int x, int y)>? WorldRoute { get; set; }
    // Сводка выбранного пути (для легенды без пересчёта): куда, дней при обычном темпе, доля дороги.
    public (string dest, double days, double road)? WorldRouteInfo { get; set; }
    public int TravelPace { get; set; } = 1;
    // Место мира под курсором (индекс в WorldMap.Places) или -1.
    public int HoveredWorldPlace { get; set; } = -1;
    // Выбранное место мира (Tab/клик) — подсвечено на карте, его карточка справа; -1 — нет.
    public int SelectedWorldPlace { get; set; } = -1;
    // Фильтр карты мира: какие категории показывать (WorldMapView.Categories).
    public HashSet<string> WorldFilter { get; set; } = ["settlements", "sites", "nature"];

    // "F1"/"F2"/"F3" — какой из верхнеуровневых табов текущего экрана сейчас активен (Program.cs
    // выставляет per-screen). Используется только для звука: клавиша уже активного таба — no-op.
    public string? ActiveTabKey { get; set; }

    // Ввод временно недоступен (например, пока крутится кубик) — рендер вкладок/стрелок должен
    // рисовать себя затемнённым (ColorHelper.Darker), не дожидаясь фактического клика/наведения.
    public bool InputDisabled { get; set; }

    public string PendingInputText { get; set; } = "";
    public string? PendingCommand { get; set; }

    public int SelectedMapItemIndex { get; set; } = -1;
    public int SelectedInventoryIndex { get; set; } = -1;

    // "Закреплённый" предмет инвентаря (клик или Tab/Shift+Tab) — в отличие от SelectedInventoryIndex
    // (который наведение мышью тоже двигает и сбрасывает при уходе мыши), это переживает уход мыши с
    // инвентаря: картинка/записка закреплённого предмета остаётся, пока мышь наводится на стрелки
    // страниц самой картинки (иначе туда было бы не попасть — уход с инвентаря сбросил бы показ).
    public int PinnedInventoryIndex { get; set; } = -1;
    public int NotePage { get; set; } = 0;
    public int NotePageCount { get; set; } = 0;

    public int InventoryPageFirstItemIdx { get; set; } = -1;
    public int InventoryPageLastItemIdx { get; set; } = -1;
    public int InventoryPageFirstNextItemIdx { get; set; } = -1;
    public int InventoryPageFirstPrevItemIdx { get; set; } = -1;

    // Тот же принцип наведения/закрепления, что у инвентаря (см. PinnedInventoryIndex), только
    // индекс — позиция в HeroDisplay.SortedSpells(hero), а не в исходном hero.Spells.
    public int SelectedSpellIndex { get; set; } = -1;
    public int PinnedSpellIndex { get; set; } = -1;

    public int SpellsPageFirstItemIdx { get; set; } = -1;
    public int SpellsPageLastItemIdx { get; set; } = -1;
    public int SpellsPageFirstNextItemIdx { get; set; } = -1;
    public int SpellsPageFirstPrevItemIdx { get; set; } = -1;

    public string[]? SelectedImageLines { get; set; }
    public List<int>? SelectedImageColor { get; set; }
    // Карточка заклинания: цвет заклинания красит только первую строку (название), остальной текст
    // (школа/поля/описание) — обычным цветом. Предметы/сущности красятся целиком (флаг = false).
    public bool SelectedImageColorTitleOnly { get; set; }
    // Многоцветные строки панели картинки (карточка монстра): отрезки на строку, null-цвет — основной.
    // Действуют, только пока SelectedImageLines — тот же массив, для которого их построили
    // (любой другой SetSelected* заменяет SelectedImageLines, и старые отрезки автоматически игнорируются).
    public List<(string text, List<int>? color)>?[]? SelectedImageSegments { get; set; }
    public string[]? SelectedImageSegmentsFor { get; set; }
    public string[]? DefaultImageLines { get; set; }
    public List<int>? DefaultImageColor { get; set; }

    public int MapDrawTop { get; set; }
    public (int col, int row)? HoveredCell { get; set; }
    public Door? HoveredDoor { get; set; }
    public bool MapHoverEnabled { get; set; } = true;
    public string? StreamingTabTitle { get; set; }
    public Action? PollAction { get; set; }
    public Action? OnMapRedraw { get; set; }
    // Точечная перерисовка клеток карты (без пересчёта видимости) — для превью select_target.
    public Action<IEnumerable<(int col, int row)>>? OnMapCellsRedraw { get; set; }
    // Активный выбор цели (select_target): подсветка клеток на карте. null — выбора нет.
    public TargetSelectionView? TargetSelection { get; set; }
    // Клетка под курсором: дистанция, а в бою в ход героя — путь и превью расхода в полоске движения.
    public HoverPathInfo? HoverPath { get; set; }
    // Закреплённый кликом элемент карты: его картинка возвращается, когда мышь уходит с других клеток.
    public CellEntity? PinnedMapEntity { get; set; }
    // Сколько сообщений диалога уже было на экране: пришло новое — диалог прыгает на последнюю страницу.
    public int DialogSeenMessageCount { get; set; } = -1;
    public Func<string, (Action? draw, string? title)>? TabSwitchProvider { get; set; }

    // Poll action factories: called with current DialogDisplay (as object) to create fresh poll action on tab switch
    public Func<object, Action?>? CharacterPollActionFactory { get; set; }
    // Журнал ([F3]): опрос мыши (закладки, листание, наведение на запись) и Tab-выбор; JournalShown — журнал
    // сейчас на экране (тогда F6–F8 — его закладки, а не подвкладки персонажа).
    public Func<object, Action?>? JournalPollActionFactory { get; set; }
    public Action? JournalOnTabKey { get; set; }
    public Action? JournalOnShiftTabKey { get; set; }
    public bool JournalShown { get; set; }
    // Показывать подсказки клавиш «[F1]» (AppConfig.ShowKeyHints); выключено — на их месте пробелы той же длины.
    public bool ShowKeyHints { get; set; } = true;
    public Func<object, Action?>? MapPollActionFactory { get; set; }
    public Func<object, Action?>? AbilitiesPollActionFactory { get; set; }

    // Tab/ShiftTab key handlers active on current tab (for streaming context)
    public Action? OnTabKey { get; set; }
    public Action? OnShiftTabKey { get; set; }
    // Per-tab stored Tab handlers — restored by SwitchTab when switching tabs
    public Action? MapOnTabKey { get; set; }
    public Action? MapOnShiftTabKey { get; set; }
    public Action? CharacterOnTabKey { get; set; }
    public Action? CharacterOnShiftTabKey { get; set; }
    // Redraws the current tab's main content (not the dialog block); saves/restores cursor
    public Action? RedrawCurrentContent { get; set; }

    /// <summary>
    /// Calculate legend width. If cols < 20, width increases by (20 - cols) * 4.
    /// </summary>
    public int GetLegendWidth(int cols)
    {
        int extraWidth = Math.Max(0, (BaseCols - cols) * (CellWidth + 1));
        return LegendPanelWidth + extraWidth;
    }

    public int InnerWidth(int cols) => cols * (CellWidth + 1) + 8 + GetLegendWidth(cols);
}

// Подсказка о клетке под курсором (строка блока боя). ShowPath — бой и ход героя: показывать путь,
// иначе только дистанцию. CostFt: null — не дойти; TargetSymbol — курсор на существе (путь до соседней клетки).
public sealed record HoverPathInfo(int DistanceFt, bool ShowPath, int? CostFt, string? TargetSymbol);

public class TargetSelectionView
{
    public HashSet<(int col, int row)> Selectable { get; } = [];
    // Начало фигуры (герой для конуса/линии): сама клетка не красится, но линии/углы вокруг неё
    // считаются подсвеченными — фигура визуально растёт из героя.
    public (int col, int row)? Origin { get; set; }
    public HashSet<(int col, int row)> Preview { get; set; } = [];
    public HashSet<(int col, int row)> Selected { get; } = [];
}
