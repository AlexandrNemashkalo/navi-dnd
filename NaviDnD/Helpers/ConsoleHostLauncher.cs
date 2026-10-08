using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

public static class ConsoleHostLauncher
{
    public const string RelaunchFlag = "--navidnd-conhost";

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    // Must run before ConsoleSetup hides the window or initializes input/sound/storage.
    public static bool TryRelaunch(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Contains(RelaunchFlag)
            || Environment.GetEnvironmentVariable("NAVIDND_TEST_WORLDSTATE") != null
            || Console.IsInputRedirected || Console.IsOutputRedirected) return false;
        nint window = GetConsoleWindow();
        // A pseudoconsole has a hidden message window, not the classic console's visible HWND.
        if (window != 0 && IsWindowVisible(window)) return false;
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable)) return false;
        try
        {
            string? assembly = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? Assembly.GetEntryAssembly()?.Location : null;
            using var child = Process.Start(CreateStartInfo(executable, args, Environment.CurrentDirectory, assembly));
            return child != null;
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Trace.TraceWarning("Cannot restart in classic console: {0}", error.Message);
            return false;
        }
    }

    public static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> args, string workingDirectory, string? assembly = null)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe"))
        {
            UseShellExecute = true,
            WorkingDirectory = workingDirectory,
            WindowStyle = ProcessWindowStyle.Normal
        };
        start.ArgumentList.Add(executable);
        if (!string.IsNullOrEmpty(assembly)) start.ArgumentList.Add(assembly);
        foreach (string arg in args)
            if (arg != RelaunchFlag) start.ArgumentList.Add(arg);
        start.ArgumentList.Add(RelaunchFlag);
        return start;
    }
}
