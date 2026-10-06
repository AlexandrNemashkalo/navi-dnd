using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

// Консоль Windows (conhost) сама обрабатывает Ctrl+колесо мыши — меняет размер шрифта, а с ним и
// окно игры, ещё до того, как событие дойдёт до приложения. Щипок на тачпаде ноутбука приходит
// именно как Ctrl+колесо. Окном владеет conhost, изнутри консоли это не отключить — поэтому
// низкоуровневый перехватчик мыши (WH_MOUSE_LL) на своём потоке: Ctrl+колесо над окном игры гасится
// (размер не меняется) и передаётся игре обычной прокруткой (ConsoleMouseReader.AddWheel) —
// например, масштаб карты мира.
internal static class ConsoleZoomBlocker
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int VK_CONTROL = 0x11;

    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll")] private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc fn, nint hMod, uint threadId);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, nint hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("kernel32.dll")] private static extern nint GetConsoleWindow();
    [DllImport("kernel32.dll")] private static extern nint GetModuleHandle(string? name);

    private const uint GA_ROOT = 2;

    private static LowLevelMouseProc? _proc; // держим ссылку — иначе GC соберёт делегат под хуком
    private static nint _hook;
    private static nint _consoleWindow;

    public static void Start()
    {
        if (_proc != null) return;
        _consoleWindow = GetConsoleWindow();
        if (_consoleWindow == 0) return;
        _proc = Hook;

        // Низкоуровневому хуку нужен цикл сообщений на том же потоке.
        var thread = new Thread(() =>
        {
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == 0) return;
            while (GetMessage(out _, 0, 0, 0) > 0) { }
        }) { IsBackground = true, Name = "ConsoleZoomBlocker" };
        thread.Start();
    }

    private static nint Hook(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && wParam == WM_MOUSEWHEEL && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (GetAncestor(WindowFromPoint(info.pt), GA_ROOT) == _consoleWindow)
            {
                ConsoleMouseReader.AddWheel((short)(info.mouseData >> 16) / 120);
                return 1; // погасить: conhost не меняет шрифт и размер окна
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
