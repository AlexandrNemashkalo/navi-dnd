using System.Runtime.InteropServices;

namespace NaviDnD.UiTests;

// Positions the driver's OWN console window (not the game) - presses it to the right edge of the screen so as
// not to overlap the game window, which GameConsole.MoveToTopLeft presses to the upper left corner.
internal static class SelfWindow
{
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int SM_CXSCREEN = 0;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;

    // Fixed width of the driver window (not the game), in characters - does not depend on what width the
    // terminal started with.
    public const int FixedWidthColumns = 60;

    public static void ApplyFixedWidth()
    {
        try { Console.WindowWidth = FixedWidthColumns; }
        catch { /*don’t have your own console - not critical*/ }
    }

    public static void MoveToRightEdge()
    {
        IntPtr hwnd = GetConsoleWindow();
        if (hwnd == IntPtr.Zero) return;
        if (!GetWindowRect(hwnd, out var rect)) return;

        int width = rect.Right - rect.Left;
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int x = Math.Max(0, screenWidth - width);

        SetWindowPos(hwnd, IntPtr.Zero, x, 0, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    }
}
