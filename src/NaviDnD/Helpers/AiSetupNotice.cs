using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

internal static class AiSetupNotice
{
    internal static void Show()
    {
        string message = L.T("Claude Code and Codex CLI were not found.\n\nThe game needs one of these clients:\n")
            + "• Claude Code: code.claude.com/docs/en/quickstart\n"
            + "• Codex CLI: developers.openai.com/codex/cli\n\n"
            + (OperatingSystem.IsWindows()
                ? L.T("Install a client and sign in. If it is already installed, set the path to its exe in the game settings.\n\nPress OK to open the settings.")
                : L.T("Install a client and sign in. If it is already installed, set the full path to the claude or codex executable in the game settings (find it with: which claude).\n\nPress OK to open the settings."));
        if (OperatingSystem.IsWindows())
            // The notification must also be visible above the fullscreen backdrop.
            MessageBox(IntPtr.Zero, message, L.T("NaviDnD — AI setup"), 0x40 | 0x10000 | 0x40000);
        else
            ShowUnix(message, L.T("NaviDnD — AI setup"));
    }

    // A system window like MessageBox in Windows (macOS: osascript, Linux: zenity).) Without it, the settings
    // screen that opens next would itself carry the heading “not found”; the console output would be lost under
    // the redrawn screen.
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void ShowUnix(string message, string title)
    {
        if (OperatingSystem.IsMacOS() && UnixTools.Which("osascript") is { } osascript)
            UnixTools.Run(osascript, ["-e", "on run argv", "-e", "activate",
                "-e", "display dialog (item 1 of argv) with title (item 2 of argv) buttons {\"OK\"} default button 1 with icon caution",
                "-e", "end run", message, title], TimeSpan.FromMinutes(10));
        else if (!OperatingSystem.IsMacOS() && UnixTools.Which("zenity") is { } zenity
                 && (Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 } || Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 }))
            UnixTools.Run(zenity, ["--warning", "--no-markup", "--title=" + title, "--text=" + message], TimeSpan.FromMinutes(10));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
