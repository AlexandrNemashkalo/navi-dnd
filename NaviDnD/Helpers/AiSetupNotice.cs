using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

internal static class AiSetupNotice
{
    internal static void Show()
    {
        string message = L.T("Claude Code и Codex CLI не найдены.\n\nДля игры нужен один из этих клиентов:\n")
            + "• Claude Code: code.claude.com/docs/en/quickstart\n"
            + "• Codex CLI: developers.openai.com/codex/cli\n\n"
            + L.T("Установите клиент и войдите в аккаунт. Если он уже установлен, укажите путь к его exe в настройках игры.\n\nНажмите ОК, чтобы открыть настройки.");
        if (OperatingSystem.IsWindows())
            // The notification must also be visible above the fullscreen backdrop.
            MessageBox(IntPtr.Zero, message, L.T("NaviDnD — настройка нейронки"), 0x40 | 0x10000 | 0x40000);
        else
            Console.WriteLine(message);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
