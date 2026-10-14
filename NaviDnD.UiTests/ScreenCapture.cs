using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NaviDnD.UiTests;

// Captures the client area of the console window through PrintWindow and samples the pixel at character
// coordinates (col, row) - this is how the REAL rendered color is checked, bypassing the legacy Windows Console
// API (ReadConsoleOutputAttribute), which does not reflect the 24-bit ANSI. Call ONLY while the game console is
// active (inside GameConsole.WithGameConsole) - GetConsoleWindow()/GetStdHandle() return handles for the
// currently connected console.
[SupportedOSPlatform("windows")]
internal static class ScreenCapture
{
    private const uint PW_CLIENTONLY = 0x00000001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll")]
    private static extern bool GetCurrentConsoleFont(IntPtr hConsoleOutput, bool bMaximumWindow, out CONSOLE_FONT_INFO info);

    [DllImport("kernel32.dll")]
    private static extern COORD GetConsoleFontSize(IntPtr hConsoleOutput, int nFont);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_FONT_INFO { public int nFont; public COORD dwFontSize; }

    // Returns the “ink” color of a character cell (col, row), 0-based - the same coordinates that
    // ConsoleBufferReader/GameConsole. uses. Sampling one pixel in the center of a cell is unreliable: thin
    // strokes of a letter (especially Cyrillic) often do not fall exactly in the center, and we get a
    // background instead of text. Instead, we scan the ENTIRE cell and take a pixel that is as different as
    // possible from background - this is the real color of the text, resistant to inaccurate hits and small
    // coordinate mismatches / DPI. Throws an exception with a precise description of what step failed (and not
    // silently null) - this is the only way to understand the reason in an environment that cannot be seen with
    // the eyes.
    public static (byte r, byte g, byte b) SampleCellColor(int col, int row, (byte r, byte g, byte b) background,
        Action<Bitmap, Rectangle>? inspect = null)
    {
        IntPtr hwnd = GetConsoleWindow();
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("GetConsoleWindow() вернул NULL — у консоли нет оконного хендла (возможно, безголовая/виртуальная сессия без GDI-окон).");

        if (!GetClientRect(hwnd, out var rect))
            throw new InvalidOperationException($"GetClientRect провалился (Win32Error={Marshal.GetLastWin32Error()}).");

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"GetClientRect вернул нулевые размеры ({width}x{height}).");

        using var stdOut = ConsoleHandles.OpenOutput();
        if (!GetCurrentConsoleFont(stdOut.DangerousGetHandle(), false, out var fontInfo))
            throw new InvalidOperationException($"GetCurrentConsoleFont провалился (Win32Error={Marshal.GetLastWin32Error()}).");

        var cellSize = GetConsoleFontSize(stdOut.DangerousGetHandle(), fontInfo.nFont);
        if (cellSize.X <= 0 || cellSize.Y <= 0)
            throw new InvalidOperationException($"GetConsoleFontSize вернул нулевой размер ячейки ({cellSize.X}x{cellSize.Y}).");

        int cellLeft = col * cellSize.X;
        int cellTop = row * cellSize.Y;
        if (cellLeft < 0 || cellTop < 0 || cellLeft >= width || cellTop >= height)
            throw new InvalidOperationException($"Ячейка ({cellLeft},{cellTop}) вне клиентской области ({width}x{height}).");
        int cellRight = Math.Min(width, cellLeft + cellSize.X);
        int cellBottom = Math.Min(height, cellTop + cellSize.Y);

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = graphics.GetHdc();
            bool ok;
            try { ok = PrintWindow(hwnd, hdc, PW_CLIENTONLY); }
            finally { graphics.ReleaseHdc(hdc); }
            if (!ok)
                throw new InvalidOperationException($"PrintWindow вернул false (Win32Error={Marshal.GetLastWin32Error()}).");
        }

        inspect?.Invoke(bitmap, new Rectangle(cellLeft, cellTop, cellRight - cellLeft, cellBottom - cellTop));
        Color best = default;
        int bestDist = -1;
        for (int y = cellTop; y < cellBottom; y++)
        {
            for (int x = cellLeft; x < cellRight; x++)
            {
                var p = bitmap.GetPixel(x, y);
                int dist = Math.Abs(p.R - background.r) + Math.Abs(p.G - background.g) + Math.Abs(p.B - background.b);
                if (dist > bestDist) { bestDist = dist; best = p; }
            }
        }
        return (best.R, best.G, best.B);
    }
}
