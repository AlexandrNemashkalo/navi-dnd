using System.Runtime.InteropServices;

namespace NaviDnD.UiTests;

// Позиционирует СОБСТВЕННОЕ окно консоли драйвера (не игры) — прижимает к правому
// краю экрана, чтобы не перекрывать окно игры, которое GameConsole.MoveToTopLeft
// прижимает к левому верхнему углу.
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

    // Фиксированная ширина окна драйвера (не игры), в символах — не зависит от того,
    // с какой ширины стартовал терминал.
    public const int FixedWidthColumns = 60;

    public static void ApplyFixedWidth()
    {
        try { Console.WindowWidth = FixedWidthColumns; }
        catch { /* нет своей консоли — не критично */ }
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
