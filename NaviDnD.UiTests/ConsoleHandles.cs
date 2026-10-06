using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace NaviDnD.UiTests;

// GetStdHandle(STD_OUTPUT_HANDLE/STD_INPUT_HANDLE) не годится здесь: под "dotnet test"
// тесthost-процесс запускается с уже перенаправленным stdout/stdin (пайп для перехвата
// вывода теста), и это перенаправление не сбрасывается AttachConsole() к чужой консоли —
// GetStdHandle продолжает отдавать старый пайп. CONOUT$/CONIN$ всегда открывают ИМЕННО
// консоль, к которой процесс подключён ПРЯМО СЕЙЧАС, независимо от того, что было в
// стандартных хендлах на старте процесса.
internal static class ConsoleHandles
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    public static SafeFileHandle OpenOutput()
    {
        var handle = CreateFileW("CONOUT$", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new InvalidOperationException($"CreateFileW(CONOUT$) провалился (Win32Error={Marshal.GetLastWin32Error()}).");
        return handle;
    }

    public static SafeFileHandle OpenInput()
    {
        var handle = CreateFileW("CONIN$", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new InvalidOperationException($"CreateFileW(CONIN$) провалился (Win32Error={Marshal.GetLastWin32Error()}).");
        return handle;
    }
}
