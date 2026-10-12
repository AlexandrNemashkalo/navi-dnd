using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

// Запросы к нейронке — отдельные процессы (Claude CLI, а он запускает свои: node, MCP-сервер). Чтобы они не
// оставались висеть, когда игру закрыли (крестик, Esc из меню, падение), каждый такой процесс кладётся в
// Windows Job Object с KILL_ON_JOB_CLOSE: игра завершилась как угодно — система закрывает её дескриптор job,
// и всё дерево процессов запроса завершается само (их дочерние процессы попадают в job автоматически).
// На macOS/Linux job нет: процессы запоминаются, и их деревья завершаются при обычном выходе из игры, при
// необработанном исключении и по SIGHUP (закрыли окно терминала) / SIGTERM / SIGINT. SIGKILL игры не перехватить —
// тогда MCP-сервер всё равно завершится, когда закроются каналы CLI.
public static class ChildProcessJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    // Дескриптор job живёт до конца процесса игры (не закрываем — его закроет система при выходе).
    private static readonly Lazy<IntPtr> _job = new(Create);

    private static IntPtr Create()
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
        return job;
    }

    // Процесс запроса — в job (не вышло — просто живёт как раньше).
    public static void Add(Process process)
    {
        if (!OperatingSystem.IsWindows()) { AddUnix(process); return; }
        try
        {
            if (_job.Value != IntPtr.Zero) AssignProcessToJobObject(_job.Value, process.Handle);
        }
        catch { /* процесс уже завершился или нет прав — не мешаем запросу */ }
    }

    // pid → время старта: провайдеры освобождают свои Process, а время старта защищает от переиспользованного pid.
    private static readonly ConcurrentDictionary<int, DateTime> _unixChildren = new();
    private static readonly Lazy<PosixSignalRegistration[]> _unixHooks = new(InstallUnixHooks);

    private static void AddUnix(Process process)
    {
        try
        {
            _ = _unixHooks.Value;
            int id = process.Id;
            _unixChildren[id] = process.StartTime;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => _unixChildren.TryRemove(id, out _);
            if (process.HasExited) _unixChildren.TryRemove(id, out _);
        }
        catch { /* the process already exited — nothing to clean up */ }
    }

    private static PosixSignalRegistration[] InstallUnixHooks()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillUnixChildren();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => KillUnixChildren();
        // Хранится в статическом поле: собранная сборщиком регистрация перестаёт ловить сигнал. Действие по умолчанию (выход) всё равно выполняется.
        return [.. new[] { PosixSignal.SIGHUP, PosixSignal.SIGTERM, PosixSignal.SIGINT }
            .Select(signal => PosixSignalRegistration.Create(signal, _ => KillUnixChildren()))];
    }

    private static void KillUnixChildren()
    {
        foreach (var (id, started) in _unixChildren.ToArray())
        {
            try
            {
                using var process = Process.GetProcessById(id);
                if (process.StartTime == started) process.Kill(entireProcessTree: true);
            }
            catch { /* already gone */ }
            _unixChildren.TryRemove(id, out _);
        }
    }
}
