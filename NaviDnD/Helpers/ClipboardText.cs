using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

// Текст из буфера обмена Windows (user32, без WinForms) — для вставки Ctrl+V / Shift+Insert в поля
// анкеты и настроек: консоль сама вставку не делает (ввод читаем напрямую из буфера консоли).
// macOS: pbpaste; Linux: wl-paste / xclip / xsel, если есть, иначе ничего. Там терминал вставляет и сам
// (Cmd+V, Ctrl+Shift+V) — текст приходит как набранные символы; Ctrl+V всё равно читает буфер здесь.
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
        if (!OperatingSystem.IsWindows()) return Flatten(GetUnix());
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
                    return Flatten(Marshal.PtrToStringUni(ptr) ?? "");
                }
                finally { GlobalUnlock(handle); }
            }
            finally { CloseClipboard(); }
        }
        return "";
    }

    private static string Flatten(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static string GetUnix()
    {
        (string tool, string[] arguments)[] tools = OperatingSystem.IsMacOS() ? [("pbpaste", [])]
            : [
                .. Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 } ? new[] { ("wl-paste", new[] { "--no-newline" }) } : [],
                .. Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 }
                    ? new[] { ("xclip", new[] { "-selection", "clipboard", "-out" }), ("xsel", new[] { "--clipboard", "--output" }) } : [],
            ];
        foreach (var (tool, arguments) in tools)
        {
            if (UnixTools.Which(tool) is not { } path) continue;
            // pbpaste отдаёт текст в кодировке локали: без UTF-8-локали (голый запуск) не-ASCII текст теряется.
            var result = UnixTools.Run(path, arguments, TimeSpan.FromSeconds(2), start => start.Environment["LC_ALL"] = "en_US.UTF-8");
            if (result is { ExitCode: 0 } ok) return ok.Output;
        }
        return "";
    }

    // Нажатие — вставка: Ctrl+V или Shift+Insert.
    public static bool IsPasteKey(ConsoleKeyInfo key) =>
        (key.Key == ConsoleKey.V && (key.Modifiers & ConsoleModifiers.Control) != 0)
        || (key.Key == ConsoleKey.Insert && (key.Modifiers & ConsoleModifiers.Shift) != 0);
}
