using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

// Текст из буфера обмена Windows (user32, без WinForms) — для вставки Ctrl+V / Shift+Insert в поля
// анкеты и настроек: консоль сама вставку не делает (ввод читаем напрямую из буфера консоли).
public static class ClipboardText
{
    private const uint CF_UNICODETEXT = 13;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr hMem);

    // Текст буфера одной строкой (переносы → пробелы) или "" — пусто/не текст/буфер занят.
    public static string Get()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return "";
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero)) { Thread.Sleep(20); continue; } // буфер занят другим окном
            try
            {
                IntPtr handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero) return "";
                IntPtr ptr = GlobalLock(handle);
                if (ptr == IntPtr.Zero) return "";
                try
                {
                    string text = Marshal.PtrToStringUni(ptr) ?? "";
                    return text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
                }
                finally { GlobalUnlock(handle); }
            }
            finally { CloseClipboard(); }
        }
        return "";
    }

    // Нажатие — вставка: Ctrl+V или Shift+Insert.
    public static bool IsPasteKey(ConsoleKeyInfo key) =>
        (key.Key == ConsoleKey.V && (key.Modifiers & ConsoleModifiers.Control) != 0)
        || (key.Key == ConsoleKey.Insert && (key.Modifiers & ConsoleModifiers.Shift) != 0);
}
