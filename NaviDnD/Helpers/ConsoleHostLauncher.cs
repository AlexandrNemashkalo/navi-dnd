using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NaviDnD.Helpers;

// The game draws in the classic console (conhost): Windows Terminal does not allow you to control the window,
// font and mouse as it needs. Any launch without RelaunchFlag - restarts in conhost.exe with this flag (the
// flag is set only by the game itself).
public static class ConsoleHostLauncher
{
    public const string RelaunchFlag = "--navidnd-conhost";

    // Console node Windows (conhost) - default console value in HKCU\Console\%%Startup.
    public const string ConhostDelegation = "{B23D10C0-E52E-411E-9D5B-C09FDF709C7D}";
    private const string StartupKey = @"Console\%%Startup";
    private static readonly string[] DelegationValues = ["DelegationConsole", "DelegationTerminal"];

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    // Previous settings of the player’s console at the time of restart: they are returned by the game that has
    // already been restarted (its console has been created by that time); did not return (failure) - the next
    // launch will return.
    public static string RestorePath { get; set; } = Path.Combine(Path.GetTempPath(), "navidnd-console-restore.json");

    // Visible Classic Console Window; The terminal pseudo-console has a hidden service window.
    public static bool InClassicConsole()
    {
        nint window = GetConsoleWindow();
        return window != 0 && IsWindowVisible(window);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    // One game per user: the second launch (another click on the shortcut while the game is loading) does not
    // open the second copy - it shows the window of the already launched one. Without this, each click launched
    // the game with a voice-over model, and a dozen copies clogged up the memory. The mutex is held by the game
    // (with the flag) until exit; window - in the file next to it.
    private const string InstanceName = @"Local\NaviDnD.Game";
    private const string SwitchName = @"Local\NaviDnD.ConsoleSwitch";
    private static Mutex? _instance;
    public static string WindowPath { get; set; } = Path.Combine(Path.GetTempPath(), "navidnd-window.txt");

    // Must run before ConsoleSetup hides the window or initializes input/sound/storage. true - exit this process.
    public static bool TryRelaunch(string[] args)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("NAVIDND_TEST_WORLDSTATE") != null
            || Console.IsInputRedirected || Console.IsOutputRedirected) return false;
        if (args.Contains(RelaunchFlag))
        {
            using (SwitchGate()) RestorePending();
            // Another game is already running (two starts in a race) - show its window, close this one.
            if (!ClaimInstance()) { ActivateRunning(); return true; }
            nint own = GetConsoleWindow();
            try { File.WriteAllText(WindowPath, own.ToString()); } catch { /*not important for startup*/ }
            if (own != 0) SetForegroundWindow(own);
            // There was already a restart, but the classic console window was still not there - the terminal intercepted it too.
            if (!InClassicConsole()) Log("После перезапуска игра всё ещё не в классической консоли.");
            return false;
        }
        // The game is already running - its window is forward, do not launch a second copy.
        if (IsRunning()) { ActivateRunning(); return true; }
        // Without flag - always restart (shortcut, exe, restart after update): guessing from the window who is
        // the owner of the console is unreliable - on part Windows 11 the terminal intercepts even an explicit
        // conhost.exe from the shortcut.
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable)) return false;
        try
        {
            string? assembly = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? Assembly.GetEntryAssembly()?.Location : null;
            using (SwitchGate())
            {
                RestorePending();   // the previous restart did not return the settings (failure) - return before new substitution.
                // At the time of startup, the “default console” is classic (otherwise Windows 11 with the
                // default terminal may also give conhost.exe to the terminal); The previous settings are
                // returned by restarting the game.
                var saved = ForceConhostDelegation();
                if (saved.Count > 0) File.WriteAllText(RestorePath, System.Text.Json.JsonSerializer.Serialize(saved));
                // New game window gets focus (otherwise Windows might open it behind other windows).
                AllowSetForegroundWindow(-1);
                using var child = Process.Start(CreateStartInfo(executable, args, Environment.CurrentDirectory, assembly));
                if (child == null)
                {
                    Log("conhost.exe не запустился.");
                    RestorePending();
                    return false;
                }
            }
            // The first window is hidden after the second is launched: the game will open in it, no need to wait.
            if (GetConsoleWindow() is var own and not 0) ShowWindow(own, 0);
            return true;
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log("Не удалось перезапустить в классической консоли: " + error.Message);
            return false;
        }
    }

    private static bool IsRunning() => Mutex.TryOpenExisting(InstanceName, out var existing) && Dispose(existing);

    private static bool Dispose(Mutex mutex) { mutex.Dispose(); return true; }

    // Take the game place; The old copy is still closing (restart after update) - wait for it a little.
    private static bool ClaimInstance()
    {
        _instance = new Mutex(false, InstanceName);
        try { return _instance.WaitOne(TimeSpan.FromSeconds(3)); }
        catch (AbandonedMutexException) { return true; }   // last game fell without letting go
    }

    // Window of the running game - expand and go.
    private static void ActivateRunning()
    {
        try
        {
            if (!nint.TryParse(File.ReadAllText(WindowPath).Trim(), out nint window) || window == 0) return;
            if (IsIconic(window)) ShowWindow(window, 9);   // SW_RESTORE
            SetForegroundWindow(window);
        }
        catch { /*there is no window - we just don’t duplicate the game*/ }
    }

    // Replacing and returning console settings - one process at a time (parallel runs do not override each other).
    private static IDisposable SwitchGate()
    {
        var gate = new Mutex(false, SwitchName);
        bool owned;
        try { owned = gate.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { owned = true; }
        return new Gate(gate, owned);
    }

    private sealed class Gate(Mutex mutex, bool owned) : IDisposable
    {
        public void Dispose()
        {
            if (owned) mutex.ReleaseMutex();
            mutex.Dispose();
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

    // Previous default console/terminal values (null - no value) and classic console entry.
    public static Dictionary<string, object?> ForceConhostDelegation()
    {
        var saved = new Dictionary<string, object?>();
        if (!OperatingSystem.IsWindows()) return saved;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StartupKey, writable: true);
            foreach (string name in DelegationValues)
            {
                saved[name] = key.GetValue(name);
                key.SetValue(name, ConhostDelegation, RegistryValueKind.String);
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log("Не удалось временно выбрать классическую консоль: " + error.Message);
        }
        return saved;
    }

    public static void RestoreDelegation(Dictionary<string, object?> saved)
    {
        if (!OperatingSystem.IsWindows() || saved.Count == 0) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StartupKey, writable: true);
            foreach (var (name, value) in saved)
                if (value == null) key.DeleteValue(name, throwOnMissingValue: false);
                else key.SetValue(name, value);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log("Не удалось вернуть настройки консоли: " + error.Message);
        }
    }

    // Restore the console settings saved during restart (file RestorePath)) and delete the file.
    public static void RestorePending()
    {
        try
        {
            if (!File.Exists(RestorePath)) return;
            var saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(RestorePath));
            if (saved != null) RestoreDelegation(saved.ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
            File.Delete(RestorePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log("Не удалось вернуть настройки консоли: " + error.Message);
        }
    }

    private static void Log(string message)
    {
        try
        {
            string dir = Path.Combine(AppConfig.DataRoot, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "console.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { /*log is not more important than launch*/ }
    }
}
