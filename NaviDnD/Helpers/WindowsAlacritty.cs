using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

// Default Windows GPU terminal. Classic conhost remains available for diagnostics and UI tests.
public static class WindowsAlacritty
{
    public const string LaunchFlag = "--alacritty";
    public const string ChildFlag = "--navidnd-alacritty";
    public const string ClassicFlag = "--classic-console";
    public const string Title = "NaviDnD — Alacritty";
    internal static bool Active { get; private set; }
    internal static nint Window => Active ? FindWindow(null, Title) : 0;

    internal static bool Launch(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (args.Contains(ChildFlag))
        {
            Active = true;
            RemoveWindowFrame();
            ConsoleSetup.SetConsoleIcon();
            return false;
        }
        if (!ShouldLaunch(args, Environment.GetEnvironmentVariable("NAVIDND_TEST_WORLDSTATE") != null)) return false;
        try
        {
            string terminal = FindExecutable() ?? throw new FileNotFoundException(
                L.T("Alacritty was not found. Install it, put alacritty.exe beside NaviDnD.exe, or set NAVIDND_ALACRITTY_PATH."));
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException(L.T("Game executable not found."));
            string? assembly = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? Assembly.GetEntryAssembly()?.Location : null;
            string config = Path.Combine(AppContext.BaseDirectory, "Terminal", "alacritty-windows.toml");
            if (!File.Exists(config)) throw new FileNotFoundException(L.T("Alacritty game configuration not found."), config);
            using var child = Process.Start(CreateStartInfo(terminal, executable, config, args, Environment.CurrentDirectory, assembly))
                ?? throw new InvalidOperationException(L.T("Alacritty did not start."));
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine(error.Message);
            Environment.ExitCode = 1;
        }
        return true;
    }

    public static bool ShouldLaunch(IEnumerable<string> args, bool uiTest)
    {
        var flags = args.ToHashSet();
        return !flags.Contains(ChildFlag) && !flags.Contains(ClassicFlag)
            && !flags.Contains(ConsoleHostLauncher.RelaunchFlag) && (!uiTest || flags.Contains(LaunchFlag));
    }

    internal static string? FindExecutable()
    {
        string? configured = Environment.GetEnvironmentVariable("NAVIDND_ALACRITTY_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        string[] candidates = [Path.Combine(AppContext.BaseDirectory, "alacritty.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Alacritty", "alacritty.exe"),
            .. (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => Path.Combine(p.Trim('"'), "alacritty.exe"))];
        return candidates.FirstOrDefault(File.Exists);
    }

    public static ProcessStartInfo CreateStartInfo(string terminal, string executable, string config,
        IEnumerable<string> args, string directory, string? assembly = null)
    {
        var start = new ProcessStartInfo(terminal)
        {
            UseShellExecute = false, WorkingDirectory = directory, WindowStyle = ProcessWindowStyle.Normal
        };
        foreach (string arg in new[] { "--config-file", config, "--title", Title, "-e", executable }) start.ArgumentList.Add(arg);
        if (assembly != null) start.ArgumentList.Add(assembly);
        start.ArgumentList.Add(ChildFlag);
        foreach (string arg in args.Where(a => a != LaunchFlag && a != ChildFlag && a != ClassicFlag && a != ConsoleHostLauncher.RelaunchFlag))
            start.ArgumentList.Add(arg);
        return start;
    }

    private static void RemoveWindowFrame()
    {
        var window = Window;
        if (window == 0 || !GetClientRect(window, out var client) || !GetWindowRect(window, out var bounds)) return;
        // decorations=None makes Winit report the minimized icon as a 160x28 client,
        // shrinking ConPTY and destroying the screen. Keep its normal non-client
        // handler, remove only Win32 frame styles, and preserve the initial grid size.
        int style = GetWindowLong(window, -16);
        SetWindowLong(window, -16, style & ~0x00C40000); // caption and sizing frame
        SetWindowPos(window, 0, bounds.Left, bounds.Top, client.Right, client.Bottom,
            0x0004 | 0x0010 | 0x0020); // NOZORDER | NOACTIVATE | FRAMECHANGED
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? className, string title);
}
