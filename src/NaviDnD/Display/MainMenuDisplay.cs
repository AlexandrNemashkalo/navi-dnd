using NaviDnD.Data;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

// Same frame, theme palette and selection arrows as the game. No decorative image pipeline:
// the title is a small pixel font; only the selected row gently changes brightness.
internal sealed class MainMenuDisplay(WorldState settings, DisplayConfig display)
{
    public enum Choice { Continue, NewGame, Games, Settings, Updates, Exit }

    private sealed record Item(string Name, Choice? Action);

    private static readonly string[] Logo = BuildLogo();
    private static string[] BuildLogo()
    {
        string[] letters = ["10001/11001/10101/10011/10001", "01110/10001/11111/10001/10001",
            "10001/10001/10001/01010/00100", "111/010/010/010/111", "000/000/000/000/000",
            "11110/10001/10001/10001/11110", "10001/11001/10101/10011/10001", "11110/10001/10001/10001/11110"];
        return Enumerable.Range(0, 5).Select(row => string.Join("  ", letters.Select(letter =>
            string.Concat(letter.Split('/')[row].Select(c => c == '1' ? "██" : "  "))))).ToArray();
    }

    private static string Title => L.T(" MAIN MENU");
    private const int HeaderRows = 3; // top frame, header, divider

    private List<int> Fg => display.MainForeground;
    private List<int> Bright => ColorHelper.Pale(display.MainForeground, 0.75);
    private List<int> Dim => ColorHelper.Darker(display.MainForeground, 0.5);

    private int _width, _height, _x0;
    private char[,] _chars = new char[0, 0];
    private List<int>?[,] _colors = new List<int>?[0, 0];
    private List<int>?[,] _backgrounds = new List<int>?[0, 0];
    private readonly List<(int row, int x, int w)> _itemPos = [];
    private List<Item> _items = [];
    private int _selected;
    private double _introOpacity = 1;
    private double _pulse;

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
        bool intro = WindowsAlacritty.AwaitingFirstFrame
            || (OperatingSystem.IsMacOS() && MacApp.TakeStartupIntro());
        _introOpacity = intro ? 0 : 1;
        DrawAll();

        UnixTerminal.Current?.Flush();
        WindowsAlacritty.RevealFirstFrame();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long nextFrame = 0;
        while (true)
        {
            if (clock.ElapsedMilliseconds >= nextFrame)
            {
                nextFrame = clock.ElapsedMilliseconds + 50;
                _pulse = (1 - Math.Cos(clock.Elapsed.TotalSeconds * Math.Tau / 4)) / 2;
                RenderItems();
                if (_introOpacity < 1)
                {
                    double t = Math.Clamp(clock.Elapsed.TotalMilliseconds / 450, 0, 1);
                    _introOpacity = t * t * (3 - 2 * t);
                    DrawAll();
                }
                else WriteRow(_itemPos[_selected].row, positioned: true);
                UnixTerminal.Current?.Flush();
            }
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
            if (_introOpacity < 1) { _introOpacity = 1; DrawAll(); }

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
        _backgrounds = new List<int>?[_height, _width];
        for (int r = 0; r < _height; r++) for (int c = 0; c < _width; c++) _chars[r, c] = ' ';

        // Center: logo, label, points, tooltip.
        var logo = _width >= Logo[0].Length + 8 && _height >= 30 ? Logo : ["NAVI DND"];
        int spacing = _height >= 30 ? 2 : 1;
        int blockHeight = logo.Length + 2 + 3 + _items.Count * spacing + 1;
        int y = Math.Max(1, (_height - blockHeight) / 2);
        for (int i = 0; i < logo.Length; i++)
            Center(y + i, logo[i], ColorHelper.MixWith(Bright, Fg, i / (double)Math.Max(1, logo.Length - 1) * 0.55));
        y += logo.Length + 1;
        Center(y, L.T("AI DUNGEON MASTER"), Dim);
        Center(y + 2, new string('─', Math.Min(42, _width - 4)), ColorHelper.MixWith(display.MainBackground, Fg, 0.3));
        y += 3;
        _itemPos.Clear();
        foreach (var _ in _items)
        {
            _itemPos.Add((y, 0, 0));
            y += spacing;
        }
        RenderItems();
        Center(Math.Min(_height - 3, y), new string('─', Math.Min(42, _width - 4)), ColorHelper.MixWith(display.MainBackground, Fg, 0.3));
        Center(Math.Min(_height - 1, y + 2), L.T("[↑↓]SELECT     [Enter]CONFIRM"), Dim);
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
            { _chars[row, c] = ' '; _colors[row, c] = null; _backgrounds[row, c] = null; }

            bool selected = i == _selected && item.Action != null;
            string text = selected ? "→ " + item.Name + " ←" : "  " + item.Name + "  ";
            var color = item.Action == null ? ColorHelper.MixWith(display.MainBackground, Fg, 0.28)
                : selected ? ColorHelper.MixWith(Fg, Bright, 0.6 + _pulse * 0.2) : Fg;
            int width = Math.Min(_width, Math.Max(42, text.Length + 4)), x = (_width - width) / 2;
            if (selected)
            {
                var bg = ColorHelper.MixWith(display.MainBackground, Fg, 0.06 + _pulse * 0.02);
                for (int c = x; c < x + width; c++) _backgrounds[row, c] = bg;
            }
            Center(row, text, color);
            _itemPos[i] = (row, x, width);
        }
    }

    private int Center(int row, string text, List<int> color)
    {
        int x = Math.Max(0, (_width - text.Length) / 2);
        Put(row, x, text, color);
        return x;
    }

    private void Put(int row, int x, string text, List<int> color)
    {
        if (row < 0 || row >= _height) return;
        for (int i = 0; i < text.Length && x + i < _width; i++)
        {
            if (x + i < 0) continue;
            char ch = text[i];
            _chars[row, x + i] = ch;
            _colors[row, x + i] = color;
        }
    }

    // ── Output ──────────────────────────────── ────────────────────────────────
    private void DrawAll()
    {
        if (_introOpacity < 1)
        {
            // Commit each brightness step as a complete terminal frame, not row by row.
            Console.Write("\x1b[?2026h");
            try { ColorHelper.WithForegroundOpacity(display.MainBackground, _introOpacity, DrawFrame); }
            finally { Console.Write("\x1b[?2026l"); }
        }
        else DrawFrame();
    }

    private void DrawFrame()
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
        int c = 0;
        while (c < _width)
        {
            var color = _colors[row, c];
            var bg = _backgrounds[row, c] ?? display.MainBackground;
            int start = c;
            while (c < _width && ReferenceEquals(_colors[row, c], color)
                && ReferenceEquals(_backgrounds[row, c] ?? display.MainBackground, bg)) c++;
            var run = new string(Enumerable.Range(start, c - start).Select(i => _chars[row, i]).ToArray());
            ColorHelper.WriteColored(run, fgColor: color ?? display.MainForeground, bgColor: bg);
        }
    }
}
