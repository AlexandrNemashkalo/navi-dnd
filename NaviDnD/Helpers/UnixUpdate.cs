using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NaviDnD.Helpers;

// Обновление на macOS — без помощника: файлы запущенной программы можно заменять (старые остаются открытыми у неё),
// а после установки игра перезапускается в том же окне терминала (execv). Storage/ и logs/ не трогаются.
[UnsupportedOSPlatform("windows")]
internal static class UnixUpdate
{
    // Программы игры: «исполняемый» нужен им и после замены. ZIP сборки несёт права сам, это — страховка.
    // Пути — от папки программ и от папки файлов игры (у NaviDnD.app это Contents/MacOS и Contents/Resources).
    private static readonly string[] Programs = ["NaviDnD", "NaviDnD.McpServer", "alacritty", "createdump", "Speech/setup-silero.sh",
        "Speech/silero/python/bin/python3", "Speech/silero/python/bin/python3.12"];

    internal static void MarkExecutable(string root)
    {
        const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        foreach (string name in Programs)
        {
            string path = Path.Combine(root, name);
            if (File.Exists(path)) File.SetUnixFileMode(path, File.GetUnixFileMode(path) | execute);
        }
    }

    // Новая версия — в том же окне: запросы к мастеру и озвучка завершаются, терминал возвращается в обычный режим,
    // и процесс заменяется программой игры с теми же аргументами. Не вышло — игра просто закрывается.
    internal static void Restart()
    {
        ChildProcessJob.KillAll();
        SileroSpeech.Close();
        UnixTerminal.Current?.Restore();
        string program = Path.Combine(AppContext.BaseDirectory, "NaviDnD");
        string?[] arguments = [program, .. Environment.GetCommandLineArgs().Skip(1), null];
        execv(program, arguments);
        Environment.Exit(0);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int execv(string path, string?[] argv);
}
