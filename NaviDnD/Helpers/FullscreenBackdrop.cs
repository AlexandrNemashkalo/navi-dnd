using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

// Режим «весь экран» (настройка ЭКРАН): окно игры остаётся своего размера и рисуется как обычно, а под ним — окно-
// подложка на весь монитор (с панелью задач) цвета фона игры; окно игры — по центру монитора, поверх подложки.
// Подложка — отдельное Win32-окно в своём потоке: не активируется кликом, не видно в панели задач и Alt+Tab.
public static class FullscreenBackdrop
{
    public static bool Active { get; private set; }

    private static IntPtr _backdrop;
    private static (int x, int y)? _windowedPos;     // где было окно игры до режима — вернуть при выключении
    private static WndProc? _wndProc;                // держим делегат — иначе его соберёт GC
    private static readonly object _lock = new();

    // Включить / выключить (повторный вызов с тем же значением ничего не делает). color — фон игры.
    public static void Set(bool on, List<int> color)
    {
        if (!OperatingSystem.IsWindows()) return;
        lock (_lock)
        {
            if (on == Active) { if (on) Recenter(); return; }
            var console = GetConsoleWindow();
            if (console == IntPtr.Zero) return;
            if (on)
            {
                if (GetWindowRect(console, out var r)) _windowedPos = (r.Left, r.Top);
                var mon = MonitorRect(console);
                var ready = new ManualResetEventSlim();
                var thread = new Thread(() => RunBackdrop(mon, color, ready)) { IsBackground = true, Name = "FullscreenBackdrop" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                ready.Wait(2000);
                Active = _backdrop != IntPtr.Zero;
                if (Active) Borderless(console, true);
                Recenter();
                SetForegroundWindow(console);
            }
            else
            {
                if (_backdrop != IntPtr.Zero) PostMessage(_backdrop, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                _backdrop = IntPtr.Zero;
                Active = false;
                Borderless(console, false);
                SetWindowPos(console, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                if (_windowedPos is var (x, y))
                    SetWindowPos(console, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }
    }

    // Без оформления Windows: ни системной рамки, ни скругления углов, ни тени (Windows 11) — остаётся только своя
    // рамка игры. Выключение — стили и оформление как были.
    private static (int style, int exStyle)? _savedStyles;

    private static void Borderless(IntPtr console, bool on)
    {
        if (on)
        {
            int style = GetWindowLong(console, GWL_STYLE), ex = GetWindowLong(console, GWL_EXSTYLE);
            _savedStyles = (style, ex);
            SetWindowLong(console, GWL_STYLE, style & ~(WS_BORDER | WS_DLGFRAME | WS_THICKFRAME));
            SetWindowLong(console, GWL_EXSTYLE, ex & ~(WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE | WS_EX_DLGMODALFRAME));
        }
        else if (_savedStyles is var (style, ex))
        {
            SetWindowLong(console, GWL_STYLE, style);
            SetWindowLong(console, GWL_EXSTYLE, ex);
        }
        int corner = on ? DWMWCP_DONOTROUND : DWMWCP_ROUND;
        DwmSetWindowAttribute(console, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        int border = on ? DWMWA_COLOR_NONE : DWMWA_COLOR_DEFAULT;
        DwmSetWindowAttribute(console, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        int nc = on ? DWMNCRP_DISABLED : DWMNCRP_USEWINDOWSTYLE;   // без неклиентской отрисовки — без тени
        DwmSetWindowAttribute(console, DWMWA_NCRENDERING_POLICY, ref nc, sizeof(int));
        SetWindowPos(console, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    // Окно игры — по центру монитора поверх подложки (после смены размера окна — снова).
    public static void Recenter()
    {
        if (!Active || !OperatingSystem.IsWindows()) return;
        var console = GetConsoleWindow();
        if (console == IntPtr.Zero || !GetWindowRect(console, out var r)) return;
        var mon = MonitorRect(console);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int x = mon.Left + Math.Max(0, (mon.Right - mon.Left - w) / 2), y = mon.Top + Math.Max(0, (mon.Bottom - mon.Top - h) / 2);
        SetWindowPos(console, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    // Размер монитора, на котором окно игры (для масштаба во весь экран).
    public static (int width, int height) MonitorSize()
    {
        if (!OperatingSystem.IsWindows()) return (1920, 1080);
        var r = MonitorRect(GetConsoleWindow());
        return (r.Right - r.Left, r.Bottom - r.Top);
    }

    private static RECT MonitorRect(IntPtr window)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        var monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
        return GetMonitorInfo(monitor, ref info) ? info.rcMonitor : new RECT { Right = 1920, Bottom = 1080 };
    }

    private static void RunBackdrop(RECT mon, List<int> color, ManualResetEventSlim ready)
    {
        try
        {
            _wndProc = BackdropProc;
            var hInstance = GetModuleHandle(null);
            string cls = "NaviDnDBackdrop";
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hInstance,
                hbrBackground = CreateSolidBrush((uint)(color[0] | color[1] << 8 | color[2] << 16)),
                hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
                lpszClassName = cls,
            };
            RegisterClassEx(ref wc);   // повторная регистрация после выключения — ошибка, класс уже есть: не мешает
            _backdrop = CreateWindowEx(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, cls, "NaviDnD", WS_POPUP | WS_VISIBLE,
                mon.Left, mon.Top, mon.Right - mon.Left, mon.Bottom - mon.Top, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        }
        finally { ready.Set(); }
        if (_backdrop == IntPtr.Zero) return;
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private static IntPtr BackdropProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE: return MA_NOACTIVATE;   // клик по подложке не уводит фокус от игры
            case WM_CLOSE: DestroyWindow(hWnd); return IntPtr.Zero;
            case WM_DESTROY: PostQuitMessage(0); return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    // ── Win32 ──
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    private const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000;
    private const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint WM_CLOSE = 0x10, WM_DESTROY = 0x2, WM_MOUSEACTIVATE = 0x21;
    private static readonly IntPtr MA_NOACTIVATE = 3, HWND_TOPMOST = -1, HWND_NOTOPMOST = -2, IDC_ARROW = 32512;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    private const uint MONITOR_DEFAULTTONEAREST = 2, SWP_FRAMECHANGED = 0x20;
    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    private const int WS_BORDER = 0x00800000, WS_DLGFRAME = 0x00400000, WS_THICKFRAME = 0x00040000;
    private const int WS_EX_WINDOWEDGE = 0x100, WS_EX_CLIENTEDGE = 0x200, WS_EX_STATICEDGE = 0x20000, WS_EX_DLGMODALFRAME = 0x1;
    private const int DWMWA_NCRENDERING_POLICY = 2, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
    private const int DWMNCRP_USEWINDOWSTYLE = 0, DWMNCRP_DISABLED = 1, DWMWCP_ROUND = 2, DWMWCP_DONOTROUND = 1;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE), DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
}
