using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

// Standard Windows "Open File" window (comdlg32 GetOpenFileNameW) - without WinForms:, connecting WinForms
// would change the target platform of the project (net9.0-windows) and the build paths on which the MCP-server
// and UI-tests depend. The window is shown in a separate STA- stream on top of the window consoles. macOS:
// AppleScript "choose file" via osascript; Linux: zenity, otherwise null (no file selection).
public static class FileDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);

    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();

    private const int OFN_FILEMUSTEXIST = 0x00001000, OFN_PATHMUSTEXIST = 0x00000800, OFN_EXPLORER = 0x00080000,
        OFN_NOCHANGEDIR = 0x00000008;

    // Path to the selected file or null (cancel). filter - pairs “description\0mask” separated by \0.
    public static string? OpenFile(string title, string filter)
    {
        if (!OperatingSystem.IsWindows()) return OpenFileUnix(title, filter);
        string? result = null;
        var thread = new Thread(() =>
        {
            const int bufferChars = 4096;
            IntPtr buffer = Marshal.AllocHGlobal(bufferChars * 2);
            try
            {
                Marshal.WriteInt16(buffer, 0);
                var ofn = new OPENFILENAME
                {
                    lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                    hwndOwner = GetConsoleWindow(),
                    lpstrFilter = filter.Replace('|', '\0') + "\0\0",
                    nFilterIndex = 1,
                    lpstrFile = buffer,
                    nMaxFile = bufferChars,
                    lpstrTitle = title,
                    Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
                };
                if (GetOpenFileNameW(ref ofn)) result = Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static string? OpenFileUnix(string title, string filter)
    {
        (string tool, string[] arguments)? command = OperatingSystem.IsMacOS() ? ("osascript", MacArguments(title, filter))
            : Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 } || Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 }
                ? ("zenity", ZenityArguments(title, filter)) : null;
        if (command is not { } c || UnixTools.Which(c.tool) is not { } path) return null;
        // Waits for the player like a Windows window; cancel - non-zero exit code.
        var result = UnixTools.Run(path, c.arguments, null, start => start.Environment["LC_ALL"] = "en_US.UTF-8");
        string? file = result is { ExitCode: 0 } ok ? ok.Output.TrimEnd('\r', '\n') : null;
        return string.IsNullOrWhiteSpace(file) || !File.Exists(file) ? null : file;
    }

    // “description|*.png;*.jpg|…” → png, jpg (masks of the form “*” or “*.*” - any file).
    internal static string[] Extensions(string filter) =>
        [.. filter.Split('|').Where((_, i) => i % 2 == 1).SelectMany(masks => masks.Split(';'))
            .Select(mask => mask.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant())
            .Where(ext => ext.Length > 0 && ext != "*").Distinct()];

    // The header and file types are passed as script arguments - nothing needs to be escaped. Extensions are
    // also listed, and their UTI: "choose file of type" accepts both. "activate" displays a window on top of
    // the terminal.
    internal static string[] MacArguments(string title, string filter)
    {
        var kinds = Extensions(filter).SelectMany(ext => Utis.TryGetValue(ext, out var uti) ? new[] { ext, uti } : [ext]).Distinct();
        return ["-e", "on run argv", "-e", "activate",
            "-e", "if (count of argv) > 1 then",
            "-e", "set chosen to choose file with prompt (item 1 of argv) of type (rest of argv)",
            "-e", "else",
            "-e", "set chosen to choose file with prompt (item 1 of argv)",
            "-e", "end if",
            "-e", "return POSIX path of chosen",
            "-e", "end run",
            title, .. kinds];
    }

    private static readonly Dictionary<string, string> Utis = new()
    {
        ["png"] = "public.png", ["jpg"] = "public.jpeg", ["jpeg"] = "public.jpeg", ["bmp"] = "com.microsoft.bmp",
        ["gif"] = "com.compuserve.gif", ["svg"] = "public.svg-image",
    };

    internal static string[] ZenityArguments(string title, string filter)
    {
        var parts = filter.Split('|');
        var arguments = new List<string> { "--file-selection", "--title=" + title };
        for (int i = 0; i + 1 < parts.Length; i += 2)
            arguments.Add($"--file-filter={parts[i]} | {string.Join(' ', parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries))}");
        return [.. arguments];
    }
}
