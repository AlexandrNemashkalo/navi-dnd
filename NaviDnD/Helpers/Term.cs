namespace NaviDnD.Helpers;

// The game's view of the terminal. On Windows (and whenever UnixTerminal is not running, e.g. in tests) every member is
// the matching System.Console call. On macOS/Linux UnixTerminal owns stdin and stdout: .NET's own cursor getters would
// query the terminal through stdin and race the input reader, so the cursor position and visibility are tracked here.
internal static class Term
{
    private static UnixTerminal? Unix => UnixTerminal.Current;

    public static bool CursorVisible
    {
        get => Unix?.CursorVisible ?? (OperatingSystem.IsWindows() && Console.CursorVisible);
        set { if (Unix is { } u) u.ShowCursor(value); else Console.CursorVisible = value; }
    }

    public static int CursorLeft
    {
        get => Unix?.CursorLeft ?? Console.CursorLeft;
        set { if (Unix is { } u) u.SetCursorPosition(value, u.CursorTop); else Console.CursorLeft = value; }
    }

    public static int CursorTop
    {
        get => Unix?.CursorTop ?? Console.CursorTop;
        set { if (Unix is { } u) u.SetCursorPosition(u.CursorLeft, value); else Console.CursorTop = value; }
    }

    public static void SetCursorPosition(int left, int top)
    {
        if (Unix is { } u) u.SetCursorPosition(left, top);
        else Console.SetCursorPosition(left, top);
    }

    public static int WindowWidth => Unix?.Width ?? Console.WindowWidth;
    public static int WindowHeight => Unix?.Height ?? Console.WindowHeight;
    public static int BufferWidth => Unix?.Width ?? Console.BufferWidth;
    public static int BufferHeight => Unix?.Height ?? Console.BufferHeight;

    public static bool KeyAvailable => Unix?.KeyAvailable ?? Console.KeyAvailable;

    public static ConsoleKeyInfo ReadKey(bool intercept = false) => Unix?.ReadKey() ?? Console.ReadKey(intercept);

    // Drops the keys typed ahead (the old "while (Console.KeyAvailable) Console.ReadKey(true)").
    public static void FlushKeys()
    {
        while (KeyAvailable) ReadKey(true);
    }

    public static void Clear()
    {
        if (Unix is { } u) u.Clear();
        else Console.Clear();
    }
}
