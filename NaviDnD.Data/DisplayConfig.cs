using NaviDnD.Data.Models;

namespace NaviDnD.Data;

public enum CharacterSubTab { Inventory, Abilities, Effects, Spells }

// What the map cycle shows: a location (screen F1) or a world map (screen F4).). The location can be larger
// than the screen - it is viewed with a camera (CameraCol/Row), there is no separate “district”). The world is
// still a test map.
public enum MapLevel { World, Location }

public class DisplayConfig
{
    public const int LegendPanelWidth = 58; //38;
    private const int BaseCols = 20;

    // Indentation on the left in front of the frame (BorderDrawer.WriteLeftMargin) - for symmetry with a
    // mandatory safety column on the right (see ConsoleSetup.SetConsoleConfig: without it, auto-transfer on the
    // last column of the buffer breaks SafeNewLine). Single edit point - used in all calculations of absolute
    // screen columns (mouse clicks, getting into map cells, etc.).
    public const int LeftMargin = 1;

    public int CellWidth { get; init; } = 3;
    public char Delimetr { get; init; } = ' ';
    public int MaxHistoryLines { get; init; } = 21;

    // Scrolling the tactical map legend list (rows at the top), mouse wheel over the legend.
    public int LegendScroll { get; set; }

    // Card height (15 lines, see DungeonGenerator.MaxRows / HardcodedCaveGenerator.Rows) at the current cell
    // width. Serves as a “floor” for MaxHigh while the card has not yet been drawn even once (for example, in
    // the character editor when creating a game) - without this, the character/ability card in the editor would
    // be lower than during the game.
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
    public int CombatReservedLines { get; init; } = 8;   // order of battle, 3 actions, path, movement, place, time

    public int DialogRightPanelWidth { get; set; } = 36;

    public int DialogScrollOffset { get; set; } = 0;
    public int InventoryScrollOffset { get; set; } = 0;
    public int AbilityScrollOffset { get; set; } = 0;
    public int AbilityPageOffset { get; set; } = 0;
    public int EffectsPageOffset { get; set; } = 0;
    public int SpellsPageOffset { get; set; } = 0;

    public CharacterSubTab CharacterSubTab { get; set; } = CharacterSubTab.Inventory;
    public MapLevel MapLevel { get; set; } = MapLevel.Location;

    // ── Location map camera ───────────────────────── ───────────────────────── The map can be larger than the
    // screen: a window no larger than ViewportCols×ViewportRows cells is shown. CameraCol/CameraRow — map
    // coordinates of the lower left cell of the window. The entire screen layout (width, legend, frames) is
    // calculated from the window size (ViewCols/ViewRows), not the entire map.
    public const int ViewportCols = 20;
    public const int ViewportRows = 15;
    public int CameraCol { get; set; } = 1;
    public int CameraRow { get; set; } = 1;
    // The window is always ViewportCols×ViewportRows: location - the field up to MapConfig.MaxSize×MaxSize,
    // along which the camera walks; the cells outside the generated map are just emptiness (not explored).
    public int ViewCols(MapConfig map) => ViewportCols;
    public int ViewRows(MapConfig map) => ViewportRows;

    // Window cell (1..ViewCols, 1..ViewRows; ConsoleMouseReader.ScreenToMapCell) → map cell.
    public (int col, int row)? ScreenCellToWorld((int col, int row)? cell) =>
        cell is { } c ? (c.col + CameraCol - 1, c.row + CameraRow - 1) : null;
    // World map camera: center (in world cells) - dragged with the mouse; scale wheel/Ctrl ± (0 - detail, cell
    // = 2 characters; 1..4 - world cells per “pixel” of the half-block, WorldMapView).
    public double WorldCenterX { get; set; } = -1;
    public double WorldCenterY { get; set; } = -1;
    public int WorldZoomStep { get; set; } = 3;

    // Travel on the world map: the selected goal (cell), route to it (cells) and pace (0 - slow, 1 - normal, 2
    // - fast). The target is selected by clicking on the map, [F10] - go, [F12] - temp.
    public (int x, int y)? WorldTarget { get; set; }
    public List<(int x, int y)>? WorldRoute { get; set; }
    // Summary of the selected path (for the legend without recalculation): where, days at normal pace, share of the road.
    public (string dest, double days, double road)? WorldRouteInfo { get; set; }
    public int TravelPace { get; set; } = 1;
    // Place of the world under the cursor (index in WorldMap.Places) or -1.
    public int HoveredWorldPlace { get; set; } = -1;
    // Selected place of the world (Tab/click) - highlighted on the map, its card is on the right; -1 - no.
    public int SelectedWorldPlace { get; set; } = -1;
    // World map filter: which categories to show (WorldMapView.Categories).
    public HashSet<string> WorldFilter { get; set; } = ["settlements", "sites", "nature"];

    // "F1"/"F2"/"F3" - which of the top-level tabs of the current screen is currently active (Program.cs sets
    // per-screen). Used only for sound: the key of an already active tab is no-op.
    public string? ActiveTabKey { get; set; }

    // Input is temporarily unavailable (e.g. while the cube is spinning) - the tab/arrow renderer should draw
    // itself dimmed (ColorHelper.Darker)) without waiting for the actual click/hover.
    public bool InputDisabled { get; set; }

    public string PendingInputText { get; set; } = "";
    public string? PendingCommand { get; set; }

    public int SelectedMapItemIndex { get; set; } = -1;
    public int SelectedInventoryIndex { get; set; } = -1;

    // A “pinned” inventory item (click or Tab/Shift+Tab) - unlike SelectedInventoryIndex (which mouse-over also
    // moves and resets when the mouse leaves), this survives the mouse leaving the inventory: the picture/note
    // of the pinned item remains while the mouse is hovering over the arrows of the pages of the picture itself
    // (otherwise it would not be possible to get there - leaving the inventory would reset the display).
    public int PinnedInventoryIndex { get; set; } = -1;
    public int NotePage { get; set; } = 0;
    public int NotePageCount { get; set; } = 0;

    public int InventoryPageFirstItemIdx { get; set; } = -1;
    public int InventoryPageLastItemIdx { get; set; } = -1;
    public int InventoryPageFirstNextItemIdx { get; set; } = -1;
    public int InventoryPageFirstPrevItemIdx { get; set; } = -1;

    // The same pointing/fixing principle as the inventory (see PinnedInventoryIndex), only the index is a
    // position in HeroDisplay.SortedSpells(hero), and not in the original hero.Spells.
    public int SelectedSpellIndex { get; set; } = -1;
    public int PinnedSpellIndex { get; set; } = -1;

    public int SpellsPageFirstItemIdx { get; set; } = -1;
    public int SpellsPageLastItemIdx { get; set; } = -1;
    public int SpellsPageFirstNextItemIdx { get; set; } = -1;
    public int SpellsPageFirstPrevItemIdx { get; set; } = -1;

    public string[]? SelectedImageLines { get; set; }
    public List<int>? SelectedImageColor { get; set; }
    // Spell Card: The spell color only colors the first line (title), the rest of the text
    // (school/fields/description) is in normal color. Items/entities are painted entirely (flag = false).
    public bool SelectedImageColorTitleOnly { get; set; }
    // Multi-color lines of the picture panel (monster card): segments per line, null color - main. They are
    // valid only as long as SelectedImageLines is the same array for which they were built (any other
    // SetSelected* replaces SelectedImageLines, and the old segments are automatically ignored).
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
    // Point redrawing of map cells (without recalculating visibility) - for preview select_target.
    public Action<IEnumerable<(int col, int row)>>? OnMapCellsRedraw { get; set; }
    // Active target selection (select_target): highlights cells on the map. null - no choice.
    public TargetSelectionView? TargetSelection { get; set; }
    // Cell under the cursor: distance, and in battle on the hero’s turn - path and consumption preview in the movement bar.
    public HoverPathInfo? HoverPath { get; set; }
    // Click-locked map element: its image returns when the mouse moves away from other cells.
    public CellEntity? PinnedMapEntity { get; set; }
    // How many dialogue messages were already on the screen: a new one arrived - the dialogue jumps to the last page.
    public int DialogSeenMessageCount { get; set; } = -1;
    public Func<string, (Action? draw, string? title)>? TabSwitchProvider { get; set; }

    // Poll action factories: called with current DialogDisplay (as object) to create fresh poll action on tab switch
    public Func<object, Action?>? CharacterPollActionFactory { get; set; }
    // Journal ([F3]): mouse poll (bookmarks, paging, hovering over an entry) and Tab-selection; JournalShown -
    // the journal is now on the screen (then F6–F8 are its bookmarks, not the character’s subtabs).
    public Func<object, Action?>? JournalPollActionFactory { get; set; }
    public Action? JournalOnTabKey { get; set; }
    public Action? JournalOnShiftTabKey { get; set; }
    public bool JournalShown { get; set; }
    // Show key hints “[F1]” (AppConfig.ShowKeyHints); disabled - they are replaced by spaces of the same length.
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

// Tooltip about the cell under the cursor (battle block line). ShowPath - battle and hero's move: show the
// path, otherwise only the distance. CostFt: null - not to reach; TargetSymbol - cursor on the creature (path
// to the next cell).
public sealed record HoverPathInfo(int DistanceFt, bool ShowPath, int? CostFt, string? TargetSymbol);

public class TargetSelectionView
{
    public HashSet<(int col, int row)> Selectable { get; } = [];
    // Beginning of the figure (hero for a cone/line): the cell itself is not painted, but the lines/corners
    // around it are considered highlighted - the figure visually grows from the hero.
    public (int col, int row)? Origin { get; set; }
    public HashSet<(int col, int row)> Preview { get; set; } = [];
    public HashSet<(int col, int row)> Selected { get; } = [];
}
