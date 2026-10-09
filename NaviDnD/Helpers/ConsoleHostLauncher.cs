using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NaviDnD.Helpers;

// Игра рисует в классической консоли (conhost): Windows Terminal не даёт управлять окном, шрифтом и мышью так, как
// ей нужно. Любой запуск без RelaunchFlag — перезапуск в conhost.exe с этим флагом (флаг ставит только сама игра).
public static class ConsoleHostLauncher
{
    public const string RelaunchFlag = "--navidnd-conhost";

    // Узел консоли Windows (conhost) — значение «консоль по умолчанию» в HKCU\Console\%%Startup.
    public const string ConhostDelegation = "{B23D10C0-E52E-411E-9D5B-C09FDF709C7D}";
    private const string StartupKey = @"Console\%%Startup";
    private static readonly string[] DelegationValues = ["DelegationConsole", "DelegationTerminal"];

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    // Видимое окно классической консоли; у псевдоконсоли терминала — скрытое служебное окно.
    public static bool InClassicConsole()
    {
        nint window = GetConsoleWindow();
        return window != 0 && IsWindowVisible(window);
    }

    // Must run before ConsoleSetup hides the window or initializes input/sound/storage.
    public static bool TryRelaunch(string[] args)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("NAVIDND_TEST_WORLDSTATE") != null
            || Console.IsInputRedirected || Console.IsOutputRedirected) return false;
        if (args.Contains(RelaunchFlag))
        {
            // Перезапуск уже был, а окна классической консоли всё равно нет — терминал перехватил и его.
            if (!InClassicConsole()) Log("После перезапуска игра всё ещё не в классической консоли.");
            return false;
        }
        // Без флага — всегда перезапуск (ярлык, exe, перезапуск после обновления): угадать по окну, кто хозяин
        // консоли, ненадёжно — на части Windows 11 терминал перехватывает даже явный conhost.exe из ярлыка.
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable)) return false;
        try
        {
            string? assembly = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? Assembly.GetEntryAssembly()?.Location : null;
            // На время запуска «консоль по умолчанию» — классическая (иначе Windows 11 с терминалом по умолчанию
            // может отдать и conhost.exe терминалу); прежние настройки игрока возвращаются, как только окно появилось.
            var saved = ForceConhostDelegation();
            try
            {
                using var child = Process.Start(CreateStartInfo(executable, args, Environment.CurrentDirectory, assembly));
                if (child == null) { Log("conhost.exe не запустился."); return false; }
                WaitForWindow(child, TimeSpan.FromSeconds(5));
                return true;
            }
            finally { RestoreDelegation(saved); }
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log("Не удалось перезапустить в классической консоли: " + error.Message);
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

    // Прежние значения «консоль/терминал по умолчанию» (null — значения не было) и запись классической консоли.
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

    // Пока conhost не создал окно, настройка «по умолчанию» ещё нужна; дольше timeout не ждём.
    private static void WaitForWindow(Process child, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            while (sw.Elapsed < timeout && !child.HasExited)
            {
                child.Refresh();
                if (child.MainWindowHandle != 0) break;
                Thread.Sleep(50);
            }
            Thread.Sleep(300);   // окно есть — игра внутри уже стартует в нём
        }
        catch (InvalidOperationException) { }
    }

    private static void Log(string message)
    {
        try
        {
            string dir = Path.Combine(AppConfig.ProjectRoot, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "console.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { /* лог не важнее запуска */ }
    }
}
