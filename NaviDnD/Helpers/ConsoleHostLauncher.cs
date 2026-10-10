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
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    // Прежние настройки консоли игрока на время перезапуска: их возвращает уже перезапущенная игра (её консоль к тому
    // времени создана); не вернула (сбой) — вернёт следующий запуск.
    public static string RestorePath { get; set; } = Path.Combine(Path.GetTempPath(), "navidnd-console-restore.json");

    // Видимое окно классической консоли; у псевдоконсоли терминала — скрытое служебное окно.
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

    // Одна игра на пользователя: второй запуск (ещё клик по ярлыку, пока игра грузится) не открывает вторую копию —
    // показывает окно уже запущенной. Без этого каждый клик запускал игру с моделью озвучки, и десяток копий
    // забивал память. Мьютекс держит игра (с флагом) до выхода; окно — в файле рядом с ним.
    private const string InstanceName = @"Local\NaviDnD.Game";
    private const string SwitchName = @"Local\NaviDnD.ConsoleSwitch";
    private static Mutex? _instance;
    public static string WindowPath { get; set; } = Path.Combine(Path.GetTempPath(), "navidnd-window.txt");

    // Must run before ConsoleSetup hides the window or initializes input/sound/storage. true — этому процессу выйти.
    public static bool TryRelaunch(string[] args)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("NAVIDND_TEST_WORLDSTATE") != null
            || Console.IsInputRedirected || Console.IsOutputRedirected) return false;
        if (args.Contains(RelaunchFlag))
        {
            using (SwitchGate()) RestorePending();
            // Уже идёт другая игра (два запуска наперегонки) — показать её окно, эту закрыть.
            if (!ClaimInstance()) { ActivateRunning(); return true; }
            nint own = GetConsoleWindow();
            try { File.WriteAllText(WindowPath, own.ToString()); } catch { /* не важно для запуска */ }
            if (own != 0) SetForegroundWindow(own);
            // Перезапуск уже был, а окна классической консоли всё равно нет — терминал перехватил и его.
            if (!InClassicConsole()) Log("После перезапуска игра всё ещё не в классической консоли.");
            return false;
        }
        // Игра уже запущена — её окно вперёд, вторую копию не запускать.
        if (IsRunning()) { ActivateRunning(); return true; }
        // Без флага — всегда перезапуск (ярлык, exe, перезапуск после обновления): угадать по окну, кто хозяин
        // консоли, ненадёжно — на части Windows 11 терминал перехватывает даже явный conhost.exe из ярлыка.
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable)) return false;
        try
        {
            string? assembly = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? Assembly.GetEntryAssembly()?.Location : null;
            using (SwitchGate())
            {
                RestorePending();   // прошлый перезапуск не вернул настройки (сбой) — вернуть до новой подмены
                // На время запуска «консоль по умолчанию» — классическая (иначе Windows 11 с терминалом по умолчанию
                // может отдать и conhost.exe терминалу); прежние настройки возвращает перезапущенная игра.
                var saved = ForceConhostDelegation();
                if (saved.Count > 0) File.WriteAllText(RestorePath, System.Text.Json.JsonSerializer.Serialize(saved));
                // Новое окно игры получает фокус (иначе Windows может открыть его позади других окон).
                AllowSetForegroundWindow(-1);
                using var child = Process.Start(CreateStartInfo(executable, args, Environment.CurrentDirectory, assembly));
                if (child == null)
                {
                    Log("conhost.exe не запустился.");
                    RestorePending();
                    return false;
                }
            }
            // Первое окно прячется уже после запуска второго: игра откроется в нём, ждать не нужно.
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

    // Занять место игры; прежняя копия ещё закрывается (перезапуск после обновления) — подождать её немного.
    private static bool ClaimInstance()
    {
        _instance = new Mutex(false, InstanceName);
        try { return _instance.WaitOne(TimeSpan.FromSeconds(3)); }
        catch (AbandonedMutexException) { return true; }   // прошлая игра упала, не отпустив
    }

    // Окно запущенной игры — развернуть и вперёд.
    private static void ActivateRunning()
    {
        try
        {
            if (!nint.TryParse(File.ReadAllText(WindowPath).Trim(), out nint window) || window == 0) return;
            if (IsIconic(window)) ShowWindow(window, 9);   // SW_RESTORE
            SetForegroundWindow(window);
        }
        catch { /* окна нет — просто не дублируем игру */ }
    }

    // Подмена и возврат настройки консоли — по одному процессу за раз (параллельные запуски не перетирают друг друга).
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

    // Вернуть настройки консоли, сохранённые при перезапуске (файл RestorePath), и удалить файл.
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
            string dir = Path.Combine(AppConfig.ProjectRoot, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "console.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { /* лог не важнее запуска */ }
    }
}
