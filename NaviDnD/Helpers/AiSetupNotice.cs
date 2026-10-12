using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

internal static class AiSetupNotice
{
    internal static void Show()
    {
        string message = L.T("Claude Code и Codex CLI не найдены.\n\nДля игры нужен один из этих клиентов:\n")
            + "• Claude Code: code.claude.com/docs/en/quickstart\n"
            + "• Codex CLI: developers.openai.com/codex/cli\n\n"
            + (OperatingSystem.IsWindows()
                ? L.T("Установите клиент и войдите в аккаунт. Если он уже установлен, укажите путь к его exe в настройках игры.\n\nНажмите ОК, чтобы открыть настройки.")
                : L.T("Установите клиент и войдите в аккаунт. Если он уже установлен, укажите в настройках игры полный путь к исполняемому файлу claude или codex (его покажет команда which claude).\n\nНажмите ОК, чтобы открыть настройки."));
        if (OperatingSystem.IsWindows())
            // The notification must also be visible above the fullscreen backdrop.
            MessageBox(IntPtr.Zero, message, L.T("NaviDnD — настройка нейронки"), 0x40 | 0x10000 | 0x40000);
        else
            ShowUnix(message, L.T("NaviDnD — настройка нейронки"));
    }

    // Системное окно, как MessageBox в Windows (macOS: osascript, Linux: zenity). Без него экран настроек, который откроется
    // следом, сам несёт заголовок «не найдены»; вывод в консоль потерялся бы под перерисованным экраном.
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
