using System.Text.Json.Nodes;
using NaviDnD.Data;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

// Экран «МОИ ИГРЫ» в стиле анкеты: до Storage.MaxGames игр карточками 2×2. На карточке — портрет цветом
// героя, имя, раса/класс, HP, сюжетная арка, день/время суток и когда играли. Пустая карточка —
// «СВОБОДНО» (новая игра в это место). Стрелки — выбор, Enter — играть, Del — удалить (с подтверждением),
// мышь — наведение выбирает, клик подтверждает. Esc / «[Esc]МЕНЮ» в заголовке — назад.
public class GamesDisplay(WorldState settings, DisplayConfig display, string title, string? message = null)
{
    public enum Action { Back, Play, NewGame }

    private sealed record GameCard(int Slot, bool Exists, string Name = "", string Symbol = "", List<int>? Color = null,
        string? Image = null, string Race = "", string Class = "", string Hp = "", string Arc = "", string Time = "",
        DateTime Played = default);

    private const int CardWidth = 58, CardHeight = 13, Gap = 3, PortraitWidth = 16;

    private readonly List<MouseUiHelper.TitleTab> _titleTabs = MouseUiHelper.ComputeTitleTabs(title);
    private List<GameCard> _cards = [];
    private int _selected;
    private bool _confirmDelete;
    private string _message = message ?? "";
    private bool _escHovered;

    private int _width, _height, _x0, _bodyTop, _left, _top;
    private char[,] _chars = new char[0, 0];
    private List<int>?[,] _fg = new List<int>?[0, 0];

    private List<int> Fg => display.MainForeground;
    private List<int> Bright => ColorHelper.Pale(display.MainForeground, 0.75);
    private List<int> Dim => ColorHelper.Darker(display.MainForeground, 0.5);

    public (Action action, int slot) Show()
    {
        _cards = Enumerable.Range(1, Storage.MaxGames).Select(ReadCard).ToList();
        _selected = Math.Clamp(Storage.ActiveSlot - 1, 0, _cards.Count - 1);
        if (!_cards[_selected].Exists && _cards.FindIndex(c => c.Exists) is var first and >= 0) _selected = first;

        var border = new BorderDrawer(settings, display);
        _bodyTop = Console.CursorTop + 1;
        border.DrawSeparator();
        _width = display.InnerWidth(display.ViewCols(settings.Map));
        _height = Math.Max(24, Console.WindowHeight - _bodyTop - 1);
        _x0 = DisplayConfig.LeftMargin + 1;
        for (int r = 0; r < _height; r++) border.DrawContentLine(() => { });
        border.DrawBottomBorder();
        Redraw();

        while (true)
        {
            var (move, click, _) = ConsoleMouseReader.DrainMouseEvents();
            if (move is { } m)
            {
                int over = CardAt(m.x, m.y);
                bool overEsc = MouseUiHelper.GetHoveredTabKey(m.x, m.y, _titleTabs) == "Esc";
                ConsoleMouseReader.SetCursorShape(over >= 0 || overEsc);
                if (overEsc != _escHovered)
                {
                    _escHovered = overEsc;
                    MouseUiHelper.SetTabHighlight(title, _titleTabs, "Esc", overEsc, display);
                }
                if (over >= 0 && over != _selected && !_confirmDelete) { _selected = over; Redraw(); }
            }
            if (click is { } c)
            {
                if (MouseUiHelper.GetHoveredTabKey(c.x, c.y, _titleTabs) == "Esc") { Sound.PlayClick(); return Back(); }
                if (c.y < _bodyTop) { ConsoleMouseReader.StartWindowDrag(); continue; }
                int card = CardAt(c.x, c.y);
                if (card >= 0 && !_confirmDelete)
                {
                    _selected = card;
                    Sound.PlayClick();
                    return Open();
                }
                continue;
            }

            if (ConsoleMouseReader.TryReadKey() is not { } key) { Thread.Sleep(15); continue; }
            if (_confirmDelete)
            {
                // Подтверждение удаления: Enter — да, Esc/любая другая — нет.
                _confirmDelete = false;
                if (key.Key == ConsoleKey.Enter)
                {
                    Storage.DeleteSlot(_cards[_selected].Slot);
                    _cards[_selected] = new GameCard(_cards[_selected].Slot, false);
                    _message = L.T("Игра удалена");
                    Sound.PlayClick();
                }
                else _message = "";
                Redraw();
                continue;
            }

            _message = "";
            switch (key.Key)
            {
                case ConsoleKey.Escape: ConsoleMouseReader.SetCursorShape(false); return Back();
                case ConsoleKey.LeftArrow: MoveTo(_selected % 2 == 1 ? _selected - 1 : _selected); break;
                case ConsoleKey.RightArrow: MoveTo(_selected % 2 == 0 ? _selected + 1 : _selected); break;
                case ConsoleKey.UpArrow: MoveTo(_selected >= 2 ? _selected - 2 : _selected); break;
                case ConsoleKey.DownArrow: MoveTo(_selected < 2 ? _selected + 2 : _selected); break;
                case ConsoleKey.Tab: MoveTo((_selected + 1) % _cards.Count); break;
                case ConsoleKey.Enter: Sound.PlayClick(); return Open();
                case ConsoleKey.Delete or ConsoleKey.Backspace:
                    if (_cards[_selected].Exists)
                    {
                        _confirmDelete = true;
                        Sound.PlayClick();
                    }
                    break;
            }
            Redraw();
        }
    }

    private (Action, int) Back() { ConsoleMouseReader.SetCursorShape(false); return (Action.Back, 0); }

    private (Action, int) Open()
    {
        ConsoleMouseReader.SetCursorShape(false);
        var card = _cards[_selected];
        return (card.Exists ? Action.Play : Action.NewGame, card.Slot);
    }

    private void MoveTo(int index)
    {
        index = Math.Clamp(index, 0, _cards.Count - 1);
        if (index == _selected) return;
        _selected = index;
        Sound.PlayClick();
    }

    // ── Данные карточки — прямо из файла игры ─────────────────────────────────
    private static GameCard ReadCard(int slot)
    {
        string path = Storage.SlotPath(slot);
        if (!File.Exists(path)) return new GameCard(slot, false);
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path));
            var hero = root?["hero"];
            string Stat(string name) => hero?["stats"]?.AsArray()
                .FirstOrDefault(s => string.Equals((string?)s?["name"], name, StringComparison.OrdinalIgnoreCase))?["value"]?.ToString() ?? "";
            var color = hero?["color"]?.AsArray().Select(v => (int)v!).ToList();
            var time = root?["time"];
            string timeText = time == null ? "" : L.F("День {0}", time["day"]) + " · " + GameTime.PartOfDayText((string?)time["partOfDay"]);
            return new GameCard(slot, true,
                Name: (string?)hero?["name"] ?? L.T("Безымянный"),
                Symbol: (string?)hero?["symbol"] ?? "",
                Color: color is { Count: 3 } ? color : null,
                Image: (string?)hero?["image"],
                Race: Stat("Раса"),
                Class: Stat("Класс"),
                Hp: hero?["dead"]?.GetValue<bool>() == true ? L.T("— погиб") : (string?)hero?["hp"] ?? "",
                Arc: (string?)root?["narrative"]?["currentArc"] ?? "",
                Time: timeText,
                Played: File.GetLastWriteTime(path));
        }
        catch
        {
            return new GameCard(slot, true, Name: L.T("Повреждённое сохранение"), Played: File.GetLastWriteTime(path));
        }
    }

    // ── Мышь ──────────────────────────────────────────────────────────────────
    private (int x, int y) CardOrigin(int index) =>
        (_left + (index % 2) * (CardWidth + Gap), _top + (index / 2) * (CardHeight + 1));

    private int CardAt(short x, short y)
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            var (cx, cy) = CardOrigin(i);
            if (x >= _x0 + cx && x < _x0 + cx + CardWidth && y >= _bodyTop + cy && y < _bodyTop + cy + CardHeight) return i;
        }
        return -1;
    }

    // ── Отрисовка ─────────────────────────────────────────────────────────────
    private void Redraw()
    {
        _chars = new char[_height, _width];
        _fg = new List<int>?[_height, _width];
        for (int r = 0; r < _height; r++) for (int c = 0; c < _width; c++) _chars[r, c] = ' ';

        int total = CardWidth * 2 + Gap;
        int blockHeight = CardHeight * 2 + 1 + 3;
        _left = Math.Max(1, (_width - total) / 2);
        _top = Math.Max(0, (_height - blockHeight) / 2);

        for (int i = 0; i < _cards.Count; i++) DrawCard(i);

        string footer = _confirmDelete
            ? L.F("Удалить игру «{0}»?   [Enter]ДА   [Esc]НЕТ", _cards[_selected].Name)
            : _message.Length > 0 ? _message
            : _cards[_selected].Exists ? L.T("[←→↑↓]ВЫБОР   [Enter]ИГРАТЬ   [Del]УДАЛИТЬ") : L.T("[←→↑↓]ВЫБОР   [Enter]НОВАЯ ИГРА");
        CenterIn(_top + CardHeight * 2 + 2, _left, total, footer, _confirmDelete ? Bright : Dim);

        Console.CursorVisible = false;
        for (int r = 0; r < _height; r++) WriteRow(r);
    }

    private void DrawCard(int index)
    {
        var card = _cards[index];
        var (x, y) = CardOrigin(index);
        bool selected = index == _selected;
        bool active = card.Exists && card.Slot == Storage.ActiveSlot;
        string boxTitle = L.F("ИГРА {0}", card.Slot) + (active ? " · " + L.T("ПОСЛЕДНЯЯ") : "");
        DrawBox(y, x, CardWidth, CardHeight, boxTitle, selected ? Bright : MouseUiHelper.FrameColor(display), selected); // выбранная — светлой рамкой и стрелками

        if (!card.Exists)
        {
            CenterIn(y + CardHeight / 2 - 1, x, CardWidth, L.T("СВОБОДНО"), selected ? Bright : Dim);
            CenterIn(y + CardHeight / 2 + 1, x, CardWidth, L.T("новая игра"), Dim);
            return;
        }

        // Портрет цветом героя слева, сведения справа.
        var color = card.Color ?? Fg;
        var art = string.IsNullOrEmpty(card.Image) ? null : SvgToBrailleConverter.Convert(card.Image, "", PortraitWidth);
        if (art != null)
            for (int i = 0; i < art.Length && i < CardHeight - 4; i++) Put(y + 2 + i, x + 3, art[i], color, skipBlank: true);
        else
            Put(y + CardHeight / 2, x + 3 + PortraitWidth / 2 - 1, card.Symbol, color);

        int tx = x + 3 + PortraitWidth + 3, tw = CardWidth - (tx - x) - 3, ty = y + 2;
        Put(ty, tx, Fit(card.Name, tw), color);
        string who = string.Join(" · ", new[] { card.Race, card.Class }.Where(s => s.Length > 0));
        if (who.Length > 0) Put(ty + 1, tx, Fit(who, tw), Fg);
        if (card.Hp.Length > 0) Put(ty + 2, tx, Fit(L.T("ХП ") + card.Hp, tw), Fg);

        if (card.Arc.Length > 0)
        {
            var arc = TextWrapper.WrapText(card.Arc, tw);
            for (int i = 0; i < arc.Count && i < 3; i++)
                Put(ty + 4 + i, tx, Fit(i == 2 && arc.Count > 3 ? arc[i] + "…" : arc[i], tw), Dim);
        }
        if (card.Time.Length > 0) Put(y + CardHeight - 3, tx, Fit(card.Time, tw), Dim);
        Put(y + CardHeight - 2, tx, Fit(L.F("Играли {0:dd.MM HH:mm}", card.Played), tw), Dim);
    }

    // Заголовок в разрыве верхней линии: «╭──── ИГРА 1 ────╮», у выбранной вместо части рамки стрелки —
    // «╭─ → ИГРА 1 ← ─╮» (текст на том же месте, не прыгает).
    private void DrawBox(int y, int x, int w, int h, string boxTitle, List<int> line, bool arrows = false)
    {
        Put(y, x, arrows ? "╭─ →" : "╭───", line);
        Put(y, x + 5, boxTitle, Bright);
        Put(y, x + 6 + boxTitle.Length, (arrows ? "← " : "──") + new string('─', Math.Max(0, w - boxTitle.Length - 9)) + "╮", line);
        for (int r = 1; r < h - 1; r++)
        {
            Put(y + r, x, "│", line);
            Put(y + r, x + w - 1, "│", line);
        }
        Put(y + h - 1, x, "╰" + new string('─', w - 2) + "╯", line);
    }

    private void CenterIn(int row, int left, int width, string text, List<int> color) =>
        Put(row, left + Math.Max(0, (width - text.Length) / 2), text, color);

    private static string Fit(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";

    private void Put(int row, int x, string text, List<int> color, bool skipBlank = false)
    {
        if (row < 0 || row >= _height) return;
        for (int i = 0; i < text.Length && x + i < _width; i++)
        {
            if (x + i < 0) continue;
            if (skipBlank && text[i] == '⠀') continue;
            _chars[row, x + i] = text[i];
            _fg[row, x + i] = color;
        }
    }

    private void WriteRow(int row)
    {
        Console.SetCursorPosition(_x0, _bodyTop + row);
        int c = 0;
        while (c < _width)
        {
            var fg = _fg[row, c];
            int start = c;
            while (c < _width && ReferenceEquals(_fg[row, c], fg)) c++;
            var run = new string(Enumerable.Range(start, c - start).Select(i => _chars[row, i]).ToArray());
            ColorHelper.WriteColored(run, fgColor: fg ?? display.MainForeground, bgColor: display.MainBackground);
        }
    }
}
