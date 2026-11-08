using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NaviDnD.Helpers;

// Update on macOS - without an assistant: files of the running program can be replaced (old ones remain open in
// it), and after installation the game restarts in the same terminal window (execv). Storage/ and logs/ are not
// touched.
[UnsupportedOSPlatform("windows")]
internal static class UnixUpdate
{
    // Game programs: they still need “executable” after replacement. ZIP assembly bears the rights itself, this
    // is insurance. Paths - from the programs folder and from the game files folder (for NaviDnD.app these are
    // Contents/MacOS and Contents/Resources).
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

    // New version - in the same window: requests to the GM and voice acting are completed, the terminal returns
    // to normal mode, and the process is replaced by the game program with the same arguments. It didn't work
    // out - the game just closes.
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
