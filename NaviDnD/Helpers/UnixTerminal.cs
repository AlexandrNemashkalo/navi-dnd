using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace NaviDnD.Helpers;

// The terminal on macOS/Linux. The game was written against the Windows console: a queue of key and mouse records
// (ReadConsoleInput) and a cursor whose position can always be read back. This class gives it the same model on a
// VT terminal: stdin goes raw, an input thread turns escape sequences (keys, SGR mouse, bracketed paste) into
// Windows-style INPUT_RECORDs, and stdout goes through a writer that tracks the cursor and batches writes.
// .NET's own Console input and cursor getters are never used while this runs — they would read stdin themselves.
internal sealed class UnixTerminal
{
    public static UnixTerminal? Current { get; private set; }

    private const int StdIn = 0;
    private const ushort KeyEvent = 0x0001, MouseEvent = 0x0002;
    private const uint MouseMoved = 0x0001, DoubleClick = 0x0002, MouseWheeled = 0x0004, MouseHWheeled = 0x0008;
    private const uint LeftButton = 0x0001, RightButton = 0x0002, MiddleButton = 0x0004;
    private const uint ShiftPressed = 0x0010, LeftAltPressed = 0x0002, LeftCtrlPressed = 0x0008;

    private readonly byte[] _savedTermios = new byte[256];
    private readonly byte[] _rawTermios = new byte[256];
    private readonly TrackingWriter _out;
    private readonly List<ConsoleMouseReader.INPUT_RECORD> _queue = [];
    private readonly object _queueLock = new();
    private readonly BlockingCollection<byte> _bytes = new();
    private readonly Timer _flushTimer;
    private int _width = 80, _height = 24;
    private long _sizeCheckedAt;
    private uint _buttons;
    private long _lastLeftPress;
    private (short x, short y) _lastLeftPos;
    private bool _lastWasDouble;
    private int _restored;
    private int _ticks;
    private readonly List<PosixSignalRegistration> _signals = [];   // a registration lasts only while it is referenced

    public bool CursorVisible { get; private set; } = true;
    public int CursorLeft => _out.Col;
    public int CursorTop => _out.Row;
    public bool LeftButtonDown => (_buttons & LeftButton) != 0;

    // The game's grid. In a larger terminal (full screen) the game is centred in it: the size is the game's, and
    // the writer and the mouse shift by the origin.
    public int Width => _centered ? _layoutCols : RealWidth;
    public int Height => _centered ? _layoutRows : RealHeight;
    private int RealWidth { get { RefreshSize(); return _width; } }
    private int RealHeight { get { RefreshSize(); return _height; } }
    private int _offX, _offY, _layoutCols, _layoutRows;
    private bool _centered;

    // Rows at the top the game must not use: on a MacBook with a notch the full-screen terminal reaches under it.
    public static int ReservedTopRows { get; set; }

    // Takes over the terminal; null when this is Windows or stdin/stdout are not a terminal (tests, pipes).
    public static UnixTerminal? Start()
    {
        if (OperatingSystem.IsWindows() || Current != null || Console.IsInputRedirected || Console.IsOutputRedirected) return Current;
        try
        {
            Current = new UnixTerminal();
            return Current;
        }
        catch
        {
            Current = null;
            return null;
        }
    }

    private UnixTerminal()
    {
        if (tcgetattr(StdIn, _savedTermios) != 0) throw new InvalidOperationException("stdin is not a terminal");
        Buffer.BlockCopy(_savedTermios, 0, _rawTermios, 0, _savedTermios.Length);
        cfmakeraw(_rawTermios);
        // Raw input, but keep output processing (\n → \r\n) and signals (Ctrl+C still quits).
        OrFlag(_rawTermios, OFlagOffset, OPOST | ONLCR);
        OrFlag(_rawTermios, LFlagOffset, ISIG);
        tcsetattr(StdIn, TCSANOW, _rawTermios);

        RefreshSize(force: true);
        // Straight to fd 1: .NET's console stream locks Console.Out on every write, which deadlocks against the
        // synchronized writer that Console.SetOut installs around this one.
        _out = new TrackingWriter(new StdOutStream(), this);
        Console.SetOut(_out);
        // Alternate screen, all mouse motion in SGR coordinates, bracketed paste.
        _out.Write("\x1b[?1049h\x1b[?1000h\x1b[?1002h\x1b[?1003h\x1b[?1006h\x1b[?2004h\x1b[H");
        _out.Flush();

        new Thread(ReadStdIn) { IsBackground = true, Name = "stdin reader" }.Start();
        new Thread(Parse) { IsBackground = true, Name = "input parser" }.Start();
        _flushTimer = new Timer(_ => Tick(), null, 8, 8);

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Restore();
        _signals.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => Restore()));
        _signals.Add(PosixSignalRegistration.Create(PosixSignal.SIGHUP, _ => Restore()));
        _signals.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, _ => Restore()));
        _signals.Add(PosixSignalRegistration.Create(PosixSignal.SIGWINCH, _ => RefreshSize(force: true)));
    }

    // Leaves the terminal as it was: mouse and paste modes off, cursor shown, main screen, original line settings.
    public void Restore()
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0) return;
        try
        {
            _flushTimer.Dispose();
            _out.Write("\x1b[?2004l\x1b[?1006l\x1b[?1003l\x1b[?1002l\x1b[?1000l\x1b[0m\x1b[?25h\x1b[?1049l");
            _out.Flush();
        }
        catch { /* the terminal is gone */ }
        tcsetattr(StdIn, TCSANOW, _savedTermios);
    }

    private void Tick()
    {
        _out.Flush();
        // A child process that inherits the terminal can leave it in line mode (and .NET resets it around such
        // children) — put raw mode back.
        if (++_ticks % 60 != 0) return;
        var now = new byte[256];
        if (tcgetattr(StdIn, now) == 0 && !now.AsSpan(0, 64).SequenceEqual(_rawTermios.AsSpan(0, 64)))
            tcsetattr(StdIn, TCSANOW, _rawTermios);
    }

    private void RefreshSize(bool force = false)
    {
        long now = Environment.TickCount64;
        if (!force && now - _sizeCheckedAt < 100) return;
        _sizeCheckedAt = now;
        try
        {
            int w = Console.WindowWidth, h = Console.WindowHeight;
            if (w > 0) _width = w;
            if (h > 0) _height = h;
        }
        catch { /* keep the last size */ }
    }

    public void SetCursorPosition(int left, int top)
    {
        _out.Write($"\x1b[{Math.Max(0, top) + 1};{Math.Max(0, left) + 1}H");
    }

    public void ShowCursor(bool visible)
    {
        if (visible == CursorVisible) return;
        _out.Write(visible ? "\x1b[?25h" : "\x1b[?25l");
    }

    private void SetCursorVisible(bool visible) => CursorVisible = visible;

    public void Clear() => _out.Write("\x1b[H\x1b[2J\x1b[3J");

    // Asks the terminal window for a size (honoured by xterm-compatible terminals; ignored elsewhere).
    public void RequestSize(int cols, int rows)
    {
        if (cols == RealWidth && rows == RealHeight) return;
        _out.Write($"\x1b[8;{rows};{cols}t");
        _out.Flush();
        Thread.Sleep(50);
        RefreshSize(force: true);
    }

    // The game is laid out for a fixed grid (on Windows the console window is resized to it). A smaller terminal would
    // wrap every line, so wait here — asking the player to enlarge it — until it fits.
    public void EnsureSize(int cols, int rows)
    {
        _centered = false;
        _offX = _offY = 0;
        RequestSize(cols, rows);
        bool shown = false;
        while (RealWidth < cols || RealHeight < rows + ReservedTopRows)
        {
            string[] lines =
            [
                L.T("The terminal window is too small for NaviDnD."),
                L.F("It needs at least {0} × {1} characters; now it is {2} × {3}.", cols, rows + ReservedTopRows, RealWidth, RealHeight),
                L.T("Enlarge the window or make the font smaller (Cmd and minus)."),
            ];
            Clear();
            for (int i = 0; i < lines.Length; i++)
            {
                SetCursorPosition(Math.Max(0, (Width - lines[i].Length) / 2), Math.Max(0, Height / 2 - 1 + i));
                _out.Write(lines[i].Length > Width ? lines[i][..Width] : lines[i]);
            }
            _out.Flush();
            shown = true;
            int w = Width, h = Height;
            for (int tick = 1; Width == w && Height == h; tick++)
            {
                lock (_queueLock) _queue.Clear();   // keys pressed meanwhile are not meant for the game
                Thread.Sleep(150);
                RefreshSize(force: true);
                // Ask again every second: after Cmd-minus (smaller font, same rows) the window fits the screen, and the
                // terminal (Terminal.app, iTerm2) grows it to the requested size by itself.
                if (tick % 7 == 0)
                {
                    _out.Write($"\x1b[8;{rows};{cols}t");
                    _out.Flush();
                }
            }
        }
        if (shown) Clear();
        _offX = (RealWidth - cols) / 2;
        _offY = ReservedTopRows + (RealHeight - ReservedTopRows - rows) / 2;
        (_layoutCols, _layoutRows) = (cols, rows);
        _centered = _offX > 0 || _offY > 0;
    }

    public void SetPointerShape(bool hand)
    {
        _out.Write(hand ? "\x1b]22;pointer\x1b\\" : "\x1b]22;default\x1b\\");
    }

    public void Flush() => _out.Flush();

    // ── Input queue (the ReadConsoleInput model) ──────────────────────────────────────────────────────────────

    public int Peek(ConsoleMouseReader.INPUT_RECORD[] buf)
    {
        _out.Flush();
        lock (_queueLock)
        {
            if (_queue.Count == 0) return 0;
            buf[0] = _queue[0];
            return 1;
        }
    }

    public int Read(ConsoleMouseReader.INPUT_RECORD[] buf)
    {
        lock (_queueLock)
        {
            if (_queue.Count == 0) return 0;
            buf[0] = _queue[0];
            _queue.RemoveAt(0);
            return 1;
        }
    }

    // Console.KeyAvailable semantics: non-key records ahead of the first key are dropped.
    public bool KeyAvailable
    {
        get
        {
            _out.Flush();
            lock (_queueLock)
            {
                while (_queue.Count > 0 && _queue[0].EventType != KeyEvent) _queue.RemoveAt(0);
                return _queue.Count > 0;
            }
        }
    }

    public ConsoleKeyInfo ReadKey()
    {
        while (true)
        {
            lock (_queueLock)
            {
                while (_queue.Count > 0)
                {
                    var r = _queue[0];
                    _queue.RemoveAt(0);
                    if (r.EventType == KeyEvent) return ToKeyInfo(r);
                }
            }
            _out.Flush();
            Thread.Sleep(5);
        }
    }

    private static ConsoleKeyInfo ToKeyInfo(ConsoleMouseReader.INPUT_RECORD r) =>
        new((char)r.UnicodeChar, (ConsoleKey)r.VirtualKeyCode,
            (r.KeyControlState & ShiftPressed) != 0, (r.KeyControlState & 0x0003) != 0, (r.KeyControlState & 0x000C) != 0);

    private void Enqueue(ConsoleMouseReader.INPUT_RECORD r)
    {
        lock (_queueLock) _queue.Add(r);
    }

    private void Key(ConsoleKey key, char ch, uint mods = 0)
    {
        Enqueue(new ConsoleMouseReader.INPUT_RECORD
        {
            EventType = KeyEvent, KeyDownFlag = 1, RepeatCount = 1,
            VirtualKeyCode = (ushort)key, UnicodeChar = ch, KeyControlState = mods,
        });
    }

    private void Mouse(short x, short y, uint buttons, uint flags)
    {
        var r = new ConsoleMouseReader.INPUT_RECORD { EventType = MouseEvent };
        r.MouseEvent.dwMousePosition.X = x;
        r.MouseEvent.dwMousePosition.Y = y;
        r.MouseEvent.dwButtonState = buttons;
        r.MouseEvent.dwEventFlags = flags;
        Enqueue(r);
    }

    // ── Byte reading and escape-sequence parsing ──────────────────────────────────────────────────────────────

    private void ReadStdIn()
    {
        var buf = new byte[4096];
        while (true)
        {
            nint n = read(StdIn, buf, buf.Length);
            if (n <= 0)
            {
                if (n < 0 && Marshal.GetLastPInvokeError() == 4) continue;   // EINTR
                break;
            }
            for (int i = 0; i < n; i++) _bytes.Add(buf[i]);
        }
    }

    private int _pushedBack = -1;   // a byte read one too far, returned before the queue

    private byte Next()
    {
        if (_pushedBack >= 0) { byte b = (byte)_pushedBack; _pushedBack = -1; return b; }
        return _bytes.Take();
    }

    private bool TryNext(out byte b, int timeoutMs = 30)
    {
        if (_pushedBack >= 0) { b = (byte)_pushedBack; _pushedBack = -1; return true; }
        return _bytes.TryTake(out b, timeoutMs);
    }

    private void Parse()
    {
        var paste = new StringBuilder();
        bool pasting = false;
        while (true)
        {
            byte b = Next();
            if (b == 0x1b)
            {
                if (!TryNext(out byte c)) { Key(ConsoleKey.Escape, '\x1b'); continue; }
                if (c == (byte)'[')
                {
                    var (prefix, pars, final) = ReadCsi();
                    if (pasting)
                    {
                        if (final == '~' && pars == "201") { pasting = false; EmitPaste(paste.ToString()); paste.Clear(); }
                        continue;
                    }
                    if (final == '~' && pars == "200") { pasting = true; continue; }
                    HandleCsi(prefix, pars, final);
                }
                else if (c == (byte)'O')
                {
                    if (TryNext(out byte f)) HandleSs3((char)f);
                }
                else if (c == 0x1b) { Key(ConsoleKey.Escape, '\x1b'); _pushedBack = c; }
                else HandleChar(DecodeUtf8(c), LeftAltPressed);
                continue;
            }
            int cp = DecodeUtf8(b);
            if (pasting) { paste.Append(char.ConvertFromUtf32(cp)); continue; }
            HandleChar(cp, 0);
        }
    }

    private (char prefix, string pars, char final) ReadCsi()
    {
        var sb = new StringBuilder();
        char prefix = '\0';
        while (TryNext(out byte b, 100))
        {
            char ch = (char)b;
            if (sb.Length == 0 && prefix == '\0' && ch is '<' or '?' or '>' or '=') { prefix = ch; continue; }
            if (b >= 0x40 && b <= 0x7e) return (prefix, sb.ToString(), ch);
            sb.Append(ch);
        }
        return (prefix, sb.ToString(), '\0');
    }

    private int DecodeUtf8(byte first)
    {
        int extra = first >= 0xf0 ? 3 : first >= 0xe0 ? 2 : first >= 0xc0 ? 1 : 0;
        if (extra == 0) return first;
        int cp = first & (0x3f >> extra);
        for (int i = 0; i < extra; i++)
        {
            if (!TryNext(out byte b, 50)) return '?';
            cp = (cp << 6) | (b & 0x3f);
        }
        return cp;
    }

    private void EmitPaste(string text)
    {
        // Pasted line breaks would submit the input — they become spaces.
        foreach (var rune in text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').EnumerateRunes())
            HandleChar(rune.Value, 0);
    }

    private void HandleChar(int cp, uint mods)
    {
        switch (cp)
        {
            case '\r': Key(ConsoleKey.Enter, '\r', mods); return;
            case '\n': Key(ConsoleKey.Enter, '\n', mods | LeftCtrlPressed); return;
            case '\t': Key(ConsoleKey.Tab, '\t', mods); return;
            case 0x7f: case 0x08: Key(ConsoleKey.Backspace, '\b', mods); return;
            case ' ': Key(ConsoleKey.Spacebar, ' ', mods); return;
            case 0: Key(ConsoleKey.Spacebar, '\0', mods | LeftCtrlPressed); return;
        }
        if (cp < 0x20)
        {
            Key(ConsoleKey.A + (cp - 1), (char)cp, mods | LeftCtrlPressed);   // Ctrl+A … Ctrl+Z
            return;
        }
        if (cp > 0xffff)
        {
            // Outside the BMP: deliver both surrogates as characters with no key code.
            string s = char.ConvertFromUtf32(cp);
            Key(0, s[0], mods);
            Key(0, s[1], mods);
            return;
        }
        char ch = (char)cp;
        var (key, shift) = KeyFor(ch);
        Key(key, ch, mods | (shift ? ShiftPressed : 0));
    }

    // US-layout virtual key for a character, like Windows reports it.
    private static (ConsoleKey key, bool shift) KeyFor(char ch)
    {
        if (ch is >= 'a' and <= 'z') return (ConsoleKey.A + (ch - 'a'), false);
        if (ch is >= 'A' and <= 'Z') return (ConsoleKey.A + (ch - 'A'), true);
        if (ch is >= '0' and <= '9') return (ConsoleKey.D0 + (ch - '0'), false);
        const string shifted = ")!@#$%^&*(";
        int d = shifted.IndexOf(ch);
        if (d >= 0) return (ConsoleKey.D0 + d, true);
        return ch switch
        {
            ';' => (ConsoleKey.Oem1, false), ':' => (ConsoleKey.Oem1, true),
            '=' => (ConsoleKey.OemPlus, false), '+' => (ConsoleKey.OemPlus, true),
            ',' => (ConsoleKey.OemComma, false), '<' => (ConsoleKey.OemComma, true),
            '-' => (ConsoleKey.OemMinus, false), '_' => (ConsoleKey.OemMinus, true),
            '.' => (ConsoleKey.OemPeriod, false), '>' => (ConsoleKey.OemPeriod, true),
            '/' => (ConsoleKey.Oem2, false), '?' => (ConsoleKey.Oem2, true),
            '`' => (ConsoleKey.Oem3, false), '~' => (ConsoleKey.Oem3, true),
            '[' => (ConsoleKey.Oem4, false), '{' => (ConsoleKey.Oem4, true),
            '\\' => (ConsoleKey.Oem5, false), '|' => (ConsoleKey.Oem5, true),
            ']' => (ConsoleKey.Oem6, false), '}' => (ConsoleKey.Oem6, true),
            '\'' => (ConsoleKey.Oem7, false), '"' => (ConsoleKey.Oem7, true),
            _ => (0, char.IsUpper(ch)),
        };
    }

    private static uint Modifiers(string pars)
    {
        // "1;5" — the second parameter is 1 + (shift 1, alt 2, ctrl 4).
        var parts = pars.Split(';');
        if (parts.Length < 2 || !int.TryParse(parts[1], out int m) || m < 2) return 0;
        m -= 1;
        return ((m & 1) != 0 ? ShiftPressed : 0) | ((m & 2) != 0 ? LeftAltPressed : 0) | ((m & 4) != 0 ? LeftCtrlPressed : 0);
    }

    private void HandleCsi(char prefix, string pars, char final)
    {
        if (prefix == '<' && final is 'M' or 'm') { HandleMouse(pars, final == 'M'); return; }
        uint mods = Modifiers(pars);
        // "code;mods u" (CSI u): a character with modifiers — the game's terminal sends Ctrl/Cmd with + and − this way,
        // which a terminal otherwise passes as a bare "=" or "-".
        if (final == 'u' && prefix == '\0' && int.TryParse(pars.Split(';')[0], out int code) && code is > 0x20 and < 0x7f)
        {
            HandleChar(code, mods);
            return;
        }
        ConsoleKey? key = final switch
        {
            'A' => ConsoleKey.UpArrow, 'B' => ConsoleKey.DownArrow, 'C' => ConsoleKey.RightArrow, 'D' => ConsoleKey.LeftArrow,
            'H' => ConsoleKey.Home, 'F' => ConsoleKey.End,
            'P' => ConsoleKey.F1, 'Q' => ConsoleKey.F2, 'R' => ConsoleKey.F3, 'S' => ConsoleKey.F4,
            'Z' => ConsoleKey.Tab,
            '~' => int.TryParse(pars.Split(';')[0], out int n) ? n switch
            {
                1 or 7 => ConsoleKey.Home, 2 => ConsoleKey.Insert, 3 => ConsoleKey.Delete, 4 or 8 => ConsoleKey.End,
                5 => ConsoleKey.PageUp, 6 => ConsoleKey.PageDown,
                11 => ConsoleKey.F1, 12 => ConsoleKey.F2, 13 => ConsoleKey.F3, 14 => ConsoleKey.F4, 15 => ConsoleKey.F5,
                17 => ConsoleKey.F6, 18 => ConsoleKey.F7, 19 => ConsoleKey.F8, 20 => ConsoleKey.F9, 21 => ConsoleKey.F10,
                23 => ConsoleKey.F11, 24 => ConsoleKey.F12,
                _ => null,
            } : null,
            _ => null,
        };
        if (key is not { } k) return;
        if (final == 'Z') mods |= ShiftPressed;
        Key(k, k == ConsoleKey.Tab ? '\t' : '\0', mods);
    }

    private void HandleSs3(char final)
    {
        ConsoleKey? key = final switch
        {
            'A' => ConsoleKey.UpArrow, 'B' => ConsoleKey.DownArrow, 'C' => ConsoleKey.RightArrow, 'D' => ConsoleKey.LeftArrow,
            'H' => ConsoleKey.Home, 'F' => ConsoleKey.End,
            'P' => ConsoleKey.F1, 'Q' => ConsoleKey.F2, 'R' => ConsoleKey.F3, 'S' => ConsoleKey.F4,
            'M' => ConsoleKey.Enter,
            _ => null,
        };
        if (key is { } k) Key(k, k == ConsoleKey.Enter ? '\r' : '\0');
    }

    // SGR mouse: "b;x;y" + M (press/motion) or m (release); coordinates are 1-based.
    private void HandleMouse(string pars, bool press)
    {
        var p = pars.Split(';');
        if (p.Length < 3 || !int.TryParse(p[0], out int b) || !int.TryParse(p[1], out int x) || !int.TryParse(p[2], out int y)) return;
        short cx = (short)(x - 1 - _offX), cy = (short)(y - 1 - _offY);
        if (cx < 0 || cy < 0) return;   // outside the game (the margins around it in full screen)
        if ((b & 64) != 0)
        {
            int delta = (b & 1) == 0 ? 120 : -120;   // like WHEEL_DELTA: positive is away from the user
            // Horizontal scrolling (trackpad swipe): like MOUSE_HWHEELED, positive is to the right.
            if ((b & 3) > 1) Mouse(cx, cy, (uint)(-delta << 16), MouseHWheeled);
            else Mouse(cx, cy, (uint)(delta << 16), MouseWheeled);
            return;
        }
        uint button = (b & 3) switch { 0 => LeftButton, 1 => MiddleButton, 2 => RightButton, _ => 0 };
        if ((b & 32) != 0)
        {
            // Motion: the low bits name the held button (3 — none).
            _buttons = button;
            Mouse(cx, cy, _buttons, MouseMoved);
            return;
        }
        if (press)
        {
            _buttons |= button;
            uint flags = 0;
            if (button == LeftButton)
            {
                long now = Environment.TickCount64;
                bool dbl = !_lastWasDouble && now - _lastLeftPress < 500 && _lastLeftPos == (cx, cy);
                _lastWasDouble = dbl;
                _lastLeftPress = now;
                _lastLeftPos = (cx, cy);
                if (dbl) flags = DoubleClick;
            }
            Mouse(cx, cy, _buttons, flags);
        }
        else
        {
            _buttons &= ~button;
            Mouse(cx, cy, _buttons, 0);
        }
    }

    // ── Output with cursor tracking ───────────────────────────────────────────────────────────────────────────

    private sealed class TrackingWriter(Stream stdout, UnixTerminal term) : TextWriter
    {
        private readonly StreamWriter _inner = new(stdout, new UTF8Encoding(false), 1 << 16) { AutoFlush = false };
        private readonly object _lock = new();
        private enum State { Normal, Esc, Csi, Osc, OscEsc }
        private State _state;
        private readonly StringBuilder _params = new();
        private char _highSurrogate;
        private bool _pendingWrap;
        private int _savedCol, _savedRow;
        public int Col { get; private set; }
        public int Row { get; private set; }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_lock) Track(value);
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            lock (_lock) { foreach (char c in value) Track(c); }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            lock (_lock) { for (int i = 0; i < count; i++) Track(buffer[index + i]); }
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            lock (_lock) { foreach (char c in buffer) Track(c); }
        }

        // After a line start the cursor goes to the game's left edge.
        private void ToLeftEdge()
        {
            if (term._offX > 0) _inner.Write($"\x1b[{term._offX}C");
        }

        public override void WriteLine(string? value) { Write(value); Write('\n'); }

        public override void Flush()
        {
            lock (_lock)
            {
                try { _inner.Flush(); } catch (IOException) { /* the terminal is gone */ }
            }
        }

        // Tracks the cursor (in the game's grid) and writes the character; cursor moves are shifted to the origin.
        private void Track(char c)
        {
            switch (_state)
            {
                case State.Esc:
                    _state = State.Normal;
                    if (c == '[') { _state = State.Csi; _params.Clear(); return; }   // written with its final byte
                    _inner.Write('\x1b');
                    _inner.Write(c);
                    if (c == ']') _state = State.Osc;
                    else if (c == '7') { _savedCol = Col; _savedRow = Row; }
                    else if (c == '8') { Col = _savedCol; Row = _savedRow; _pendingWrap = false; }
                    else if (c == 'M') { Row = Math.Max(0, Row - 1); _pendingWrap = false; }
                    return;
                case State.Csi:
                    if (c >= 0x40 && c <= 0x7e) { _state = State.Normal; Csi(c); }
                    else _params.Append(c);
                    return;
                case State.Osc:
                    _inner.Write(c);
                    if (c == '\a') _state = State.Normal;
                    else if (c == '\x1b') _state = State.OscEsc;
                    return;
                case State.OscEsc:
                    _inner.Write(c);
                    _state = c == '\\' ? State.Normal : State.Osc;
                    return;
            }
            if (c == '\x1b') { _state = State.Esc; return; }
            _inner.Write(c);
            switch (c)
            {
                case '\r': Col = 0; _pendingWrap = false; ToLeftEdge(); return;
                case '\n': Col = 0; Row = Math.Min(Row + 1, term._height - 1); _pendingWrap = false; ToLeftEdge(); return;
                case '\b': Col = Math.Max(0, Col - 1); _pendingWrap = false; return;
                case '\t': Col = Math.Min(term._width - 1, (Col / 8 + 1) * 8); return;
            }
            if (c < 0x20 || c == 0x7f) return;
            int cp = c;
            if (char.IsHighSurrogate(c)) { _highSurrogate = c; return; }
            if (char.IsLowSurrogate(c))
            {
                if (_highSurrogate == '\0') return;
                cp = char.ConvertToUtf32(_highSurrogate, c);
                _highSurrogate = '\0';
            }
            int w = CharWidth(cp);
            if (w == 0) return;
            int width = term._width;
            if (_pendingWrap || Col + w > width)
            {
                Col = 0;
                Row = Math.Min(Row + 1, term._height - 1);
                _pendingWrap = false;
            }
            Col += w;
            if (Col >= width) { Col = width - 1; _pendingWrap = true; }
        }

        private void Csi(char final)
        {
            string raw = _params.ToString();
            var p = raw.TrimStart('?').Split(';');
            int Arg(int i, int def) => i < p.Length && int.TryParse(p[i], out int v) && v > 0 ? v : def;
            // Absolute moves go to the game's grid inside the terminal; the rest pass unchanged.
            _inner.Write(final switch
            {
                'H' or 'f' when !raw.StartsWith('?') => $"\x1b[{Arg(0, 1) + term._offY};{Arg(1, 1) + term._offX}H",
                'G' => $"\x1b[{Arg(0, 1) + term._offX}G",
                'd' => $"\x1b[{Arg(0, 1) + term._offY}d",
                _ => $"\x1b[{raw}{final}",
            });
            if (final is 'E' or 'F') ToLeftEdge();
            switch (final)
            {
                case 'H': case 'f': Row = Arg(0, 1) - 1; Col = Arg(1, 1) - 1; break;
                case 'A': Row = Math.Max(0, Row - Arg(0, 1)); break;
                case 'B': Row = Math.Min(term._height - 1, Row + Arg(0, 1)); break;
                case 'C': Col = Math.Min(term._width - 1, Col + Arg(0, 1)); break;
                case 'D': Col = Math.Max(0, Col - Arg(0, 1)); break;
                case 'E': Row = Math.Min(term._height - 1, Row + Arg(0, 1)); Col = 0; break;
                case 'F': Row = Math.Max(0, Row - Arg(0, 1)); Col = 0; break;
                case 'G': Col = Arg(0, 1) - 1; break;
                case 'd': Row = Arg(0, 1) - 1; break;
                case 's': _savedCol = Col; _savedRow = Row; break;
                case 'u': Col = _savedCol; Row = _savedRow; break;
                case 'h' or 'l' when _params.ToString() == "?25": term.SetCursorVisible(final == 'h'); return;
                default: return;
            }
            _pendingWrap = false;
        }
    }

    // Terminal cell width of a code point: 0 for combining marks and joiners, 2 for East Asian wide and emoji.
    public static int CharWidth(int cp)
    {
        if (cp is >= 0x0300 and <= 0x036f or >= 0x200b and <= 0x200f or >= 0xfe00 and <= 0xfe0f or 0x20e3) return 0;
        if (cp is >= 0x1100 and <= 0x115f or >= 0x2e80 and <= 0x303e or >= 0x3041 and <= 0x33ff or >= 0x3400 and <= 0x4dbf
            or >= 0x4e00 and <= 0x9fff or >= 0xa000 and <= 0xa4cf or >= 0xac00 and <= 0xd7a3 or >= 0xf900 and <= 0xfaff
            or >= 0xfe30 and <= 0xfe4f or >= 0xff00 and <= 0xff60 or >= 0xffe0 and <= 0xffe6
            or >= 0x1f300 and <= 0x1f64f or >= 0x1f680 and <= 0x1f6ff or >= 0x1f900 and <= 0x1f9ff or >= 0x20000 and <= 0x3fffd)
            return 2;
        return 1;
    }

    private sealed class StdOutStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                nint n = write(1, ref MemoryMarshal.GetReference(buffer), buffer.Length);
                if (n < 0)
                {
                    if (Marshal.GetLastPInvokeError() == 4) continue;   // EINTR
                    throw new IOException("stdout closed");
                }
                buffer = buffer[(int)n..];
            }
        }
    }

    // ── termios ───────────────────────────────────────────────────────────────────────────────────────────────

    private const int TCSANOW = 0;
    private static readonly bool Mac = OperatingSystem.IsMacOS();
    // struct termios: tcflag_t is 8 bytes on macOS, 4 on Linux; c_oflag is the 2nd field, c_lflag the 4th.
    private static int OFlagOffset => Mac ? 8 : 4;
    private static int LFlagOffset => Mac ? 24 : 12;
    private const uint OPOST = 0x1;
    private static uint ONLCR => Mac ? 0x2u : 0x4u;
    private static uint ISIG => Mac ? 0x80u : 0x1u;

    private static void OrFlag(byte[] termios, int offset, uint flag)
    {
        uint v = BitConverter.ToUInt32(termios, offset);
        BitConverter.TryWriteBytes(termios.AsSpan(offset, 4), v | flag);
    }

    [DllImport("libc", SetLastError = true)] private static extern int tcgetattr(int fd, byte[] termios);
    [DllImport("libc", SetLastError = true)] private static extern int tcsetattr(int fd, int action, byte[] termios);
    [DllImport("libc")] private static extern void cfmakeraw(byte[] termios);
    [DllImport("libc", SetLastError = true)] private static extern nint read(int fd, byte[] buf, nint count);
    [DllImport("libc", SetLastError = true)] private static extern nint write(int fd, ref byte buf, nint count);
}
