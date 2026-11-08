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
    private static readonly object StartupLock = new();
    private static Timer? _startupFallback;
    public const string Title = "NaviDnD";
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
            SetTaskbarIdentity();
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
            var start = CreateStartInfo(terminal, executable, config, args, Environment.CurrentDirectory, assembly);
            WindowsTerminalSizing.PrepareLiveConfig(start);
            using var child = Process.Start(start)
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

    private static void SetTaskbarIdentity()
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (executable == null) return;
            string? assembly = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? Assembly.GetEntryAssembly()?.Location : null;
            var launch = CreateTaskbarStartInfo(executable, assembly, AppContext.BaseDirectory);
            WindowsTaskbar.SetWindowIdentity(Window, launch, Path.Combine(AppContext.BaseDirectory, "dnd.ico"));
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not set NaviDnD taskbar identity: {error.Message}");
        }
    }

    public static ProcessStartInfo CreateTaskbarStartInfo(string executable, string? assembly = null, string? directory = null)
    {
        // Window relaunch properties reject long strings (the full terminal command
        // exceeds the shell limit even under LocalAppData). A GUI launcher also avoids
        // allocating a temporary console before the game starts its terminal.
        string? launcher = directory == null ? null : Path.Combine(directory, "NaviDnD.Launcher.exe");
        if (launcher != null && File.Exists(launcher))
            return new ProcessStartInfo(launcher) { UseShellExecute = false, WorkingDirectory = directory! };
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (assembly != null) start.ArgumentList.Add(assembly);
        return start;
    }

    public static ProcessStartInfo CreateStartInfo(string terminal, string executable, string config,
        IEnumerable<string> args, string directory, string? assembly = null)
    {
        var start = new ProcessStartInfo(terminal)
        {
            UseShellExecute = false, WorkingDirectory = directory, WindowStyle = ProcessWindowStyle.Normal
        };
        foreach (string arg in new[] { "--config-file", config, "--title", Title, "-o", QuietPosition,
            "-o", WindowsTerminalSizing.FontOverride(Path.GetDirectoryName(Path.GetFullPath(assembly ?? executable))!), "-e", executable }) start.ArgumentList.Add(arg);
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
        int disabled = 1;
        DwmSetWindowAttribute(window, 3, ref disabled, sizeof(int)); // DWMWA_TRANSITIONS_FORCEDISABLED
        _awaitingFirstFrame = true;
        // Alacritty owns this HWND: cross-process DWM cloaking is not reliable.
        // Keep its startup position off-screen until the first complete frame is ready.
        // Startup errors must not leave an invisible application indefinitely.
        _startupFallback = new Timer(_ => RevealFirstFrame(), null, 15000, Timeout.Infinite);
    }

    internal static bool AwaitingFirstFrame => _awaitingFirstFrame;

    internal static bool ApplySize(int requested)
    {
        if (!OperatingSystem.IsWindows()) return false;
        nint previous = SetThreadDpiAwarenessContext(new nint(-4));
        try { return ApplySizeCore(requested); }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }

    private static (int width, int height)? ReadCellPixels()
    {
        // ConPTY consumes window-size escape queries. The native client and console grid
        // give the actual font metrics, including DPI and Alacritty's line-height offset.
        if (!GetClientRect(Window, out var rect)) return null;
        int cols = Console.WindowWidth, rows = Console.WindowHeight;
        return cols > 0 && rows > 0 ? (rect.Right / cols, rect.Bottom / rows) : null;
    }

    private static bool ApplySizeCore(int requested)
    {
        if (!Active || UnixTerminal.Current is not { } terminal) return false;
        string? path = Environment.GetEnvironmentVariable(WindowsTerminalSizing.SessionConfigVariable);
        if (path == null || !File.Exists(path)) return false;
        var before = ReadCellPixels();
        if (before == null) return false;
        string previous = File.ReadAllText(path);
        double size = WindowsTerminalSizing.FontSize(requested, WindowsTerminalSizing.ScreenFit());
        string updated = WindowsTerminalSizing.LiveConfigText(Path.Combine(AppContext.BaseDirectory, "Terminal", "alacritty-windows.toml"), size);
        var watch = Stopwatch.StartNew();
        (int width, int height)? cell = before;
        var fontMatch = System.Text.RegularExpressions.Regex.Match(previous, @"(?m)^size\s*=\s*([0-9.]+)");
        bool sameFont = double.TryParse(fontMatch.Groups[1].Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double oldSize) && oldSize == size;
        int offset = -1;
        if (sameFont)
        {
            var offsetMatch = System.Text.RegularExpressions.Regex.Match(previous, @"\by\s*=\s*(-?\d+)");
            if (int.TryParse(offsetMatch.Groups[1].Value, out int oldOffset)) offset = oldOffset;
        }
        else
        {
            File.WriteAllText(path, updated);
            while (watch.ElapsedMilliseconds < 2500)
            {
                Thread.Sleep(80);
                cell = ReadCellPixels();
                if (cell != null && cell != before) break;
            }
            if (cell == null || cell == before) { File.WriteAllText(path, previous); return false; }
        }
        for (int attempt = 0; cell is { } measured && measured.height != 2 * measured.width && attempt < 4; attempt++)
        {
            offset = WindowsTerminalSizing.CorrectedLineOffset(offset, measured.width, measured.height);
            File.WriteAllText(path, WindowsTerminalSizing.LiveConfigText(
                Path.Combine(AppContext.BaseDirectory, "Terminal", "alacritty-windows.toml"), size, offset));
            watch.Restart();
            while (watch.ElapsedMilliseconds < 2500)
            {
                Thread.Sleep(80);
                cell = ReadCellPixels();
                if (cell != null && cell != measured) break;
            }
        }
        if (cell == null || cell.Value.height != 2 * cell.Value.width)
        {
            File.WriteAllText(path, previous);
            return false;
        }
        int width = 150 * cell.Value.width, height = 63 * cell.Value.height;
        GetWindowRect(Window, out var bounds);
        int x = bounds.Left + (bounds.Right - bounds.Left - width) / 2;
        int y = bounds.Top + (bounds.Bottom - bounds.Top - height) / 2;
        SetWindowPos(Window, 0, AwaitingFirstFrame ? bounds.Left : Math.Max(0, x),
            AwaitingFirstFrame ? bounds.Top : Math.Max(0, y), width, height, 0x0004 | 0x0010);
        if (FullscreenBackdrop.Active) FullscreenBackdrop.Recenter();
        watch.Restart();
        while (watch.ElapsedMilliseconds < 1000)
        {
            Thread.Sleep(20);
            if (Console.WindowWidth == 150 && Console.WindowHeight == 63) break;
        }
        terminal.RefreshLayoutAfterResize();
        ConsoleMouseReader.RefreshWindowButtons();
        return true;
    }

    internal static (int x, int y) InitialWindowPosition()
    {
        if (!GetWindowRect(Window, out var bounds)) return (0, 0);
        var work = new Rect { Right = GetSystemMetrics(0), Bottom = GetSystemMetrics(1) };
        // The primary monitor work area excludes the taskbar on any edge.
        SystemParametersInfo(0x30, 0, ref work, 0); // SPI_GETWORKAREA
        return CenterInWorkArea(work.Left, work.Top, work.Right, work.Bottom,
            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    public static (int x, int y) CenterInWorkArea(int left, int top, int right, int bottom, int width, int height) =>
        (left + Math.Max(0, (right - left - width) / 2), top + Math.Max(0, (bottom - top - height) / 2));

    internal static void RevealFirstFrame()
    {
        lock (StartupLock)
        {
            if (!_awaitingFirstFrame) return;
            _startupFallback?.Dispose();
            UnixTerminal.Current?.Flush();
            // Alacritty remains visible off-screen, so it can consume and render this frame.
            Thread.Sleep(100);
            DwmFlush();
            _awaitingFirstFrame = false;
            var window = Window;
            var (x, y) = InitialWindowPosition();
            SetWindowPos(window, 0, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
            if (FullscreenBackdrop.Active)
                FullscreenBackdrop.Recenter();
            FullscreenBackdrop.RevealStartup();
            SetForegroundWindow(window);
        }
    }

    internal static void HideForExit()
    {
        if (!Active) return;
        lock (StartupLock)
        {
            _awaitingFirstFrame = false;
            _startupFallback?.Dispose();
            if (Window != 0) ShowWindow(Window, 0);
        }
        try { WindowsTaskbar.ClearWindowIdentity(Window); }
        catch (COMException error) { Debug.WriteLine(error.Message); }
        string? session = Environment.GetEnvironmentVariable(WindowsTerminalSizing.SessionConfigVariable);
        if (session != null && Path.GetDirectoryName(session) == Path.TrimEndingDirectorySeparator(Path.GetTempPath())
            && Path.GetFileName(session).StartsWith("navidnd-terminal-", StringComparison.Ordinal) && Path.GetExtension(session) == ".toml")
        {
            try { File.Delete(session); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Debug.WriteLine(error.Message); }
        }
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
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint parameter, ref Rect value, uint flags);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);

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
