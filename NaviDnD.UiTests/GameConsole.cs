using System.Runtime.InteropServices;

namespace NaviDnD.UiTests;

// Temporarily connects to the console of the REAL game process to read its buffer or send a key/mouse click
// there - just as a Windows- console from a real user would do (WriteConsoleInput is the same API that the game
// itself reads input through ReadConsoleInput). Immediately after - disconnects and returns to its console.
internal static class GameConsole
{
    // IMPORTANT: no more than the actual width of the console buffer (=WindowWidth, see
    // ConsoleSetup.SetConsoleConfig). ReadConsoleOutputCharacterW does not stop at the line boundary - a
    // request longer than the buffer flows to the beginning of the NEXT line, and then the “found” column has
    // no relation to the real screen (this is exactly how a bug with pixel sampling was found - they were
    // looking for “Rustem” outside of its lines). RightBorderCol+1 is the exact justified width of the frame.
    public const int RightBorderCol = 148;
    public const int TitleReadWidth = RightBorderCol + 1;
    public const int ScanRowCount = 90;

    private const uint ATTACH_PARENT_PROCESS = unchecked((uint)-1);
    private const ushort KEY_EVENT_TYPE = 0x0001;
    private const ushort MOUSE_EVENT_TYPE = 0x0002;
    private const uint LEFT_BUTTON = 0x0001;
    private const uint MOUSE_MOVED = 0x0001; // dwEventFlags - the same value as LEFT_BUTTON, but this is a different field

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    // IMPORTANT: without explicit CharSet.Unicode the default of P/Invoke is CharSet.Ansi, which resolves the
    // function name in WriteConsoleInputA - the same INPUT_RECORD, but with ANSI- semantics of character
    // fields, which is why wVirtualKeyCode=0 + arbitrary UnicodeChar (our SendText for Cyrillic alphabet) turns
    // into crap when read through Console.ReadKey() on the game side (tested on a real fault: “looking for
    // sweat” → “8IC ?>B”). WriteConsoleInputW does not do this.
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WriteConsoleInputW")]
    private static extern bool WriteConsoleInput(IntPtr hConsoleInput, INPUT_RECORD[] lpBuffer, uint nLength, out uint lpNumberOfEventsWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;

    // INPUT_RECORD: EventType(2)+pad(2) + union(16). Union is marked for both options - KEY_EVENT_RECORD and
    // MOUSE_EVENT_RECORD - the same as in Windows Console API.
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;

        [FieldOffset(4)] public int bKeyDown;
        [FieldOffset(8)] public ushort wRepeatCount;
        [FieldOffset(10)] public ushort wVirtualKeyCode;
        [FieldOffset(12)] public ushort wVirtualScanCode;
        [FieldOffset(14)] public char UnicodeChar;
        [FieldOffset(16)] public uint dwControlKeyStateKey;

        [FieldOffset(4)] public short MouseX;
        [FieldOffset(6)] public short MouseY;
        [FieldOffset(8)] public uint dwButtonState;
        [FieldOffset(12)] public uint dwControlKeyStateMouse;
        [FieldOffset(16)] public uint dwEventFlags;
    }

    private static void WithGameConsole(int processId, Action action)
    {
        FreeConsole();
        if (!AttachConsole((uint)processId))
            throw new InvalidOperationException($"Не удалось подключиться к консоли процесса {processId}.");
        try { action(); }
        finally
        {
            FreeConsole();
            AttachConsole(ATTACH_PARENT_PROCESS);
        }
    }

    public static string ReadRow(int processId, int row, int length)
    {
        string result = "";
        WithGameConsole(processId, () => result = ConsoleBufferReader.ReadRow(row, length));
        return result;
    }

    public static ushort[] ReadRowAttributes(int processId, int row, int length)
    {
        ushort[] result = [];
        WithGameConsole(processId, () => result = ConsoleBufferReader.ReadRowAttributes(row, length));
        return result;
    }

    public static string CellShape(int processId, int col, int row)
    {
        string shape = "";
        WithGameConsole(processId, () => ScreenCapture.SampleCellColor(col, row, (0, 0, 0), (bitmap, rect) =>
        {
            var levels = new List<int>();
            for (int y = rect.Top; y < rect.Bottom; y++)
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    levels.Add(pixel.R + pixel.G + pixel.B);
                }
            int threshold = (levels.Min() + levels.Max()) / 2;
            shape = string.Join("\n", levels.Chunk(rect.Width)
                .Select(line => new string(line.Select(value => value > threshold ? '#' : '.').ToArray())));
        }));
        return shape;
    }

    // Real rendered color (screenshot + pixel sampling) - bypasses legacy attributes, which do not reflect the
    // 24-bit ANSI used by the game. Throws an exception with the exact reason on failure (see
    // ScreenCapture.SampleCellColor).
    public static (byte r, byte g, byte b) SampleCellColor(int processId, int col, int row, (byte r, byte g, byte b) background)
    {
        (byte, byte, byte) result = default;
        WithGameConsole(processId, () => result = ScreenCapture.SampleCellColor(col, row, background));
        return result;
    }

    public static List<string> ReadRows(int processId, int startRow, int rowCount, int length)
    {
        var rows = new List<string>();
        WithGameConsole(processId, () =>
        {
            for (int r = startRow; r < startRow + rowCount; r++)
            {
                try { rows.Add(ConsoleBufferReader.ReadRow(r, length)); }
                catch { rows.Add(""); }
            }
        });
        return rows;
    }

    // Moves the game console window to the top left corner of the screen (without resizing) so that it is
    // predictably visible in its entirety during testing.
    public static void MoveToTopLeft(int processId)
    {
        WithGameConsole(processId, () =>
        {
            IntPtr hwnd = GetConsoleWindow();
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
        });
    }

    // Sends KEY_DOWN - just like a real keystroke.
    public static void SendKey(int processId, ConsoleKey key)
    {
        WithGameConsole(processId, () =>
        {
            using var handle = ConsoleHandles.OpenInput();
            var record = new INPUT_RECORD
            {
                EventType = KEY_EVENT_TYPE,
                bKeyDown = 1,
                wRepeatCount = 1,
                wVirtualKeyCode = (ushort)key,
            };
            WriteConsoleInput(handle.DangerousGetHandle(), [record], 1, out _);
        });
    }

    // Sends a string as the sequence KEY_DOWN with UnicodeChar (without wVirtualKeyCode) - InputBox.GetInput()
    // types text by key.KeyChar (see its default- thread), and not by key code, so this is enough to enter
    // arbitrary text, including Cyrillic.
    public static void SendText(int processId, string text)
    {
        WithGameConsole(processId, () =>
        {
            using var handle = ConsoleHandles.OpenInput();
            foreach (char c in text)
            {
                var record = new INPUT_RECORD
                {
                    EventType = KEY_EVENT_TYPE,
                    bKeyDown = 1,
                    wRepeatCount = 1,
                    UnicodeChar = c,
                };
                WriteConsoleInput(handle.DangerousGetHandle(), [record], 1, out _);
            }
        });
    }

    // Sends mouse MOVE to coordinates (col, row) without a click - what the hover responds to
    // (highlight/cursor) via DrainMouseEvents().latestMove in the game itself.
    public static void SendMouseMove(int processId, int col, int row)
    {
        WithGameConsole(processId, () =>
        {
            using var handle = ConsoleHandles.OpenInput();
            var record = new INPUT_RECORD
            {
                EventType = MOUSE_EVENT_TYPE,
                MouseX = (short)col,
                MouseY = (short)row,
                dwEventFlags = MOUSE_MOVED,
            };
            WriteConsoleInput(handle.DangerousGetHandle(), [record], 1, out _);
        });
    }

    // Sends a left-click on the coordinates (col, row) - symbolic console coordinates, 0-based, just as
    // DrainMouseEvents() sees them in the game itself.
    public static void SendLeftClick(int processId, int col, int row)
    {
        WithGameConsole(processId, () =>
        {
            using var handle = ConsoleHandles.OpenInput();
            var record = new INPUT_RECORD
            {
                EventType = MOUSE_EVENT_TYPE,
                MouseX = (short)col,
                MouseY = (short)row,
                dwButtonState = LEFT_BUTTON,
            };
            WriteConsoleInput(handle.DangerousGetHandle(), [record], 1, out _);
        });
    }

    public static void SendMouseWheel(int processId, int col, int row, int notches)
    {
        WithGameConsole(processId, () =>
        {
            using var handle = ConsoleHandles.OpenInput();
            var record = new INPUT_RECORD { EventType = MOUSE_EVENT_TYPE,
                MouseX = (short)col, MouseY = (short)row,
                dwButtonState = unchecked((uint)(notches * 120 << 16)), dwEventFlags = 0x0004 };
            Assert.True(WriteConsoleInput(handle.DangerousGetHandle(), [record], 1, out _));
        });
    }

    // One queued gesture catches bugs where polling consumes movement before starting the drag.
    public static void SendDrag(int processId, int fromCol, int fromRow, int toCol, int toRow)
    {
        WithGameConsole(processId, () =>
        {
            var events = new List<INPUT_RECORD>();
            events.Add(new INPUT_RECORD { EventType = MOUSE_EVENT_TYPE,
                MouseX = (short)fromCol, MouseY = (short)fromRow, dwButtonState = LEFT_BUTTON });
            for (int step = 1; step <= 8; step++)
                events.Add(new INPUT_RECORD { EventType = MOUSE_EVENT_TYPE,
                    MouseX = (short)(fromCol + (toCol - fromCol) * step / 8),
                    MouseY = (short)(fromRow + (toRow - fromRow) * step / 8),
                    dwButtonState = LEFT_BUTTON, dwEventFlags = MOUSE_MOVED });
            events.Add(new INPUT_RECORD { EventType = MOUSE_EVENT_TYPE,
                MouseX = (short)toCol, MouseY = (short)toRow, dwButtonState = 0 });
            using var handle = ConsoleHandles.OpenInput();
            Assert.True(WriteConsoleInput(handle.DangerousGetHandle(), events.ToArray(), (uint)events.Count, out var written));
            Assert.Equal((uint)events.Count, written);
        });
    }

    public static bool WaitForRowContains(int processId, int row, int length, string expected, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            string line;
            try { line = ReadRow(processId, row, length); }
            catch { line = ""; }
            if (line.Contains(expected)) return true;
            Thread.Sleep(150);
        }
        return false;
    }

    // The same search, but by range of lines - for markers that are drawn not in the screen title (line 1), but
    // deeper inside the card (for example, the title of the active subtab).
    public static bool WaitForAnyRowContains(int processId, int startRow, int rowCount, int length, string expected, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            List<string> rows;
            try { rows = ReadRows(processId, startRow, rowCount, length); }
            catch { rows = []; }
            if (rows.Any(r => r.Contains(expected))) return true;
            Thread.Sleep(150);
        }
        return false;
    }

    // Like WaitForAnyRowContains, but with an arbitrary condition on ALL lines at once - needed when a simple
    // “contains the substring X” gives a false positive on an OLD, not yet redrawn screen (for example, “Long
    // Sword” may already be visible in the regular inventory list BEFORE the patch moves it to an equipment
    // slot - Contains("Long Sword") will work instantly on outdated data, and this method only works when the
    // CONDITION is met).
    public static bool WaitForRowsMatch(int processId, int startRow, int rowCount, int length, Func<List<string>, bool> predicate, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            List<string> rows;
            try { rows = ReadRows(processId, startRow, rowCount, length); }
            catch { rows = []; }
            if (predicate(rows)) return true;
            Thread.Sleep(150);
        }
        return false;
    }

    // Like WaitForAnyRowContains, but for cases with several mutually exclusive outcomes (for example, the
    // result of a die roll is unpredictable - “Success”/“Failure”/crit). Returns the candidate that actually
    // appeared on the screen, or null by timeout.
    public static string? WaitForAnyRowContainsAny(int processId, int startRow, int rowCount, int length, IReadOnlyList<string> candidates, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            List<string> rows;
            try { rows = ReadRows(processId, startRow, rowCount, length); }
            catch { rows = []; }
            foreach (var candidate in candidates)
                if (rows.Any(r => r.Contains(candidate)))
                    return candidate;
            Thread.Sleep(150);
        }
        return null;
    }
}
