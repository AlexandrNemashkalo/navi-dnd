using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

// Text from the clipboard Windows (user32, without WinForms) - for inserting Ctrl+V / Shift+Insert into the
// fields of the questionnaire and settings: the console itself does not insert (we read the input directly from
// the console buffer). macOS: pbpaste; Linux: wl-paste / xclip / xsel, if there is, otherwise nothing. There
// the terminal inserts itself (Cmd+V, Ctrl+Shift+V) - the text comes as typed characters; Ctrl+V still reads
// the buffer here.
public static class ClipboardText
{
    private const uint CF_UNICODETEXT = 13;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr hMem);

    // Buffer text in one line (hyphens → spaces) or "" - empty/not text/buffer busy.
    public static string Get()
    {
        if (!OperatingSystem.IsWindows()) return Flatten(GetUnix());
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return "";
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero)) { Thread.Sleep(20); continue; } // buffer is occupied by another window
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
            // pbpaste sends text in locale encoding: without UTF-8- locale (bare launch) non-ASCII text is lost.
            var result = UnixTools.Run(path, arguments, TimeSpan.FromSeconds(2), start => start.Environment["LC_ALL"] = "en_US.UTF-8");
            if (result is { ExitCode: 0 } ok) return ok.Output;
        }
        return "";
    }

    // Press - insert: Ctrl+V or Shift+Insert.
    public static bool IsPasteKey(ConsoleKeyInfo key) =>
        (key.Key == ConsoleKey.V && (key.Modifiers & ConsoleModifiers.Control) != 0)
        || (key.Key == ConsoleKey.Insert && (key.Modifiers & ConsoleModifiers.Shift) != 0);
}
