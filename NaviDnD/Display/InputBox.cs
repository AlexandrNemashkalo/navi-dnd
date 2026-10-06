using NaviDnD.Data;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

public class InputBox
{
    private readonly BorderDrawer _borderDrawer;
    private readonly DisplayConfig _display;
    private readonly int _innerWidth;
    private readonly List<int> _mainBg;
    private readonly List<int> _mainFg;
    private readonly bool _arrowKeysMovement;

    private int _textWidth;
    private string _prompt = " Ввод: ";
    private int _promptLength;

    private int _maxLine = 1;
    private bool _multiline = false;
    private int _maxLength = int.MaxValue;

    private bool _drawBottom = true;
    private bool _needClear = true;

    private Action<int> _bottomAction;

    // Общий для всех полей ввода: поле пересоздаётся на каждом ходу, а отложенное нажатие должно дожить до следующего.
    private static ConsoleKeyInfo? _bufferedKey;

    // Автоповтор F-клавиш: пока клавишу держат, повторы идут каждые ~30 мс — их отсекаем (окно меньше, чем
    // успевает человек между двумя нажатиями: быстрое двойное нажатие, например чтобы вернуть фильтр, проходит).
    private static ConsoleKey? _lastFKey;
    private static DateTime _lastFKeyAt;
    private const int FKeyRepeatMs = 110;

    // true — это автоповтор зажатой F-клавиши (тот же Fn, что и только что). Общий для поля ввода и
    // переключения вкладок во время ответа мастера (DialogDisplay.SwitchTab).
    public static bool IsFKeyRepeat(ConsoleKey key)
    {
        if (key is < ConsoleKey.F1 or > ConsoleKey.F12) return false;
        var now = DateTime.UtcNow;
        bool repeat = _lastFKey == key && (now - _lastFKeyAt).TotalMilliseconds < FKeyRepeatMs;
        _lastFKey = key;
        _lastFKeyAt = now;
        return repeat;
    }

    public void SetNeedClear(bool needClear) => _needClear = needClear;
    public void SetDrawBottom(bool drawBottom) => _drawBottom = drawBottom;
    public void SetMultiline(bool multiline) => _multiline = multiline;
    public void SetMaxLength(int maxLength) => _maxLength = maxLength;
    public void SetBottomAction(Action<int> action) => _bottomAction = action;

    public void SetPrompt(string prompt)
    {
        _prompt = prompt;
        _promptLength = prompt.Length;
        _textWidth = _innerWidth - _promptLength;
    }

    public InputBox(WorldState worldState, DisplayConfig display)
    {
        _display = display;
        _borderDrawer = new BorderDrawer(worldState, display);
        _innerWidth = display.InnerWidth(display.ViewCols(worldState.Map));
        _mainBg = display.MainBackground;
        _mainFg = display.MainForeground;
        _arrowKeysMovement = display.ArrowKeysMovement;

        _promptLength = _prompt.Length;
        _textWidth = _innerWidth - _promptLength;
    }

    public string GetInput()
    {
        List<string> lines = RestoreLines(_display?.PendingInputText);
        int cursorX = lines[^1].Length, cursorY = lines.Count - 1;
        int startTop = Console.CursorTop;
        int viewOffset = cursorX > _textWidth ? cursorX - _textWidth : 0;

        Draw(lines, cursorX, cursorY, startTop, viewOffset);

        while (true)
        {
            ConsoleKeyInfo key;
            if (_bufferedKey.HasValue) { key = _bufferedKey.Value; _bufferedKey = null; }
            else key = ReadKeyWithPolling();

            // Зажатая F-клавиша (автоповтор) — только первое нажатие: повторы того же Fn подряд, пока клавишу
            // держат, игнорируются (иначе экран перерисовывался бы непрерывно).
            if (IsFKeyRepeat(key.Key)) continue;

            bool hasText = lines.Any(l => l.Length > 0);

            // Ctrl+«+»/«−» на карте — масштаб карты мира (MapScreenLoop: только на экране Мира).
            if (_arrowKeysMovement && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                if (key.Key is ConsoleKey.OemPlus or ConsoleKey.Add) return "ZoomIn";
                if (key.Key is ConsoleKey.OemMinus or ConsoleKey.Subtract) return "ZoomOut";
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    if (_needClear)
                        Clear(startTop, _maxLine);
                    if (_display != null) _display.PendingInputText = "";
                    return string.Join(" ", lines);

                // Журнал: [Del] при пустом вводе — удалить выбранную заметку.
                case ConsoleKey.Delete when !hasText && _display != null && _display.JournalShown:
                    return "Delete";

                case ConsoleKey.Backspace:
                    if (cursorX > 0)
                    {
                        lines[cursorY] = lines[cursorY].Remove(cursorX - 1, 1);
                        cursorX--;
                    }
                    else if (cursorY > 0)
                    {
                        int prevLength = lines[cursorY - 1].Length;
                        lines[cursorY - 1] += lines[cursorY];
                        lines.RemoveAt(cursorY);
                        cursorY--;
                        cursorX = prevLength;
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if ((key.Modifiers & ConsoleModifiers.Alt) != 0)
                        return "Left";
                    // Ctrl+стрелки на карте — сдвиг камеры, герой не двигается.
                    if (_arrowKeysMovement && (key.Modifiers & ConsoleModifiers.Control) != 0)
                        return "PanLeft";
                    // На карте стрелки — движение: закреплённая карточка с экрана персонажа их не перехватывает.
                    if (!_arrowKeysMovement && !hasText && _display.NotePageCount > 1 && (_display.PinnedInventoryIndex >= 0 || _display.PinnedSpellIndex >= 0))
                        return "NoteLeft";
                    // Журнал: ←→ при пустом вводе — листать книгу.
                    if (!hasText && _display != null && _display.JournalShown) return "Left";
                    if (_arrowKeysMovement && !hasText)
                        return TryReadChordArrow(80, ConsoleKey.LeftArrow) switch {
                            ConsoleKey.UpArrow   => "MoveNorthWest",
                            ConsoleKey.DownArrow => "MoveSouthWest",
                            _                    => "MoveWest"
                        };
                    if (cursorX > 0) cursorX--;
                    else if (cursorY > 0) { cursorY--; cursorX = lines[cursorY].Length; }
                    break;

                case ConsoleKey.RightArrow:
                    if ((key.Modifiers & ConsoleModifiers.Alt) != 0)
                        return "Right";
                    if (_arrowKeysMovement && (key.Modifiers & ConsoleModifiers.Control) != 0)
                        return "PanRight";
                    // На карте стрелки — движение: закреплённая карточка с экрана персонажа их не перехватывает.
                    if (!_arrowKeysMovement && !hasText && _display.NotePageCount > 1 && (_display.PinnedInventoryIndex >= 0 || _display.PinnedSpellIndex >= 0))
                        return "NoteRight";
                    // Журнал: ←→ при пустом вводе — листать книгу.
                    if (!hasText && _display != null && _display.JournalShown) return "Right";
                    if (_arrowKeysMovement && !hasText)
                        return TryReadChordArrow(80, ConsoleKey.RightArrow) switch {
                            ConsoleKey.UpArrow   => "MoveNorthEast",
                            ConsoleKey.DownArrow => "MoveSouthEast",
                            _                    => "MoveEast"
                        };
                    if (cursorX < lines[cursorY].Length) cursorX++;
                    else if (cursorY < lines.Count - 1) { cursorY++; cursorX = 0; }
                    break;

                case ConsoleKey.UpArrow:
                    if ((key.Modifiers & ConsoleModifiers.Alt) != 0)
                        return "Up";
                    if (_arrowKeysMovement && (key.Modifiers & ConsoleModifiers.Control) != 0)
                        return "PanUp";
                    if (_arrowKeysMovement)
                        return TryReadChordArrow(80, ConsoleKey.UpArrow) switch {
                            ConsoleKey.RightArrow => "MoveNorthEast",
                            ConsoleKey.LeftArrow  => "MoveNorthWest",
                            _                     => "MoveNorth"
                        };
                    if (cursorY > 0) { cursorY--; cursorX = Math.Min(cursorX, lines[cursorY].Length); }
                    else if (!hasText) return "Up";
                    break;

                case ConsoleKey.DownArrow:
                    if ((key.Modifiers & ConsoleModifiers.Alt) != 0)
                        return "Down";
                    if (_arrowKeysMovement && (key.Modifiers & ConsoleModifiers.Control) != 0)
                        return "PanDown";
                    if (_arrowKeysMovement)
                        return TryReadChordArrow(80, ConsoleKey.DownArrow) switch {
                            ConsoleKey.RightArrow => "MoveSouthEast",
                            ConsoleKey.LeftArrow  => "MoveSouthWest",
                            _                     => "MoveSouth"
                        };
                    if (cursorY < lines.Count - 1) { cursorY++; cursorX = Math.Min(cursorX, lines[cursorY].Length); }
                    else if (!hasText) return "Down";
                    break;

                case ConsoleKey.NumPad8: if (_arrowKeysMovement) return "MoveNorth"; break;
                case ConsoleKey.NumPad2: if (_arrowKeysMovement) return "MoveSouth"; break;
                case ConsoleKey.NumPad4: if (_arrowKeysMovement) return "MoveWest"; break;
                case ConsoleKey.NumPad6: if (_arrowKeysMovement) return "MoveEast"; break;
                case ConsoleKey.NumPad7: if (_arrowKeysMovement) return "MoveNorthWest"; break;
                case ConsoleKey.NumPad9: if (_arrowKeysMovement) return "MoveNorthEast"; break;
                case ConsoleKey.NumPad1: if (_arrowKeysMovement) return "MoveSouthWest"; break;
                case ConsoleKey.NumPad3: if (_arrowKeysMovement) return "MoveSouthEast"; break;
                // NumLock OFF: diagonal numpad sends navigation keys
                case ConsoleKey.Home:     if (_arrowKeysMovement) return "MoveNorthWest"; break;
                case ConsoleKey.PageUp:   if (_arrowKeysMovement) return "MoveNorthEast"; break;
                case ConsoleKey.End:      if (_arrowKeysMovement) return "MoveSouthWest"; break;
                case ConsoleKey.PageDown: if (_arrowKeysMovement) return "MoveSouthEast"; break;

                case ConsoleKey.Tab:
                    Sound.PlayClick();
                    return key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? "ShiftTab" : "Tab";

                case ConsoleKey.Escape:
                    if (_needClear)
                        Clear(startTop, lines.Count);
                    // Выход в меню — как переключение вкладки: набранное дождётся возвращения в игру.
                    if (_display != null) _display.PendingInputText = string.Join("\n", lines);
                    Sound.PlayClick();
                    return "Esc";

                // F1-F4 — звук только если реально переключает экран (как у мыши: клик по уже
                // активному верхнеуровневому табу не кликабелен и звука не даёт).
                case ConsoleKey.F1:
                case ConsoleKey.F2:
                case ConsoleKey.F3:
                case ConsoleKey.F4:
                case ConsoleKey.F5:
                    if (_display != null && _display.ActiveTabKey != key.Key.ToString()) Sound.PlayClick();
                    if (_display != null) _display.PendingInputText = string.Join("\n", lines);
                    return key.Key.ToString();

                case ConsoleKey.F12: // карта мира — темп пути
                case ConsoleKey.F10: // карта мира — в путь (на местности — DEBUG)
                    Sound.PlayClick();
                    if (_display != null) _display.PendingInputText = string.Join("\n", lines);
                    return key.Key.ToString();

                // Звук только если реально переключает вкладку (как у мыши — клик по уже активной
                // вкладке ничего не меняет и звука не даёт). На карте F6–F8 — подвкладки масштаба
                // карты, их звук даёт MapScreenLoop.SetMapLevel.
                case ConsoleKey.F6:
                    if (_display != null && !_arrowKeysMovement && (_display.JournalShown || _display.CharacterSubTab != CharacterSubTab.Inventory)) Sound.PlayClick();
                    if (_display != null) _display.PendingInputText = string.Join("\n", lines);
                    return key.Key.ToString();
                case ConsoleKey.F7:
                    if (_display != null && !_arrowKeysMovement && (_display.JournalShown || _display.CharacterSubTab != CharacterSubTab.Abilities)) Sound.PlayClick();
                    if (_display != null) _display.PendingInputText = string.Join("\n", lines);
                    return key.Key.ToString();
                case ConsoleKey.F8:
                    if (_display != null && !_arrowKeysMovement && (_display.JournalShown || _display.CharacterSubTab != CharacterSubTab.Effects)) Sound.PlayClick();
                    if (_display != null) _display.PendingInputText = string.Join("\n", lines);
                    return key.Key.ToString();
                case ConsoleKey.F9:
                    if (_display != null && _display.CharacterSubTab != CharacterSubTab.Spells) Sound.PlayClick();
                    if (_display != null) _display.PendingInputText = string.Join("\n", lines);
                    return key.Key.ToString();

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        int maxLineLength = (cursorY == 0) ? _textWidth : _innerWidth;

                        if (_multiline && lines[cursorY].Length >= maxLineLength)
                        {
                            lines.Insert(cursorY + 1, "");
                            cursorY++;
                            cursorX = 0;
                        }

                        int totalLength = lines.Sum(l => l.Length) + lines.Count - 1;

                        if (totalLength < _maxLength)
                        {
                            lines[cursorY] = lines[cursorY].Insert(cursorX, key.KeyChar.ToString());
                            cursorX++;
                        }
                        else
                        {
                            Console.Beep();
                        }
                    }
                    break;
            }

            cursorX = Math.Min(cursorX, lines[cursorY].Length);

            // Keep cursor in the visible window with a small right margin (only for line 0)
            if (cursorY == 0)
            {
                const int scrollMargin = 3;
                if (cursorX < viewOffset) viewOffset = cursorX;
                if (cursorX + scrollMargin > viewOffset + _textWidth) viewOffset = cursorX + scrollMargin - _textWidth;
                // Keep cursor out of the left "..." zone when there is left overflow
                if (viewOffset > 0 && cursorX - viewOffset < 3)
                    viewOffset = Math.Max(0, cursorX - 3);
            }

            Draw(lines, cursorX, cursorY, startTop, viewOffset);
        }
    }

    private void Draw(List<string> lines, int cursorX, int cursorY, int startTop, int viewOffset = 0)
    {
        Console.CursorVisible = false;
        Console.SetCursorPosition(0, startTop);

        ColorHelper.SetBackgroundColor(_mainBg);
        ColorHelper.SetForegroundColor(_mainFg);

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            int maxWidth = (i == 0) ? _textWidth : _innerWidth;

            Console.Write(new string(' ', DisplayConfig.LeftMargin));
            ColorHelper.WriteColored("│", MouseUiHelper.FrameColor(_display));

            if (i == 0)
            {
                char[] buf = new char[_textWidth];
                Array.Fill(buf, ' ');
                int visLen = Math.Min(_textWidth, Math.Max(0, line.Length - viewOffset));
                if (visLen > 0) line.CopyTo(viewOffset, buf, 0, visLen);

                if (viewOffset > 0 && _textWidth >= 3)
                { buf[0] = '.'; buf[1] = '.'; buf[2] = '.'; }
                if (line.Length > viewOffset + _textWidth && _textWidth >= 3)
                { buf[_textWidth - 3] = '.'; buf[_textWidth - 2] = '.'; buf[_textWidth - 1] = '.'; }

                Console.Write(_prompt);
                Console.Write(buf);
            }
            else
            {
                string visible = line.Length > maxWidth ? line.Substring(0, maxWidth) : line;
                Console.Write(visible);
                Console.Write(new string(' ', _innerWidth - visible.Length));
            }

            ColorHelper.WriteColored("│", MouseUiHelper.FrameColor(_display));
            int remaining = Console.WindowWidth - Console.CursorLeft;
            if (remaining > 0)
                Console.Write(new string(' ', remaining));
            if (Console.CursorTop < Console.WindowHeight - 1)
                Console.WriteLine();
        }

        int previousMax = _maxLine;
        _maxLine = Math.Max(_maxLine, lines.Count);

        if (lines.Count >= previousMax)
        {
            int delta = lines.Count - previousMax;

            if (_bottomAction != null)
                _bottomAction(delta);
            else if (_drawBottom)
                _borderDrawer.DrawBottomBorder();
        }

        int cursorOffsetInLine = cursorY == 0 ? _promptLength + (cursorX - viewOffset) : cursorX;
        int clampedLeft = Math.Clamp(DisplayConfig.LeftMargin + 1 + cursorOffsetInLine, 0, Console.BufferWidth - 1);
        int clampedTop  = Math.Clamp(startTop + cursorY,     0, Console.BufferHeight - 1);
        Console.SetCursorPosition(clampedLeft, clampedTop);
        Console.CursorVisible = true;
    }

    private static List<string> RestoreLines(string pending)
    {
        if (string.IsNullOrEmpty(pending))
            return new List<string> { "" };
        return pending.Split('\n').ToList();
    }

    private void Clear(int startTop, int height)
    {
        Console.CursorVisible = false;
        ColorHelper.SetBackgroundColor(_mainBg);

        for (int i = 0; i < height; i++)
        {
            int row = startTop + i;
            if (row < 0 || row >= Console.BufferHeight) continue;
            Console.SetCursorPosition(0, row);
            Console.Write(new string(' ', DisplayConfig.LeftMargin + _innerWidth + 2));
        }

        if (startTop >= 0 && startTop < Console.BufferHeight)
            Console.SetCursorPosition(0, startTop);
        Console.CursorVisible = true;
    }

    // Окно консоли чуть выше целого числа строк (см. ConsoleSetup.SetConsoleConfig): полоску пикселей под последней
    // строкой conhost красит ТЕКУЩИМ фоном. Перерисовки (карта мира, картинки, подсветки) могли оставить свой
    // (вода) — снизу появлялась цветная полоса. Пока ждём ввод, текущий фон — основной.
    private long _bgResetAt;

    private ConsoleKeyInfo ReadKeyWithPolling()
    {
        while (true)
        {
            _display?.PollAction?.Invoke();
            if (Environment.TickCount64 - _bgResetAt > 250)
            {
                _bgResetAt = Environment.TickCount64;
                ColorHelper.SetBackgroundColor(_mainBg);
            }
            if (_display?.PendingCommand != null)
            {
                var cmd = _display.PendingCommand;
                _display.PendingCommand = null;
                return cmd switch
                {
                    "F1"  => new ConsoleKeyInfo('\0', ConsoleKey.F1,     false, false, false),
                    "F2"  => new ConsoleKeyInfo('\0', ConsoleKey.F2,     false, false, false),
                    "F3"  => new ConsoleKeyInfo('\0', ConsoleKey.F3,     false, false, false),
                    "F4"  => new ConsoleKeyInfo('\0', ConsoleKey.F4,     false, false, false),
                    "F12" => new ConsoleKeyInfo('\0', ConsoleKey.F12,    false, false, false),
                    "F5"  => new ConsoleKeyInfo('\0', ConsoleKey.F5,     false, false, false),
                    "F6"  => new ConsoleKeyInfo('\0', ConsoleKey.F6,     false, false, false),
                    "F7"  => new ConsoleKeyInfo('\0', ConsoleKey.F7,     false, false, false),
                    "F8"  => new ConsoleKeyInfo('\0', ConsoleKey.F8,     false, false, false),
                    "F9"  => new ConsoleKeyInfo('\0', ConsoleKey.F9,     false, false, false),
                    "F10" => new ConsoleKeyInfo('\0', ConsoleKey.F10,    false, false, false),
                    "Esc" => new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false),
                    _     => throw new InvalidOperationException($"Unknown pending command: {cmd}")
                };
            }
            // Не Console.KeyAvailable: он выбрасывает из очереди события мыши — нажатие, пришедшее между опросами,
            // терялось, и перетаскивание карты через раз не начиналось. TryReadKey мышь оставляет PollAction.
            if (ConsoleMouseReader.TryReadKey() is { } key)
                return key;
            Thread.Sleep(10);
        }
    }

    // Вторая стрелка в течение timeoutMs: другая ось — диагональ (аккорд); та же стрелка — не аккорд, а
    // отдельное нажатие: возвращается в буфер (двойное нажатие — протиснуться мимо мирного существа).
    private ConsoleKey? TryReadChordArrow(int timeoutMs, ConsoleKey self)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            _display?.PollAction?.Invoke();
            if (ConsoleMouseReader.TryReadKey() is { } next)
            {
                if (next.Key == self) { _bufferedKey = next; return null; }
                if (next.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow
                             or ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
                    return next.Key;
                _bufferedKey = next;
                return null;
            }
            Thread.Sleep(5);
        }
        return null;
    }
}
