using NaviDnD.Data;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

// The main menu is in the style of other screens: a frame, a title bar “MAIN MENU” and a separator (a window is
// dragged behind them, as everywhere else), below in the center is a logo and “tabbed” items: “[F1]CONTINUE”,
// selected - “→ CONTINUE ←” (as the active tab in the header). The colors are only game colors (MainForeground
// and its light/dark shades), on the sides there are braille pictures in muted colors. There is no input line
// or hotkeys: ↑↓ + Enter, mouse (hover selects, click confirms).
internal sealed class MainMenuDisplay(WorldState settings, DisplayConfig display)
{
    public enum Choice { Continue, NewGame, Games, Settings, Updates, Exit }

    private sealed record Item(string Name, Choice? Action);

    // Logo “NAVIDND” in font ANSI Shadow.
    private static readonly string[] Logo =
    [
        "███╗   ██╗ █████╗ ██╗   ██╗██╗██████╗ ███╗   ██╗██████╗ ",
        "████╗  ██║██╔══██╗██║   ██║██║██╔══██╗████╗  ██║██╔══██╗",
        "██╔██╗ ██║███████║██║   ██║██║██║  ██║██╔██╗ ██║██║  ██║",
        "██║╚██╗██║██╔══██║╚██╗ ██╔╝██║██║  ██║██║╚██╗██║██║  ██║",
        "██║ ╚████║██║  ██║ ╚████╔╝ ██║██████╔╝██║ ╚████║██████╔╝",
        "╚═╝  ╚═══╝╚═╝  ╚═╝  ╚═══╝  ╚═╝╚═════╝ ╚═╝  ╚═══╝╚═════╝ ",
    ];

    private static string Title => L.T(" MAIN MENU");
    private const int HeaderRows = 3; // top frame, header, divider

    private List<int> Fg => display.MainForeground;
    private List<int> Bright => ColorHelper.Pale(display.MainForeground, 0.75);
    private List<int> Dim => ColorHelper.Darker(display.MainForeground, 0.5);

    private int _width, _height, _x0;
    private char[,] _chars = new char[0, 0];
    private List<int>?[,] _colors = new List<int>?[0, 0];
    private readonly List<(int row, int x, int w)> _itemPos = [];
    private List<Item> _items = [];
    private int _selected;

    internal void DrawForUpdate()
    {
        _items =
        [
            new(L.T("CONTINUE"), null), new(L.T("NEW GAME"), null), new(L.T("MY GAMES"), null),
            new(L.T("SETTINGS"), null), new(L.T("UPDATES"), Choice.Updates), new(L.T("EXIT"), null),
        ];
        _selected = 4;
        Layout();
        DrawAll();
    }

    public Choice Show(bool hasSave)
    {
        _items =
        [
            new(L.T("CONTINUE"), hasSave ? Choice.Continue : null),
            new(L.T("NEW GAME"), Choice.NewGame),
            new(L.T("MY GAMES"), Choice.Games),
            new(L.T("SETTINGS"), Choice.Settings),
            new(GameUpdates.MenuLabel, Choice.Updates),
            new(L.T("EXIT"), Choice.Exit),
        ];
        _selected = hasSave ? 0 : 1;

        Term.CursorVisible = false;
        Layout();
        DrawAll();

        while (true)
        {
            string updateLabel = GameUpdates.MenuLabel;
            if (_items[4].Name != updateLabel)
            {
                _items[4] = new(updateLabel, Choice.Updates);
                DrawAll();
                RenderItems();
                foreach (var (row, _, _) in _itemPos) WriteRow(row, positioned: true);
            }
            var (move, click, _) = ConsoleMouseReader.DrainMouseEvents();
            if (move is { } m)
            {
                int hovered = ItemAt(m.x, m.y);
                ConsoleMouseReader.SetCursorShape(hovered >= 0 && _items[hovered].Action != null);
                if (hovered >= 0 && hovered != _selected) Select(hovered, sound: false); // hover - no click
            }
            if (click is { } c)
            {
                int clicked = ItemAt(c.x, c.y);
                if (clicked >= 0 && _items[clicked].Action is { } clickAction)
                {
                    Sound.PlayClick();
                    ConsoleMouseReader.SetCursorShape(false);
                    return clickAction;
                }
                if (c.y is >= 0 and < HeaderRows) ConsoleMouseReader.StartWindowDrag(); // as on other screens
            }

            ConsoleKey? key = ConsoleMouseReader.TryReadKey()?.Key;
            if (key == null) { Thread.Sleep(15); continue; }

            switch (key)
            {
                case ConsoleKey.UpArrow: Step(-1); break;
                case ConsoleKey.DownArrow or ConsoleKey.Tab: Step(1); break;
                case ConsoleKey.Enter:
                    if (_items[_selected].Action is { } action) { Sound.PlayClick(); return action; }
                    break;
            }
        }
    }

    // Next available option (“CONTINUE” without saving and “SETTINGS” is skipped for now).
    private void Step(int dir)
    {
        int i = _selected;
        for (int n = 0; n < _items.Count; n++)
        {
            i = (i + dir + _items.Count) % _items.Count;
            if (_items[i].Action != null) { Select(i); return; }
        }
    }

    // sound: click when selecting with arrows; when hovering with the mouse - no sound.
    private void Select(int index, bool sound = true)
    {
        if (_items[index].Action == null) return;
        _selected = index;
        if (sound) Sound.PlayClick();
        RenderItems();
        foreach (var (row, _, _) in _itemPos) WriteRow(row, positioned: true);
    }

    private int ItemAt(short x, short y)
    {
        for (int i = 0; i < _itemPos.Count; i++)
        {
            var (row, ix, w) = _itemPos[i];
            if (y == HeaderRows + row && x >= _x0 + ix && x < _x0 + ix + w) return i;
        }
        return -1;
    }

    // ── Layout ────────────────────────────── ──────────────────────────────
    private void Layout()
    {
        _width = display.InnerWidth(display.ViewCols(settings.Map));
        _height = Math.Max(20, Term.WindowHeight - HeaderRows - 1); // bottom frame - in the last line of the window
        _x0 = DisplayConfig.LeftMargin + 1;
        _chars = new char[_height, _width];
        _colors = new List<int>?[_height, _width];
        for (int r = 0; r < _height; r++) for (int c = 0; c < _width; c++) _chars[r, c] = ' ';

        // Center: logo, label, points, tooltip.
        int blockHeight = Logo.Length + 2 + 3 + _items.Count * 2 + 1;
        int y = Math.Max(1, (_height - blockHeight) / 2);
        for (int i = 0; i < Logo.Length; i++)
            Center(y + i, Logo[i], ColorHelper.MixWith(Bright, Dim, i / (double)(Logo.Length - 1) * 0.6));
        y += Logo.Length + 1;
        Center(y, L.T("AI DUNGEON MASTER"), Dim);
        y += 3;
        _itemPos.Clear();
        foreach (var _ in _items)
        {
            _itemPos.Add((y, 0, 0));
            y += 2;
        }
        RenderItems();
        Center(Math.Min(_height - 2, y), L.T("[↑↓]SELECT     [Enter]CONFIRM"), Dim);

        // On the sides are pictures in muted game colors: a castle on the left, a dragon on the right.
        int sideWidth = Math.Min(40, (_width - Logo[0].Length) / 2 - 6);
        if (sideWidth >= 16)
        {
            var artColor = ColorHelper.Darker(display.MainForeground, 0.55);
            PlaceArt("lorc/dragon-head", sideWidth, left: true, artColor);
            PlaceArt("lorc/castle", sideWidth, left: false, artColor);
        }
    }

    private void PlaceArt(string icon, int widthChars, bool left, List<int> color)
    {
        var art = SvgToBrailleConverter.Convert(icon, "", widthChars);
        if (art == null) return;
        int y0 = Math.Max(1, (_height - art.Length) / 2);
        int x0 = left ? 3 : _width - 3 - widthChars;
        for (int r = 0; r < art.Length && y0 + r < _height - 1; r++)
            Put(y0 + r, x0, art[r], color, skipBlankBraille: true);
    }

    // Centered items: “NEW GAME” / “→ NEW GAME ←” (same length - when selected, the item does not move, the
    // arrows appear on the sides); inaccessible - dark.
    private void RenderItems()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            int row = _itemPos[i].row;
            for (int c = 0; c < _width; c++)
                if (Math.Abs(c - _width / 2) < 16) { _chars[row, c] = ' '; _colors[row, c] = null; }

            bool selected = i == _selected && item.Action != null;
            string text = selected ? "→ " + item.Name + " ←" : "  " + item.Name + "  ";
            var color = item.Action == null ? Dim : selected ? Bright : Fg;
            int x = Center(row, text, color);
            _itemPos[i] = (row, x, text.Length);
        }
    }

    private int Center(int row, string text, List<int> color)
    {
        int x = Math.Max(0, (_width - text.Length) / 2);
        Put(row, x, text, color);
        return x;
    }

    private void Put(int row, int x, string text, List<int> color, bool skipBlankBraille = false)
    {
        if (row < 0 || row >= _height) return;
        for (int i = 0; i < text.Length && x + i < _width; i++)
        {
            if (x + i < 0) continue;
            char ch = text[i];
            if (skipBlankBraille && ch == '⠀') continue;
            _chars[row, x + i] = ch;
            _colors[row, x + i] = color;
        }
    }

    // ── Output ──────────────────────────────── ────────────────────────────────
    private void DrawAll()
    {
        var border = new BorderDrawer(settings, display);
        Term.SetCursorPosition(0, 0);
        border.DrawTopBorder();
        border.DrawContentLine(() => MouseUiHelper.WriteColoredTitle(Title, display));
        border.DrawSeparator();
        for (int r = 0; r < _height; r++)
        {
            int row = r;
            border.DrawContentLine(() => WriteRow(row, positioned: false));
        }
        border.DrawBottomBorder();
    }

    private void WriteRow(int row, bool positioned)
    {
        if (positioned) Term.SetCursorPosition(_x0, HeaderRows + row);
        var bg = display.MainBackground;
        int c = 0;
        while (c < _width)
        {
            var color = _colors[row, c];
            int start = c;
            while (c < _width && ReferenceEquals(_colors[row, c], color)) c++;
            var run = new string(Enumerable.Range(start, c - start).Select(i => _chars[row, i]).ToArray());
            ColorHelper.WriteColored(run, fgColor: color ?? display.MainForeground, bgColor: bg);
        }
    }
}
