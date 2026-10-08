using NaviDnD.Data;
using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

public static class ConsoleMouseReader
{
    private const int STD_INPUT_HANDLE  = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_MOUSE_INPUT   = 0x0010;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    private const uint ENABLE_EXTENDED_FLAGS  = 0x0080;
    private const uint ENABLE_PROCESSED_INPUT = 0x0001;
    private const ushort KEY_EVENT_TYPE   = 0x0001;
    private const ushort MOUSE_EVENT_TYPE = 0x0002;
    private const uint MOUSE_MOVED    = 0x0001;
    private const uint MOUSE_WHEELED  = 0x0004;
    private const uint DOUBLE_CLICK   = 0x0002; // dwEventFlags — Windows fires this instead of 0 for the 2nd+ click of a fast sequence
    private const uint LEFT_BUTTON    = 0x0001; // dwButtonState bit
    private const int  IDC_HAND       = 32649;
    private const uint OCR_NORMAL     = 32512;
    private const uint SPI_SETCURSORS = 0x0057;

    [DllImport("kernel32.dll")] private static extern nint GetStdHandle(int n);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(nint h, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(nint h, uint mode);
    [DllImport("kernel32.dll")] private static extern bool ReadConsoleInput(nint h, [Out] INPUT_RECORD[] buf, int len, out int read);
    [DllImport("kernel32.dll")] private static extern bool PeekConsoleInput(nint h, [Out] INPUT_RECORD[] buf, int len, out int read);
    // Unicode-версия — для чтения символа клавиши (кириллица): ANSI-вариант отдаёт не тот символ.
    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW")] private static extern bool ReadConsoleInputW(nint h, [Out] INPUT_RECORD[] buf, int len, out int read);
    [DllImport("kernel32.dll", EntryPoint = "PeekConsoleInputW")] private static extern bool PeekConsoleInputW(nint h, [Out] INPUT_RECORD[] buf, int len, out int read);
    [DllImport("kernel32.dll")] private static extern nint GetConsoleWindow();
    [DllImport("kernel32.dll")] private static extern bool GetCurrentConsoleFont(nint h, bool bMaximum, out CONSOLE_FONT_INFO info);
    [DllImport("kernel32.dll")] private static extern COORD GetConsoleFontSize(nint h, int nFont);
    [DllImport("user32.dll")]   private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")]   private static extern bool ClientToScreen(nint hwnd, ref POINT pt);
    [DllImport("user32.dll")]   private static extern nint LoadCursor(nint hInstance, int lpCursorName);
    [DllImport("user32.dll")]   private static extern nint CopyIcon(nint hIcon); // CopyCursor — макрос над CopyIcon
    [DllImport("user32.dll")]   private static extern nint SetCursor(nint hCursor);
    [DllImport("user32.dll")]   private static extern bool SetSystemCursor(nint hCur, uint id);
    [DllImport("user32.dll")]   private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, nint pvParam, uint fWinIni);
    [DllImport("user32.dll")]   private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]   private static extern bool GetWindowRect(nint hWnd, out RECT rect);
    [DllImport("user32.dll")]   private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private const int VK_LBUTTON = 0x01;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    // Окно консоли без WS_CAPTION/WS_SYSMENU (ConsoleSetup.BlockConsoleFullscreen) не умеет
    // перетаскиваться штатно. Штатный трюк ОС (WM_NCLBUTTONDOWN+HTCAPTION, "как будто схватили
    // заголовок") здесь ненадёжен: ENABLE_MOUSE_INPUT перехватывает движения мыши для консольного
    // ввода на том же потоке conhost, что обслуживает и системный цикл перетаскивания — гонка между
    // ними срабатывает через раз. Вместо этого двигаем окно САМИ: опрашиваем РЕАЛЬНОЕ состояние
    // кнопки (GetAsyncKeyState — не буферизуется консольным вводом, в отличие от кликов из
    // DrainMouseEvents) и переносим окно на дельту курсора, пока кнопка физически зажата.
    public static void StartWindowDrag()
    {
        if (FullscreenBackdrop.Active) return;   // весь экран — окно стоит по центру
        var hwnd = GetConsoleWindow();
        if (hwnd == 0) return;
        if (!GetCursorPos(out var startCursor)) return;
        if (!GetWindowRect(hwnd, out var startRect)) return;

        _windowDrag = () =>
        {
            if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0 || !GetCursorPos(out var cur))
            {
                _windowDrag = null;
                return;
            }
            SetWindowPos(hwnd, 0, startRect.Left + cur.X - startCursor.X,
                startRect.Top + cur.Y - startCursor.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
        };
    }

    // Перетаскивание содержимого (карта мира, местность): сообщает смещение курсора в символах от точки нажатия,
    // пока кнопка зажата. События мыши читаются из очереди по порядку — движения с зажатой кнопкой двигают карту,
    // первое событие без кнопки — отпустил. Раньше смотрели только на кнопку «сейчас» (GetAsyncKeyState): быстрый
    // рывок, пока игра ещё рисовала кадр, заканчивался до начала слежения — кнопка уже отпущена, все движения
    // в очереди пропадали, и рывок считался кликом. Накопленные движения — одним сдвигом (один кадр, а не десять).
    private static Action? _windowDrag;
    private static (short x, short y)? _dragStart;
    private static (short x, short y) _dragLast;
    private static Action<int, int>? _dragDelta;
    private static Action? _dragCompleted;
    private static (short x, short y)? _dragPending;
    private static long _dragNextFrame;
    public static bool IsDragging => _dragStart.HasValue;

    public static void CancelDrag()
    {
        _dragStart = null;
        _dragDelta = null;
        _dragCompleted = null;
        _dragPending = null;
    }

    // Input polling advances the drag; no nested loop stalls animations.
    public static void BeginDrag((short x, short y) start, Action<int, int> onDelta, Action onCompleted)
    {
        _dragPending = null;
        _dragNextFrame = 0;
        _dragStart = start;
        _dragLast = start;
        _dragDelta = onDelta;
        _dragCompleted = onCompleted;
    }

    private static void CompleteDrag()
    {
        var completed = _dragCompleted;
        CancelDrag();
        completed?.Invoke();
    }

    public static void TrackDrag((short x, short y) start, Action<int, int> onDelta)
    {
        var last = start;
        var buf = new INPUT_RECORD[1];
        bool released = false;
        while (!released)
        {
            (short x, short y)? moved = null;
            bool queueEmpty = true;
            while (_enabled && PeekConsoleInput(_inputHandle, buf, 1, out int n) && n > 0)
            {
                if (buf[0].EventType == KEY_EVENT_TYPE && buf[0].KeyDownFlag != 0) { queueEmpty = false; break; }   // клавиша — не наша
                ReadConsoleInput(_inputHandle, buf, 1, out _);
                if (buf[0].EventType != MOUSE_EVENT_TYPE || (buf[0].MouseEvent.dwEventFlags & MOUSE_WHEELED) != 0) continue;
                moved = (buf[0].MouseEvent.dwMousePosition.X, buf[0].MouseEvent.dwMousePosition.Y);
                if ((buf[0].MouseEvent.dwButtonState & LEFT_BUTTON) == 0) { released = true; break; }
            }
            if (moved is { } m)
            {
                _lastMousePos = m;
                if (m != last)
                {
                    last = m;
                    onDelta(m.x - start.x, m.y - start.y);
                }
            }
            if (released) break;
            // Отпускание не дошло до очереди (кнопку отпустили вне окна и т.п.) — по реальному состоянию кнопки,
            // но сначала дать очереди догнать (последние движения ещё в пути).
            if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0)
            {
                if (!queueEmpty) break;   // впереди клавиша — отпускание за ней уже не нужно
                Thread.Sleep(20);
                if (!_enabled || !PeekConsoleInput(_inputHandle, buf, 1, out int left) || left == 0) break;
                continue;
            }
            Thread.Sleep(10);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct COORD { public short X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_FONT_INFO { public int nFont; public COORD dwFontSize; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSE_EVENT_RECORD
    {
        public COORD dwMousePosition;
        public uint dwButtonState, dwControlKeyState, dwEventFlags;
    }

    // INPUT_RECORD: EventType (2) + 2 pad + Event union (16) = 20 bytes
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)]  public ushort EventType;
        [FieldOffset(4)]  public int    KeyDownFlag;    // KEY_EVENT_RECORD.bKeyDown: non-zero = key-down
        [FieldOffset(8)]  public ushort RepeatCount;    // KEY_EVENT_RECORD.wRepeatCount — консоль склеивает одинаковые нажатия
        [FieldOffset(10)] public ushort VirtualKeyCode; // KEY_EVENT_RECORD.wVirtualKeyCode
        // KEY_EVENT_RECORD.uChar (W-версия чтения). ushort, а не char: char в структуре делает её
        // небиттовой — маршалер в ANSI-вызовах сжимает его до байта и ломает раскладку записи.
        [FieldOffset(14)] public ushort UnicodeChar;
        [FieldOffset(16)] public uint   KeyControlState;
        [FieldOffset(4)]  public MOUSE_EVENT_RECORD MouseEvent;
    }

    private static nint _inputHandle;
    private static uint _originalMode;
    private static bool _enabled;
    private static bool _handCursorActive;

    // Sub-cell fractions captured at the moment events were drained.
    private static float _cachedSubX = 0.5f;
    private static float _cachedSubY = 0.5f;

    // Last character position used for cell resolution + its result.
    // If the char position hasn't changed, we return the same cell without re-reading sub-pixel fraction.
    private static (short x, short y)? _lastCellCharPos = null;
    private static (int col, int row)? _lastCellResult  = null;

    public static void Enable()
    {
        _inputHandle = GetStdHandle(STD_INPUT_HANDLE);
        GetConsoleMode(_inputHandle, out _originalMode);
        // ENABLE_PROCESSED_INPUT снят: с ним conhost перехватывает Shift+стрелки под своё выделение текста
        // с клавиатуры (строки подсвечивались, ввод игре не доходил). Ctrl+C при этом приходит обычной клавишей.
        uint newMode = (_originalMode & ~ENABLE_QUICK_EDIT_MODE & ~ENABLE_PROCESSED_INPUT) | ENABLE_MOUSE_INPUT | ENABLE_EXTENDED_FLAGS;
        SetConsoleMode(_inputHandle, newMode);
        _enabled = true;
    }

    public static void Disable()
    {
        CancelDrag();
        _windowDrag = null;
        if (_enabled)
        {
            SetConsoleMode(_inputHandle, _originalMode);
            SetCursorShape(false);
            _enabled = false;
        }
    }

    public static void SetCursorShape(bool hand)
    {
        if (hand == _handCursorActive) return;
        _handCursorActive = hand;
        if (hand)
            SetSystemCursor(CopyIcon(LoadCursor(0, IDC_HAND)), OCR_NORMAL);
        else
        {
            SystemParametersInfo(SPI_SETCURSORS, 0, 0, 0);
            SetCursor(LoadCursor(0, 32512)); // OCR_NORMAL — force immediate visual update
        }
    }

    /// Drains movement up to the first button-down, preserving queued drag events.
    /// Returns one click at a time; later clicks and keyboard events remain queued.
    public static ((short x, short y)? move, (short x, short y)? click, int clickCount) DrainMouseEvents()
    {
        _windowDrag?.Invoke();
        if (!_enabled) return (null, null, 0);

        (short x, short y)? latestMove  = null;
        (short x, short y)? latestClick = null;
        int clickCount = 0;
        bool dragReleased = false;
        var buf = new INPUT_RECORD[1];

        while (true)
        {
            if (!PeekConsoleInput(_inputHandle, buf, 1, out int peeked) || peeked == 0) break;
            // Consume key-UP events: they are harmless but block subsequent mouse reads.
            // Stop at key-DOWN events so Console.ReadKey can process them.
            if (buf[0].EventType == KEY_EVENT_TYPE && buf[0].KeyDownFlag == 0)
            {
                ReadConsoleInput(_inputHandle, buf, 1, out _);
                continue;
            }
            if (buf[0].EventType == KEY_EVENT_TYPE) break; // KEY_DOWN — preserve for TryReadKeyDown/Console.ReadKey
            if (buf[0].EventType != MOUSE_EVENT_TYPE)
            {
                // FOCUS_EVENT, WINDOW_BUFFER_SIZE_EVENT, MENU_EVENT — consume silently
                ReadConsoleInput(_inputHandle, buf, 1, out _);
                continue;
            }
            if (!ReadConsoleInput(_inputHandle, buf, 1, out int read) || read == 0) break;
            if ((buf[0].MouseEvent.dwEventFlags & MOUSE_WHEELED) != 0)
            {
                // Старшее слово dwButtonState — знаковая прокрутка (кратна 120): > 0 — колесо от себя.
                AddWheel((short)(buf[0].MouseEvent.dwButtonState >> 16) / 120,
                    (buf[0].MouseEvent.dwMousePosition.X, buf[0].MouseEvent.dwMousePosition.Y));
            }
            else if (_dragStart is { } dragStart)
            {
                var pos = (x: buf[0].MouseEvent.dwMousePosition.X, y: buf[0].MouseEvent.dwMousePosition.Y);
                _lastMousePos = pos;
                _dragPending = pos;
                if ((buf[0].MouseEvent.dwButtonState & LEFT_BUTTON) == 0)
                {
                    dragReleased = true;
                    break;
                }
            }
            else if ((buf[0].MouseEvent.dwEventFlags & MOUSE_MOVED) != 0)
            {
                latestMove = (buf[0].MouseEvent.dwMousePosition.X, buf[0].MouseEvent.dwMousePosition.Y);
                _lastMousePos = latestMove.Value;
            }
            else if ((buf[0].MouseEvent.dwEventFlags == 0 || buf[0].MouseEvent.dwEventFlags == DOUBLE_CLICK) &&
                     (buf[0].MouseEvent.dwButtonState & LEFT_BUTTON) != 0)
            {
                latestClick = (buf[0].MouseEvent.dwMousePosition.X, buf[0].MouseEvent.dwMousePosition.Y);
                clickCount++;
                // Let the caller start a drag before consuming its queued moves/release.
                break;
            }
        }

        if (_dragStart.HasValue && (GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0)
        {
            // Key events ahead of the release must be handled by the caller first.
            if (!PeekConsoleInput(_inputHandle, buf, 1, out int pending) || pending == 0)
                dragReleased = true;
        }
        if (_dragStart is { } start && _dragPending is { } moved
            && (dragReleased || Environment.TickCount64 >= _dragNextFrame))
        {
            _dragPending = null;
            if (moved != _dragLast)
            {
                _dragLast = moved;
                _dragNextFrame = Environment.TickCount64 + 16;
                _dragDelta?.Invoke(moved.x - start.x, moved.y - start.y);
            }
        }
        if (dragReleased) CompleteDrag();

        // When the character position changed — update sub-cell fractions and invalidate cell cache.
        if (latestMove.HasValue && latestMove != _lastCellCharPos)
        {
            _cachedSubX = GetSubCellFractionX();
            _cachedSubY = GetSubCellFraction();
            _lastCellCharPos = null; // force recompute in ScreenToMapCell
            _lastCellResult  = null;
        }

        return (latestMove, latestClick, clickCount);
    }

    public static (short x, short y)? DrainMouseMoves() => DrainMouseEvents().move;

    private static int _wheelNotches;
    private static (short x, short y) _wheelPos;
    private static DateTime _wheelAt;
    private static (short x, short y) _lastMousePos;
    private static readonly object _wheelLock = new();

    // Прокрутка из консоли (DrainMouseEvents) или из ConsoleZoomBlocker (Ctrl+колесо/щипок тачпада,
    // перехваченные до консоли — с другого потока, поэтому под замком). pos == null — последняя
    // известная позиция курсора.
    public static void AddWheel(int notches, (short x, short y)? pos = null)
    {
        if (notches == 0) return;
        lock (_wheelLock)
        {
            _wheelNotches += notches;
            _wheelPos = pos ?? _lastMousePos;
            _wheelAt = DateTime.UtcNow;
        }
    }

    /// Накопленная прокрутка колеса (щелчки; > 0 — от себя) и позиция курсора при ней. Копится в
    /// DrainMouseEvents; забирать после него. Старая прокрутка (> 300 мс — экран её не обработал)
    /// отбрасывается, чтобы не сработать внезапно на другом экране.
    public static int TakeWheel(out (short x, short y) pos)
    {
        lock (_wheelLock)
        {
            pos = _wheelPos;
            int notches = (DateTime.UtcNow - _wheelAt).TotalMilliseconds <= 300 ? _wheelNotches : 0;
            _wheelNotches = 0;
            return notches;
        }
    }

    /// Non-blocking read of the next KEY_DOWN event from the input buffer.
    /// Silently consumes KEY_UP events, focus events, and window events.
    /// Stops at mouse events without consuming them (preserved for DrainMouseEvents).
    /// Returns null when no KEY_DOWN event is available.
    public static ConsoleKey? TryReadKeyDown()
    {
        if (!_enabled) return null;
        var buf = new INPUT_RECORD[1];
        while (true)
        {
            if (!PeekConsoleInput(_inputHandle, buf, 1, out int peeked) || peeked == 0) return null;
            if (buf[0].EventType == MOUSE_EVENT_TYPE) return null; // leave for DrainMouseEvents
            if (buf[0].EventType == KEY_EVENT_TYPE)
            {
                if (buf[0].KeyDownFlag == 0)
                {
                    ReadConsoleInput(_inputHandle, buf, 1, out _); // consume KEY_UP
                    continue;
                }
                ReadConsoleInput(_inputHandle, buf, 1, out _); // consume KEY_DOWN
                var key = (ConsoleKey)buf[0].VirtualKeyCode;
                if (key == ConsoleKey.F11) { Speech.Stop(); continue; }
                if (key is ConsoleKey.Escape or ConsoleKey.Enter) Speech.Stop();
                return key;
            }
            // FOCUS_EVENT, WINDOW_BUFFER_SIZE_EVENT, MENU_EVENT — consume silently
            ReadConsoleInput(_inputHandle, buf, 1, out _);
        }
    }

    /// Неблокирующее чтение следующего KEY_DOWN прямо из буфера консоли — код клавиши И символ
    /// (TryReadKeyDown отдаёт только код). Как TryReadKeyDown: KEY_UP/служебные события съедает,
    /// на событии мыши останавливается (его заберёт DrainMouseEvents). Мышь выключена — Console.ReadKey.
    private static ConsoleKeyInfo _repeatKey;
    private static int _repeatLeft;

    public static ConsoleKeyInfo? TryReadKey()
    {
        if (_repeatLeft > 0) { _repeatLeft--; return _repeatKey; }   // остаток склеенного нажатия («нн», зажатая клавиша)
        if (!_enabled) return Console.KeyAvailable ? Console.ReadKey(true) : null;
        var buf = new INPUT_RECORD[1];
        while (true)
        {
            if (!PeekConsoleInputW(_inputHandle, buf, 1, out int peeked) || peeked == 0) return null;
            if (buf[0].EventType == MOUSE_EVENT_TYPE) return null;
            ReadConsoleInputW(_inputHandle, buf, 1, out _);
            if (buf[0].EventType != KEY_EVENT_TYPE || buf[0].KeyDownFlag == 0) continue;
            var key = (ConsoleKey)buf[0].VirtualKeyCode;
            if (key == ConsoleKey.F11) { Speech.Stop(); continue; }
            if (key is ConsoleKey.Escape or ConsoleKey.Enter) Speech.Stop();
            if (key is (ConsoleKey)16 or (ConsoleKey)17 or (ConsoleKey)18 or (ConsoleKey)20 or (ConsoleKey)144 or (ConsoleKey)145 or (ConsoleKey)91 or (ConsoleKey)92) continue; // Shift/Ctrl/Alt, Caps/Num/Scroll Lock, Win — сами по себе (как Console.ReadKey)
            uint s = buf[0].KeyControlState;
            var info = new ConsoleKeyInfo((char)buf[0].UnicodeChar, key,
                shift: (s & 0x0010) != 0, alt: (s & 0x0003) != 0, control: (s & 0x000C) != 0);
            if (buf[0].RepeatCount > 1) { _repeatKey = info; _repeatLeft = buf[0].RepeatCount - 1; }
            return info;
        }
    }

    /// Converts a console screen character position to a wall/border position.
    /// For horizontal walls (between rows): returns (col, upperRow, true).
    /// For vertical walls (between columns): returns (leftCol, row, false).
    /// Returns null if the position is not on a wall.
    public static (int col, int row, bool isHorizontal)? ScreenToMapWall(
        short screenX, short screenY,
        int mapDrawTop, int mapCols, int mapRows, int cellWidth)
    {
        int cellW = cellWidth + 1;
        int relY = screenY - mapDrawTop - 3;
        if (relY < -1) return null;  // -1 = top border row

        // margin + outer │(1) + row numbers(3) chars before the first grid border.
        int relX = screenX - (DisplayConfig.LeftMargin + 4);
        if (relX < 0) return null;

        if (relY % 2 != 0)
        {
            // Horizontal wall — relY odd or -1; exclude corners (relX % cellW == 0)
            if (relX % cellW == 0) return null;
            int col = relX / cellW + 1;
            int upperRow = mapRows - (relY - 1) / 2;
            if (col < 1 || col > mapCols) return null;
            if (upperRow < 1 || upperRow > mapRows + 1) return null;
            return (col, upperRow, true);
        }
        else if (relX % cellW == 0)
        {
            // Vertical wall — includes left border (leftCol=0) and right border (leftCol=mapCols)
            int leftCol = relX / cellW;
            int row = mapRows - relY / 2;
            if (leftCol < 0 || leftCol > mapCols) return null;
            if (row < 1 || row > mapRows) return null;
            return (leftCol, row, false);
        }

        return null;
    }

    /// Converts a console screen character position to a map cell (col, row).
    /// Handles border characters: vertical borders snap to adjacent cell,
    /// horizontal grid lines use pixel sub-cell position (top/bottom half) to pick upper/lower cell.
    public static (int col, int row)? ScreenToMapCell(
        short screenX, short screenY,
        int mapDrawTop, int mapCols, int mapRows, int cellWidth)
    {
        // Same character position → same cell, no sub-pixel re-read (prevents oscillation).
        if (_lastCellCharPos.HasValue && _lastCellCharPos.Value.x == screenX && _lastCellCharPos.Value.y == screenY)
            return _lastCellResult;

        var result = ComputeMapCell(screenX, screenY, mapDrawTop, mapCols, mapRows, cellWidth);
        _lastCellCharPos = (screenX, screenY);
        _lastCellResult  = result;
        return result;
    }

    private static (int col, int row)? ComputeMapCell(
        short screenX, short screenY,
        int mapDrawTop, int mapCols, int mapRows, int cellWidth)
    {
        int cellW = cellWidth + 1;

        int relY = screenY - mapDrawTop - 3;
        if (relY < 0) return null;

        // X: margin + outer │(1) + row numbers(3) chars before the first grid border.
        int relX = screenX - (DisplayConfig.LeftMargin + 4);
        if (relX < 0) return null;

        int col;
        if (relX % cellW == 0)
        {
            // Vertical border character — pick left or right adjacent cell by pixel X fraction.
            int leftCol  = relX / cellW;
            int rightCol = leftCol + 1;
            col = _cachedSubX < 0.5f ? leftCol : rightCol;
            if (col < 1 || col > mapCols) return null;
        }
        else
        {
            col = relX / cellW + 1;
            if (col < 1 || col > mapCols) return null;
        }

        if (relY % 2 == 0)
        {
            // Data row
            int row = mapRows - relY / 2;
            if (row < 1 || row > mapRows) return null;
            return (col, row);
        }
        else
        {
            // Horizontal grid line between two rows.
            // Upper cell: row at (relY-1), lower cell: row at (relY+1).
            int upperRow = mapRows - (relY - 1) / 2;
            int lowerRow = upperRow - 1;

            int row = _cachedSubY < 0.5f ? upperRow : lowerRow;

            // Clamp to valid range
            if (row < 1 || row > mapRows)
            {
                if (upperRow >= 1 && upperRow <= mapRows) row = upperRow;
                else if (lowerRow >= 1 && lowerRow <= mapRows) row = lowerRow;
                else return null;
            }

            return (col, row);
        }
    }

    /// Returns the fractional Y position of the pixel cursor within its character cell (0.0 = top, 1.0 = bottom).
    private static float GetSubCellFraction()
    {
        try
        {
            if (!GetCursorPos(out var pt)) return 0.5f;

            nint hwnd = GetConsoleWindow();
            var clientOrigin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref clientOrigin);

            nint outHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (!GetCurrentConsoleFont(outHandle, false, out var fontInfo)) return 0.5f;
            var fontSize = GetConsoleFontSize(outHandle, fontInfo.nFont);
            if (fontSize.Y <= 0) return 0.5f;

            int pixelInCell = (pt.Y - clientOrigin.Y) % fontSize.Y;
            return Math.Clamp((float)pixelInCell / fontSize.Y, 0f, 1f);
        }
        catch
        {
            return 0.5f;
        }
    }

    /// Returns the fractional X position of the pixel cursor within its character cell (0.0 = left, 1.0 = right).
    private static float GetSubCellFractionX()
    {
        try
        {
            if (!GetCursorPos(out var pt)) return 0.5f;

            nint hwnd = GetConsoleWindow();
            var clientOrigin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref clientOrigin);

            nint outHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (!GetCurrentConsoleFont(outHandle, false, out var fontInfo)) return 0.5f;
            var fontSize = GetConsoleFontSize(outHandle, fontInfo.nFont);
            if (fontSize.X <= 0) return 0.5f;

            int pixelInCell = (pt.X - clientOrigin.X) % fontSize.X;
            return Math.Clamp((float)pixelInCell / fontSize.X, 0f, 1f);
        }
        catch
        {
            return 0.5f;
        }
    }
}
