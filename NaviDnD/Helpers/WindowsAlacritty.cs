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
    public const string QuietFlag = "--navidnd-quiet-start";
    public const string QuietPosition = "window.position={x=-32000,y=-32000}";
    private static bool _awaitingFirstFrame;
    private static Timer? _startupFallback;
    public const string Title = "NaviDnD — Alacritty";
    internal static bool Active { get; private set; }
    private static nint _window;
    internal static nint Window => Active ? (_window != 0 ? _window : FindWindow(null, Title)) : 0;

    internal static bool Launch(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (args.Contains(ChildFlag))
        {
            Active = true;
            _window = FindParentWindow();
            if (args.Contains(QuietFlag)) PrepareHiddenWindow();
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
        foreach (string arg in new[] { "--config-file", config, "--title", Title, "-o", QuietPosition, "-e", executable }) start.ArgumentList.Add(arg);
        if (assembly != null) start.ArgumentList.Add(assembly);
        start.ArgumentList.Add(ChildFlag);
        start.ArgumentList.Add(QuietFlag);
        foreach (string arg in args.Where(a => a != LaunchFlag && a != ChildFlag && a != QuietFlag && a != ClassicFlag && a != ConsoleHostLauncher.RelaunchFlag))
            start.ArgumentList.Add(arg);
        return start;
    }

    private static void PrepareHiddenWindow()
    {
        var window = Window;
        if (window == 0) return;
        int cloak = 1;
        DwmSetWindowAttribute(window, 3, ref cloak, sizeof(int)); // DWMWA_TRANSITIONS_FORCEDISABLED
        DwmSetWindowAttribute(window, 13, ref cloak, sizeof(int));
        DwmFlush(); // Commit hiding before moving the framed window onto the screen.
        _awaitingFirstFrame = true;
        // Move onto the primary display while cloaked, so DPI and terminal size settle before drawing.
        if (GetWindowRect(window, out var bounds))
            SetWindowPos(window, 0, Math.Max(0, (GetSystemMetrics(0) - (bounds.Right - bounds.Left)) / 2),
                Math.Max(0, (GetSystemMetrics(1) - (bounds.Bottom - bounds.Top)) / 2), 0, 0, 0x0001 | 0x0004 | 0x0010);
        // Startup errors must not leave an invisible application indefinitely.
        _startupFallback = new Timer(_ => RevealFirstFrame(), null, 15000, Timeout.Infinite);
    }

    internal static bool AwaitingFirstFrame => _awaitingFirstFrame;

    internal static void RevealFirstFrame()
    {
        if (!_awaitingFirstFrame) return;
        _awaitingFirstFrame = false;
        _startupFallback?.Dispose();
        UnixTerminal.Current?.Flush();
        // Give the terminal time to consume the flushed first frame before exposing its surface.
        Thread.Sleep(100);
        var window = Window;
        DwmFlush(); // Finish frame/position changes while the game is still cloaked.
        int cloak = 0;
        DwmSetWindowAttribute(window, 13, ref cloak, sizeof(int));
        SetForegroundWindow(window);
    }

    internal static void HideForExit()
    {
        if (!Active) return;
        _awaitingFirstFrame = false;
        _startupFallback?.Dispose();
        if (Window != 0) ShowWindow(Window, 0);
    }

    private static nint FindParentWindow()
    {
        // The title can change; bind controls to this game's actual terminal process.
        var information = new nint[6];
        using var process = Process.GetCurrentProcess();
        if (NtQueryInformationProcess(process.Handle, 0, information, information.Length * IntPtr.Size, out _) != 0)
            return FindWindow(null, Title);
        uint parent = unchecked((uint)information[5]);
        nint found = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != parent || (GetWindowLong(window, -16) & 0x00C00000) == 0) return true;
            found = window;
            return false;
        }, 0);
        return found != 0 ? found : FindWindow(null, Title);
    }

    private delegate bool EnumWindowCallback(nint window, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, nint data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(nint process, int kind, [Out] nint[] information, int size, out int returned);

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

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
