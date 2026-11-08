using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

// Requests to the AI are separate processes (Claude CLI, and it launches its own: node, MCP-server). So that
// they do not remain hanging when the game is closed (cross, Esc from the menu, crash), each such process is
// placed in Windows Job Object with KILL_ON_JOB_CLOSE: the game ended in any way - the system closes its handle
// job, and the entire tree of request processes ends itself (their child processes end up in job
// automatically). On macOS/Linux job there is no: processes are remembered, and their trees are terminated when
// exiting the game normally, with an unhandled exception and by SIGHUP (close the terminal window) / SIGTERM /
// SIGINT. SIGKILL games cannot be intercepted - then the MCP- server will still end when the channels are
// closed CLI.
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

    // The job descriptor lives until the end of the game process (we don’t close it - the system will close it upon exit).
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

    // The request process is in job (it didn’t work out - it just lives as before).
    public static void Add(Process process)
    {
        if (!OperatingSystem.IsWindows()) { AddUnix(process); return; }
        try
        {
            if (_job.Value != IntPtr.Zero) AssignProcessToJobObject(_job.Value, process.Handle);
        }
        catch { /*the process has already completed or you do not have rights - do not interfere with the request*/ }
    }

    // pid → start time: providers release their Process, and the start time protects against overused pid.
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
        // Stored in a static field: the registration collected by the collector stops picking up the signal. The default action (exit) is still executed.
        return [.. new[] { PosixSignal.SIGHUP, PosixSignal.SIGTERM, PosixSignal.SIGINT }
            .Select(signal => PosixSignalRegistration.Create(signal, _ => KillUnixChildren()))];
    }

    // Before restarting after update (macOS):), request processes will not survive the replacement of the game process.
    internal static void KillAll()
    {
        if (!OperatingSystem.IsWindows()) KillUnixChildren();
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
