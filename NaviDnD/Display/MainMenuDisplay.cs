using NaviDnD.Data;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

// Главное меню в стиле остальных экранов: рамка, строка заголовка «ГЛАВНОЕ МЕНЮ» и разделитель
// (за них, как и везде, тащится окно), ниже по центру — логотип и пункты-«вкладки»: «[F1]ПРОДОЛЖИТЬ»,
// выбранный — «→ ПРОДОЛЖИТЬ ←» (как активная вкладка в заголовке). Цвета — только игровые
// (MainForeground и его светлый/тёмный оттенки), по бокам — браиль-картинки приглушённым цветом.
// Строки ввода и горячих клавиш нет: ↑↓ + Enter, мышь (наведение выбирает, клик подтверждает).
internal sealed class MainMenuDisplay(WorldState settings, DisplayConfig display)
{
    public enum Choice { Continue, NewGame, Games, Settings, Updates, Exit }

    private sealed record Item(string Name, Choice? Action);

    // Логотип «NAVIDND» шрифтом ANSI Shadow.
    private static readonly string[] Logo =
    [
        "███╗   ██╗ █████╗ ██╗   ██╗██╗██████╗ ███╗   ██╗██████╗ ",
        "████╗  ██║██╔══██╗██║   ██║██║██╔══██╗████╗  ██║██╔══██╗",
        "██╔██╗ ██║███████║██║   ██║██║██║  ██║██╔██╗ ██║██║  ██║",
        "██║╚██╗██║██╔══██║╚██╗ ██╔╝██║██║  ██║██║╚██╗██║██║  ██║",
        "██║ ╚████║██║  ██║ ╚████╔╝ ██║██████╔╝██║ ╚████║██████╔╝",
        "╚═╝  ╚═══╝╚═╝  ╚═╝  ╚═══╝  ╚═╝╚═════╝ ╚═╝  ╚═══╝╚═════╝ ",
    ];

    private const string Title = " ГЛАВНОЕ МЕНЮ";
    private const int HeaderRows = 3; // верхняя рамка, заголовок, разделитель

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
            new("ПРОДОЛЖИТЬ", null), new("НОВАЯ ИГРА", null), new("МОИ ИГРЫ", null),
            new("НАСТРОЙКИ", null), new("ОБНОВЛЕНИЯ", Choice.Updates), new("ВЫХОД", null),
        ];
        _selected = 4;
        Layout();
        DrawAll();
    }

    public Choice Show(bool hasSave)
    {
        _items =
        [
            new("ПРОДОЛЖИТЬ", hasSave ? Choice.Continue : null),
            new("НОВАЯ ИГРА", Choice.NewGame),
            new("МОИ ИГРЫ", Choice.Games),
            new("НАСТРОЙКИ", Choice.Settings),
            new(GameUpdates.MenuLabel, Choice.Updates),
            new("ВЫХОД", Choice.Exit),
        ];
        _selected = hasSave ? 0 : 1;

        Console.CursorVisible = false;
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
                if (hovered >= 0 && hovered != _selected) Select(hovered, sound: false); // наведение — без щелчка
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
                if (c.y is >= 0 and < HeaderRows) ConsoleMouseReader.StartWindowDrag(); // как на остальных экранах
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

    // Следующий доступный пункт («ПРОДОЛЖИТЬ» без сохранения и «НАСТРОЙКИ» пока пропускаются).
    private void Step(int dir)
    {
        int i = _selected;
        for (int n = 0; n < _items.Count; n++)
        {
            i = (i + dir + _items.Count) % _items.Count;
            if (_items[i].Action != null) { Select(i); return; }
        }
    }

    // sound: щелчок при выборе стрелками; при наведении мышью — без звука.
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

    // ── Раскладка ────────────────────────────────────────────────────────────
    private void Layout()
    {
        _width = display.InnerWidth(display.ViewCols(settings.Map));
        _height = Math.Max(20, Console.WindowHeight - HeaderRows - 1); // нижняя рамка — в последней строке окна
        _x0 = DisplayConfig.LeftMargin + 1;
        _chars = new char[_height, _width];
        _colors = new List<int>?[_height, _width];
        for (int r = 0; r < _height; r++) for (int c = 0; c < _width; c++) _chars[r, c] = ' ';

        // Центр: логотип, подпись, пункты, подсказка.
        int blockHeight = Logo.Length + 2 + 3 + _items.Count * 2 + 1;
        int y = Math.Max(1, (_height - blockHeight) / 2);
        for (int i = 0; i < Logo.Length; i++)
            Center(y + i, Logo[i], ColorHelper.MixWith(Bright, Dim, i / (double)(Logo.Length - 1) * 0.6));
        y += Logo.Length + 1;
        Center(y, "ИИ-МАСТЕР ПОДЗЕМЕЛИЙ", Dim);
        y += 3;
        _itemPos.Clear();
        foreach (var _ in _items)
        {
            _itemPos.Add((y, 0, 0));
            y += 2;
        }
        RenderItems();
        Center(Math.Min(_height - 2, y), "[↑↓]ВЫБОР     [Enter]ПОДТВЕРДИТЬ", Dim);

        // По бокам — картинки приглушённым цветом игры: замок слева, дракон справа.
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

    // Пункты по центру: «  НОВАЯ ИГРА  » / «→ НОВАЯ ИГРА ←» (одной длины — при выборе пункт не
    // сдвигается, стрелки встают по бокам); недоступный — тёмным.
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

    // ── Вывод ────────────────────────────────────────────────────────────────
    private void DrawAll()
    {
        var border = new BorderDrawer(settings, display);
        Console.SetCursorPosition(0, 0);
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
        if (positioned) Console.SetCursorPosition(_x0, HeaderRows + row);
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
