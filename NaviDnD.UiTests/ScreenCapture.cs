using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NaviDnD.UiTests;

// Снимает клиентскую область окна консоли через PrintWindow и сэмплирует пиксель по
// символьным координатам (col, row) — так проверяется РЕАЛЬНЫЙ отрендеренный цвет,
// в обход легаси Windows Console API (ReadConsoleOutputAttribute), который не отражает
// 24-битный ANSI. Вызывать ТОЛЬКО пока консоль игры активна (внутри GameConsole.WithGameConsole) —
// GetConsoleWindow()/GetStdHandle() возвращают хендлы текущей подключённой консоли.
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

    // Возвращает цвет "чернил" символьной ячейки (col, row), 0-based — те же координаты,
    // что использует ConsoleBufferReader/GameConsole. Сэмплировать один пиксель в центре
    // ячейки ненадёжно: тонкие штрихи буквы (особенно кириллицы) часто не попадают ровно
    // в центр, и получаем фон вместо текста. Вместо этого сканируем ВСЮ ячейку и берём
    // пиксель, максимально непохожий на background, — это и есть настоящий цвет текста,
    // устойчиво к неточному попаданию и небольшому рассогласованию координат/DPI.
    // Бросает исключение с точным описанием, на каком шаге не удалось (а не молча null) —
    // это единственный способ понять причину в среде, которую нельзя увидеть глазами.
    public static (byte r, byte g, byte b) SampleCellColor(int col, int row, (byte r, byte g, byte b) background)
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
