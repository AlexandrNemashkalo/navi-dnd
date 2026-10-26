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
    private const uint MOUSE_HWHEELED = 0x0008;
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
    // Unicode-version - for reading the key symbol (Cyrillic): ANSI-version returns the wrong symbol.
    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW")] private static extern bool ReadConsoleInputW(nint h, [Out] INPUT_RECORD[] buf, int len, out int read);
    [DllImport("kernel32.dll", EntryPoint = "PeekConsoleInputW")] private static extern bool PeekConsoleInputW(nint h, [Out] INPUT_RECORD[] buf, int len, out int read);
    [DllImport("kernel32.dll")] private static extern nint GetConsoleWindow();
    [DllImport("kernel32.dll")] private static extern bool GetCurrentConsoleFont(nint h, bool bMaximum, out CONSOLE_FONT_INFO info);
    [DllImport("kernel32.dll")] private static extern COORD GetConsoleFontSize(nint h, int nFont);
    [DllImport("user32.dll")]   private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")]   private static extern bool ClientToScreen(nint hwnd, ref POINT pt);
    [DllImport("user32.dll")]   private static extern nint LoadCursor(nint hInstance, int lpCursorName);
    [DllImport("user32.dll")]   private static extern nint CopyIcon(nint hIcon); // CopyCursor - macro over CopyIcon
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

    // Console window without WS_CAPTION/WS_SYSMENU (ConsoleSetup.BlockConsoleFullscreen) cannot be dragged
    // normally. The standard OS trick (WM_NCLBUTTONDOWN+HTCAPTION, “as if they grabbed the title”) is
    // unreliable here: ENABLE_MOUSE_INPUT intercepts mouse movements for console input on the same thread
    // conhost, which also serves the system drag loop - the race between them is triggered every other time.
    // Instead, we move the window OURSELVES: poll the REAL state of the button (GetAsyncKeyState - not buffered
    // by console input, unlike clicks from DrainMouseEvents)) and move the window to the cursor delta while the
    // button is physically held down.
    public static void StartWindowDrag()
    {
        if (!OperatingSystem.IsWindows()) return;   // Unix terminal owns its window
        if (FullscreenBackdrop.Active) return;   // entire screen - the window is centered
        var hwnd = WindowControls.NativeWindow;
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

    // Drag content (world map, terrain): reports the cursor offset in characters from the click point while the
    // button is held down. Mouse events are read from the queue in order - movements with the button held down
    // move the map, the first event without the button - released. Previously, they only looked at the “now”
    // button (GetAsyncKeyState): a quick dash, while the game was still drawing a frame, ended before the
    // tracking began - the button was already released, all movements in the queue were lost, and the dash was
    // considered a click. The accumulated movements - one shift (one frame, not ten).
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
            while (_enabled && PeekInputAnsi(buf, out int n) && n > 0)
            {
                if (buf[0].EventType == KEY_EVENT_TYPE && buf[0].KeyDownFlag != 0) { queueEmpty = false; break; }   // key is not ours
                ReadInputAnsi(buf, out _);
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
            // The release did not reach the queue (the button was released outside the window, etc.) -
            // according to the real state of the button, but first let the queue catch up (the last movements
            // are still on the way).
            if (!LeftButtonHeld())
            {
                if (!queueEmpty) break;   // there is a key in front - releasing behind it is no longer necessary
                Thread.Sleep(20);
                if (!_enabled || !PeekInputAnsi(buf, out int left) || left == 0) break;
                continue;
            }
            Thread.Sleep(10);
        }
    }

    // Input queue: consoles on Windows, UnixTerminal (same entries) on macOS/Linux.
    private static bool PeekInput(INPUT_RECORD[] buf, out int n)
    {
        if (UnixTerminal.Current is { } u) { n = u.Peek(buf); return true; }
        return PeekConsoleInputW(_inputHandle, buf, 1, out n);
    }

    private static bool ReadInput(INPUT_RECORD[] buf, out int n)
    {
        if (UnixTerminal.Current is { } u) { n = u.Read(buf); return n > 0; }
        return ReadConsoleInputW(_inputHandle, buf, 1, out n);
    }

    private static bool PeekInputAnsi(INPUT_RECORD[] buf, out int n)
    {
        if (UnixTerminal.Current is { } u) { n = u.Peek(buf); return true; }
        return PeekConsoleInput(_inputHandle, buf, 1, out n);
    }

    private static bool ReadInputAnsi(INPUT_RECORD[] buf, out int n)
    {
        if (UnixTerminal.Current is { } u) { n = u.Read(buf); return n > 0; }
        return ReadConsoleInput(_inputHandle, buf, 1, out n);
    }

    private static bool LeftButtonHeld() =>
        UnixTerminal.Current is { } u ? u.LeftButtonDown : (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct COORD { public short X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_FONT_INFO { public int nFont; public COORD dwFontSize; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSE_EVENT_RECORD
    {
        public COORD dwMousePosition;
        public uint dwButtonState, dwControlKeyState, dwEventFlags;
    }

    // INPUT_RECORD: EventType (2) + 2 pad + Event union (16) = 20 bytes
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    internal struct INPUT_RECORD
    {
        [FieldOffset(0)]  public ushort EventType;
        [FieldOffset(4)]  public int    KeyDownFlag;    // KEY_EVENT_RECORD.bKeyDown: non-zero = key-down
        [FieldOffset(8)]  public ushort RepeatCount;    // KEY_EVENT_RECORD.wRepeatCount - the console glues together identical presses
        [FieldOffset(10)] public ushort VirtualKeyCode; // KEY_EVENT_RECORD.wVirtualKeyCode KEY_EVENT_RECORD.uChar (W-read version). ushort, and not char: char
        // in the structure makes it non-bit - the marshaler in ANSI- calls compresses it to a byte and breaks
        // the record layout.
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
        if (UnixTerminal.Current != null) { _enabled = true; return; }
        if (!OperatingSystem.IsWindows() || WindowsAlacritty.Active) return;
        _inputHandle = GetStdHandle(STD_INPUT_HANDLE);
        GetConsoleMode(_inputHandle, out _originalMode);
        // ENABLE_PROCESSED_INPUT has been removed: with it, conhost intercepts Shift+ arrows for his text
        // selection from the keyboard (the lines were highlighted, the input did not reach the game). Ctrl+C
        // comes with a regular key.
        uint newMode = (_originalMode & ~ENABLE_QUICK_EDIT_MODE & ~ENABLE_PROCESSED_INPUT) | ENABLE_MOUSE_INPUT | ENABLE_EXTENDED_FLAGS;
        SetConsoleMode(_inputHandle, newMode);
        _enabled = true;
    }

    public static void Disable()
    {
        CancelDrag();
        _windowDrag = null;
        if (_enabled && UnixTerminal.Current != null)
        {
            SetCursorShape(false);
            _enabled = false;
        }
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
        if (!OperatingSystem.IsWindows())
        {
            UnixTerminal.Current?.SetPointerShape(hand);
            return;
        }
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
            if (!PeekInputAnsi(buf, out int peeked) || peeked == 0) break;
            // Consume key-UP events: they are harmless but block subsequent mouse reads.
            // Stop at key-DOWN events so Console.ReadKey can process them.
            if (buf[0].EventType == KEY_EVENT_TYPE && buf[0].KeyDownFlag == 0)
            {
                ReadInputAnsi(buf, out _);
                continue;
            }
            if (buf[0].EventType == KEY_EVENT_TYPE) break; // KEY_DOWN — leave for TryReadKeyDown/Term.ReadKey
            if (buf[0].EventType != MOUSE_EVENT_TYPE)
            {
                // FOCUS_EVENT, WINDOW_BUFFER_SIZE_EVENT, MENU_EVENT — consume silently
                ReadInputAnsi(buf, out _);
                continue;
            }
            if (!ReadInputAnsi(buf, out int read) || read == 0) break;
            if ((buf[0].MouseEvent.dwEventFlags & MOUSE_HWHEELED) != 0)
            {
                lock (_wheelLock)
                {
                    int notches = _hwheelDelta.Add((short)(buf[0].MouseEvent.dwButtonState >> 16), Environment.TickCount64);
                    if (notches != 0)
                    {
                        _hwheelNotches += notches;
                        _wheelPos = (buf[0].MouseEvent.dwMousePosition.X, buf[0].MouseEvent.dwMousePosition.Y);
                        _wheelAt = DateTime.UtcNow;
                    }
                }
            }
            else if ((buf[0].MouseEvent.dwEventFlags & MOUSE_WHEELED) != 0)
            {
                // Keep small precision-touchpad deltas until they form a full wheel notch.
                AddWheelDelta((short)(buf[0].MouseEvent.dwButtonState >> 16),
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

        if (_dragStart.HasValue && !LeftButtonHeld())
        {
            // Key events ahead of the release must be handled by the caller first.
            if (!PeekInputAnsi(buf, out int pending) || pending == 0)
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

        int width = MouseUiHelper.TitleWidth;
        int left = DisplayConfig.LeftMargin + 1;
        if (latestMove is { } hover)
        {
            int button = WindowControls.Available ? WindowControls.HitTest(hover.x, hover.y, left, width) : -1;
            if (button != _windowButtonHovered)
            {
                _windowButtonHovered = button;
                DrawWindowButtons(button, left, width);
            }
            if (button >= 0) { SetCursorShape(true); latestMove = null; }
        }
        if (latestClick is { } controlClick)
        {
            int button = WindowControls.Available ? WindowControls.HitTest(controlClick.x, controlClick.y, left, width) : -1;
            if (button >= 0)
            {
                WindowControls.Activate(button);
                DrawWindowButtons(-1, left, width);
                _windowButtonHovered = -1;
                latestClick = null;
                clickCount = 0;
            }
        }
        return (latestMove, latestClick, clickCount);
    }

    private static int _windowButtonHovered = -1;

    public static void RefreshWindowButtons()
    {
        _windowButtonHovered = -1;
        DrawWindowButtons(-1, DisplayConfig.LeftMargin + 1, MouseUiHelper.TitleWidth);
    }

    private static void DrawWindowButtons(int hovered, int left, int width)
    {
        if (width < WindowControls.Buttons.Length + 3) return;
        int x = Term.CursorLeft, y = Term.CursorTop;
        bool visible = Term.CursorVisible;
        Term.CursorVisible = false;
        try
        {
            Term.SetCursorPosition(left + width - WindowControls.Buttons.Length, 1);
            WindowControls.Draw(hovered);
        }
        finally
        {
            Term.SetCursorPosition(x, y);
            Term.CursorVisible = visible;
        }
    }

    public static (short x, short y)? DrainMouseMoves() => DrainMouseEvents().move;

    // Waiting (AI thinks, loading): clicks are not accepted, but the window is still dragged by the top block
    // (frame, title, separator - lines 0–2).
    public static void DrainWhileWaiting()
    {
        if (DrainMouseEvents().click is { y: >= 0 and <= 2 }) StartWindowDrag();
    }

    private static int _wheelNotches;
    private static (short x, short y) _wheelPos;
    private static DateTime _wheelAt;
    private static (short x, short y) _lastMousePos;
    private static readonly object _wheelLock = new();
    private static readonly WheelDeltaAccumulator _wheelDelta = new();
    // Horizontal scrolling (touchpad swipe sideways): > 0 - to the right. Climbs together with vertical (TakeWheel).
    private static int _hwheelNotches;
    private static readonly WheelDeltaAccumulator _hwheelDelta = new();

    public static void AddWheelDelta(int delta, (short x, short y)? pos = null)
    {
        lock (_wheelLock)
        {
            int notches = _wheelDelta.Add(delta, Environment.TickCount64);
            if (notches != 0) AddWheel(notches, pos);
        }
    }

    // Scrolling from the console (DrainMouseEvents) or from ConsoleZoomBlocker (Ctrl+ wheel/touchpad pinch,
    // intercepted to the console - from another thread, therefore under lock and key). pos == null - last known
    // cursor position.
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

    ///Accumulated scrolling of the wheel (clicks; > 0 - away from you) and the cursor position during it. Accumulating in
    ///DrainMouseEvents; pick up after it. Old scrolling (> 300 ms - the screen did not process it)
    ///is discarded to avoid triggering unexpectedly on another screen.
    public static int TakeWheel(out (short x, short y) pos) => TakeWheel(out pos, out _);

    // The same with horizontal scrolling (horizontal: > 0 - to the right).
    public static int TakeWheel(out (short x, short y) pos, out int horizontal)
    {
        lock (_wheelLock)
        {
            pos = _wheelPos;
            bool fresh = (DateTime.UtcNow - _wheelAt).TotalMilliseconds <= 300;
            int notches = fresh ? _wheelNotches : 0;
            horizontal = fresh ? _hwheelNotches : 0;
            _wheelNotches = 0;
            _hwheelNotches = 0;
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
            if (!PeekInputAnsi(buf, out int peeked) || peeked == 0) return null;
            if (buf[0].EventType == MOUSE_EVENT_TYPE) return null; // leave for DrainMouseEvents
            if (buf[0].EventType == KEY_EVENT_TYPE)
            {
                if (buf[0].KeyDownFlag == 0)
                {
                    ReadInputAnsi(buf, out _); // pick up KEY_UP
                    continue;
                }
                ReadInputAnsi(buf, out _); // pick up KEY_DOWN
                var key = (ConsoleKey)buf[0].VirtualKeyCode;
                if (key == ConsoleKey.F11) { Speech.Stop(); continue; }
                if (key is ConsoleKey.Escape or ConsoleKey.Enter) Speech.Stop();
                return key;
            }
            // FOCUS_EVENT, WINDOW_BUFFER_SIZE_EVENT, MENU_EVENT — consume silently
            ReadInputAnsi(buf, out _);
        }
    }

    ///Non-blocking read next KEY_DOWN directly from console buffer - key code AND character
    ///(TryReadKeyDown gives only the code). How TryReadKeyDown: KEY_UP/ eats service events,
    ///stops at the mouse event (it will be picked up by DrainMouseEvents). Mouse is turned off - Console.ReadKey.
    private static ConsoleKeyInfo _repeatKey;
    private static int _repeatLeft;

    public static ConsoleKeyInfo? TryReadKey()
    {
        if (_repeatLeft > 0) { _repeatLeft--; return _repeatKey; }   // remainder of the glued press (“nn”, pressed key)
        if (!_enabled) return Term.KeyAvailable ? Term.ReadKey(true) : null;
        var buf = new INPUT_RECORD[1];
        while (true)
        {
            if (!PeekInput(buf, out int peeked) || peeked == 0) return null;
            if (buf[0].EventType == MOUSE_EVENT_TYPE) return null;
            ReadInput(buf, out _);
            if (buf[0].EventType != KEY_EVENT_TYPE || buf[0].KeyDownFlag == 0) continue;
            var key = (ConsoleKey)buf[0].VirtualKeyCode;
            if (key == ConsoleKey.F11) { Speech.Stop(); continue; }
            if (key is ConsoleKey.Escape or ConsoleKey.Enter) Speech.Stop();
            if (key is (ConsoleKey)16 or (ConsoleKey)17 or (ConsoleKey)18 or (ConsoleKey)20 or (ConsoleKey)144 or (ConsoleKey)145 or (ConsoleKey)91 or (ConsoleKey)92) continue; // Shift/Ctrl/Alt, Caps/Num/Scroll Lock, Win - by themselves (as Term.ReadKey)
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
        if (!OperatingSystem.IsWindows() || WindowsAlacritty.Active) return 0.5f;   // terminal reports only whole cells
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
        if (!OperatingSystem.IsWindows() || WindowsAlacritty.Active) return 0.5f;   // terminal reports only whole cells
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
