using NaviDnD.Helpers;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using System.Runtime.InteropServices;
using System.Text;

namespace NaviDnD;

internal static class ConsoleSetup
{
    private delegate IntPtr LowLevelKeyboardProcDelegate(int nCode, IntPtr wParam, IntPtr lParam);
    private static LowLevelKeyboardProcDelegate? _keyboardHookProc;
    private static IntPtr _keyboardHook;

    private const int WH_KEYBOARD_LL = 13;
    private const int VK_F11 = 0x7A;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_INPUT_HANDLE = -10;
    private const uint ENABLE_QUICK_EDIT = 0x0040;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private const int GWL_STYLE = -16;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_SYSMENU = 0x00080000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint WM_SETICON = 0x0080;
    private const uint WM_CLOSE = 0x0010;
    private const IntPtr ICON_SMALL = (IntPtr)0;
    private const IntPtr ICON_BIG = (IntPtr)1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    const int SW_RESTORE = 9;

    // Windows 11 only (build 22000+) - on older systems DwmSetWindowAttribute will return an error for an
    // unknown attribute, which is harmless (see try/catch in SetRoundedCorners).
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    // DWMWA_CLOAK - undocumented, but long-known attribute: removes the window from the DWM composition
    // DIRECTLY, regardless of its “position” (minimized/normal). This allows you to decouple “move the window
    // to its normal position so that resize works correctly” (SW_RESTORE) from “show the window to the user” -
    // the window can be expanded while remaining physically invisible, apply SetConsoleConfig, and only then
    // remove cloak.
    private const int DWMWA_CLOAK = 13;
    private const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CONSOLE_FONT_INFOEX
    {
        public uint cbSize;
        public uint nFont;
        public short FontSizeX;
        public short FontSizeY;
        public uint FontFamily;
        public uint FontWeight;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCurrentConsoleFontEx(IntPtr hConsoleOutput, bool bMaximumWindow, ref CONSOLE_FONT_INFOEX info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCurrentConsoleFontEx(IntPtr hConsoleOutput, bool bMaximumWindow, ref CONSOLE_FONT_INFOEX info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProcDelegate lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpmsg);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out WinRect rect);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WinRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    // BlockConsoleFullscreen above takes WS_SYSMENU/WS_CAPTION at the console window - it has no close button
    // or system menu. The usual Environment.Exit(0) terminates the process, but the console window itself
    // sometimes remains hanging - an explicit WM_CLOSE is guaranteed to close it before exiting.
    internal static void CloseConsoleAndExit()
    {
        if (!OperatingSystem.IsWindows() || WindowsAlacritty.Active)
        {
            UnixTerminal.Current?.Restore();
            Environment.Exit(0);
        }
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
            SendMessage(consoleWindow, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        Environment.Exit(0);
    }

    // Windows creates and immediately displays a console window BEFORE even one line of Main() has time to
    // execute - with a standard size/black background. We hide the window with the very first line and show it
    // only after everything (style without a frame, size for a map, colors, font) has already been configured -
    // otherwise the user sees the “regular console → game” blinking.
    internal static void HideWindow()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || WindowsAlacritty.Active) return;
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
            ShowWindow(consoleWindow, SW_HIDE);
    }

    internal static void ShowWindowReady()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || WindowsAlacritty.Active) return;
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
            ShowWindow(consoleWindow, SW_SHOW);
    }

    internal static void SetConsoleConfig(WorldState settings)
    {
        Console.Title = "NaviDnD";
        if (UnixTerminal.Current == null) Console.OutputEncoding = Encoding.UTF8;

        var (width, height) = WindowSize(settings);
        if (!OperatingSystem.IsWindows() || WindowsAlacritty.Active)
        {
            // The terminal leaves its font: we ask it for the same grid (xterm- compatible terminals do this)
            // and wait until it becomes no smaller.
            UnixTerminal.Current?.EnsureSize(Math.Min(width, MaxWidth), Math.Min(Math.Max(1, height - 1), MaxHeight));
            return;
        }

        // EnableAnsiSupport removes WS_CAPTION BEFORE this place, but conhost recalculates the pixels under N
        // lines as if the title had not disappeared - an empty strip remains at the bottom of the window. The
        // buffer above the window created scrolling (unacceptable), and the pixel cropping through SetWindowPos
        // affected the width. -2 lines cut off the real content (the top line disappeared) - rollback to -1.
        int windowHeight = Math.Max(1, height - 1);

        // Fixed window size ceiling - NOT dependent on screen resolution. Previously, this role was performed
        // only by Console.LargestWindowWidth/Height (the maximum that fits on the current monitor) - because of
        // this, on a monitor with a higher resolution, the window became higher/wider than on the old one,
        // although the map was the same. The numbers match Window Size in the shortcut properties.
        if (width > MaxWidth) width = MaxWidth;
        if (windowHeight > MaxHeight) windowHeight = MaxHeight;

        // Font: in window - 16; the entire screen is scaled (auto is the largest, at which the window fits into
        // the monitor). The grid is the same (rows/columns), the cell grows. It doesn’t fit (the character
        // width of the font is not exactly half the height) - one step too small.
        short font = FontSizeFor(width, windowHeight);
        SetConsoleFontSize(font);
        while ((Console.LargestWindowWidth < width || Console.LargestWindowHeight < windowHeight)
               && FontSizes(FontFace).Where(x => x < font).DefaultIfEmpty(0).Max() is var smaller and > 0)
            SetConsoleFontSize(font = (short)smaller);   // does not fit - the next size of this font

        // Console.LargestWindowWidth/Height - emergency barrier ONLY in case even a fixed ceiling above does
        // not fit on the screen (very small monitor) - under normal conditions it should not work.
        if (width > Console.LargestWindowWidth)
            width = Console.LargestWindowWidth;
        if (windowHeight > Console.LargestWindowHeight)
            windowHeight = Console.LargestWindowHeight;

        // We do NOT reduce the width: "+3" in width (the margin above border.InnerWidth) is not extra space,
        // but a fuse against auto-transfer on the last column of the console. -1 to the width ate up this
        // margin and caused line wrapping at the very edge (Windows console transfers text written exactly to
        // the last column of the buffer).
        Console.WindowWidth = width;
        Console.WindowHeight = windowHeight;
        Console.BufferWidth = Console.WindowWidth;
        Console.BufferHeight = Console.WindowHeight;
        TrimWindowToRows();
        NaviDnD.Helpers.FullscreenBackdrop.Recenter();   // whole screen - after changing the size the window is again centered
    }

    private const int MaxWidth = 150;
    private const int MaxHeight = 63;

    // The grid under which the game is laid out: a card field with a frame, a card and dialogue under it.
    internal static (int width, int height) WindowSize(WorldState settings)
    {
        var display = new DisplayConfig();
        // frame(2) + auto-transfer guard on the right(1) + DisplayConfig.LeftMargin on the left.
        int width = display.InnerWidth(display.ViewCols(settings.Map)) + 3 + DisplayConfig.LeftMargin;
        // The character/ability card is stretched to at least FixedMapContentHigh (see DisplayConfig.MaxHigh) -
        // the window must be that high, otherwise there is no room left for the dialog box and the picture
        // block under the card.
        int cardHigh = Math.Max(display.ViewRows(settings.Map) * 2, DisplayConfig.FixedMapContentHigh);
        return (width, cardHigh + display.MaxHistoryLines + 18);
    }

    // The console window can be slightly higher than an integer number of lines (the header is removed, and
    // conhost counts the pixels with it) - under the bottom frame there was a strip of several pixels, which
    // the console painted with the current background (either water from the world map, or another color). We
    // trim the height of the window exactly to the lines; We don’t touch the width (its adjustment broke the
    // transfer on the last column).
    private static void TrimWindowToRows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || WindowsAlacritty.Active) return;
        try
        {
            var hwnd = GetConsoleWindow();
            if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var client)) return;
            var font = new CONSOLE_FONT_INFOEX();
            font.cbSize = (uint)Marshal.SizeOf(font);
            if (!GetCurrentConsoleFontEx(GetStdHandle(STD_OUTPUT_HANDLE), false, ref font) || font.FontSizeY <= 0) return;
            int extra = (client.Bottom - client.Top) - Term.WindowHeight * font.FontSizeY;
            if (extra <= 0 || extra >= font.FontSizeY || !GetWindowRect(hwnd, out var win)) return;
            const uint SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, win.Right - win.Left, win.Bottom - win.Top - extra, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch { /*didn’t work - the strip will remain as before*/ }
    }

    // The entire screen (SCREEN setting) and the font size in it: 0 - auto, otherwise the size (does not fit - as much as fits).
    internal static bool Fullscreen;
    // Console font ("FONT TYPE" setting) and which ones can be selected.
    internal static readonly string[] FontFaces = ["Consolas", "DejaVu Sans Mono"];

    // Game fonts (folder Fonts: DejaVu Sans Mono) - are connected while the game is running, without
    // installation into the system (AddFontResourceEx without FR_PRIVATE: the console draws in another process
    // and should see them), when exiting - they are removed.
    internal static void LoadBundledFonts()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || WindowsAlacritty.Active) return;
        try
        {
            string dir = Path.Combine(AppConfig.ProjectRoot, "Fonts");
            if (!Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "*.ttf");
            foreach (var f in files) AddFontResourceEx(f, 0, IntPtr.Zero);
            // Notify about fonts without waiting for other applications: the HWND_BROADCAST timeout is applied
            // to each window separately and can delay the appearance of the menu for seconds.
            PostMessage((IntPtr)0xffff, 0x001D /* WM_FONTCHANGE */, IntPtr.Zero, IntPtr.Zero);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { foreach (var f in files) RemoveFontResourceEx(f, 0, IntPtr.Zero); };
        }
        catch { /*did not connect - they will remain in the list, the console will substitute its own font*/ }
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool RemoveFontResourceEx(string file, uint flags, IntPtr reserved);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    internal static string FontFace = "Consolas";
    internal static int FullscreenFontSize;
    private const short BaseFontSize = 16;

    // Font sizes at which it looks straight (cell 1:2): Consolas - 14 and 16, DejaVu Sans Mono - 16–22.
    internal static int[] FontSizes(string face) => face switch
    {
        "Consolas" => [14, 16],
        "DejaVu Sans Mono" => [14, 16, 20, 22],
        _ => [16],
    };

    // In the window - 16; whole screen - the selected font size or (auto) the largest of its sizes at which the
    // game fits into the monitor; Even the smallest one doesn’t fit - the smallest one.
    private static short FontSizeFor(int cols, int rows)
    {
        if (!Fullscreen) return BaseFontSize;
        var (mw, mh) = NaviDnD.Helpers.FullscreenBackdrop.MonitorSize();
        int fit = Math.Min(mh / Math.Max(1, rows), mw * 2 / Math.Max(1, cols));   // cell - exactly 1:2
        var sizes = FontSizes(FontFace);
        var fitting = sizes.Where(s => s <= fit && (FullscreenFontSize <= 0 || s <= FullscreenFontSize)).ToList();
        return (short)(fitting.Count > 0 ? fitting.Max() : sizes.Min());
    }

    private static void SetConsoleFontSize(short size)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || WindowsAlacritty.Active) return;
        var handle = GetStdHandle(STD_OUTPUT_HANDLE);
        var info = new CONSOLE_FONT_INFOEX();
        info.cbSize = (uint)Marshal.SizeOf(info);
        GetCurrentConsoleFontEx(handle, false, ref info);
        // The width of the cell is exactly half the height: in Consolas itself it is 1:2 only up to 16 (24 →
        // 11×24), and semi-blocks of maps, braille, frames in large print were flattened; the explicit width of
        // the cantilever is respected (24 → 12×24).
        info.FontSizeX = (short)(size / 2);
        info.FontSizeY = size;
        info.FaceName = FontFace;
        SetCurrentConsoleFontEx(handle, false, ref info);
    }

    private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // F11 (full-screen console mode breaks the layout) is muted only in the game window - not in other programs.
        if (nCode >= 0 && Marshal.ReadInt32(lParam) == VK_F11 && GetForegroundWindow() == GetConsoleWindow())
        {
            if (wParam == (IntPtr)0x0100 || wParam == (IntPtr)0x0104) _ = Task.Run(Helpers.Speech.Stop);
            return (IntPtr)1;
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    internal static void BlockConsoleFullscreen()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || WindowsAlacritty.Active) return;
        _keyboardHookProc = KeyboardHookCallback;
        var thread = new Thread(() =>
        {
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, GetModuleHandle(null), 0);
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            if (_keyboardHook != IntPtr.Zero)
                UnhookWindowsHookEx(_keyboardHook);
        });
        thread.IsBackground = true;
        thread.Start();
    }

    internal static void EnableAnsiSupport()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || WindowsAlacritty.Active) return;

        var handle = GetStdHandle(STD_OUTPUT_HANDLE);
        if (GetConsoleMode(handle, out uint mode))
        {
            mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
            SetConsoleMode(handle, mode);
        }

        var consoleWindow = GetConsoleWindow();
        int style = GetWindowLong(consoleWindow, GWL_STYLE);
        style &= ~(WS_THICKFRAME | WS_MAXIMIZEBOX | WS_CAPTION | WS_SYSMENU);
        SetWindowLong(consoleWindow, GWL_STYLE, style);
        // Style will not be applied visually without SWP_FRAMECHANGED - window will not redraw non-client area
        // (header/frame) on its own after SetWindowLong.
        SetWindowPos(consoleWindow, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

        SetConsoleIcon();
        SetRoundedCorners(consoleWindow);
    }

    // DWMWA_WINDOW_CORNER_PREFERENCE - available only starting from Windows 11 (build 22000). On older systems,
    // DwmSetWindowAttribute will return an error for an unknown attribute - harmless, it just won't round the
    // corners.
    private static void SetRoundedCorners(IntPtr consoleWindow)
    {
        int preference = DWMWCP_ROUND;
        DwmSetWindowAttribute(consoleWindow, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    internal static void DisableQuickEdit()
    {
        if (!OperatingSystem.IsWindows() || WindowsAlacritty.Active) return;
        IntPtr handle = GetStdHandle(STD_INPUT_HANDLE);
        GetConsoleMode(handle, out uint mode);
        mode &= ~ENABLE_QUICK_EDIT;
        SetConsoleMode(handle, mode);
    }

    internal static void SetConsoleIcon()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        IntPtr hWnd = WindowControls.NativeWindow;
        if (hWnd == IntPtr.Zero) return;

        IntPtr hIcon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "dnd.ico"), IMAGE_ICON, 0, 0, LR_LOADFROMFILE);
        if (hIcon != IntPtr.Zero)
        {
            SendMessage(hWnd, WM_SETICON, ICON_SMALL, hIcon);
            SendMessage(hWnd, WM_SETICON, ICON_BIG, hIcon);
        }
        else
        {
            Console.WriteLine(L.T("Could not load the icon. Check the path and format (.ico required)."));
        }
    }


    internal static void ShowWindow()
    {
        if (!OperatingSystem.IsWindows() || WindowsAlacritty.Active) return;
        IntPtr hWnd = GetConsoleWindow();
        if (hWnd == IntPtr.Zero) return;

        // // Cloak BEFORE restoring the position - the "collapsed → normal" transition itself remains
        // physically // invisible, despite the fact that technically the window is already in its normal
        // position and visible for Windows. int cloak = 1; DwmSetWindowAttribute(hWnd, DWMWA_CLOAK, ref cloak,
        // sizeof(int));
        ShowWindow(hWnd, SW_RESTORE);
    }

    // Removes cloak placed in ShowWindow() - call AFTER SetConsoleConfig(), when resize is already applied to
    // the window in its normal position and everything is ready to be shown to the user.
    internal static void RevealWindow()
    {
        if (!OperatingSystem.IsWindows() || WindowsAlacritty.Active) return;
        IntPtr hWnd = GetConsoleWindow();
        if (hWnd == IntPtr.Zero) return;

        int uncloak = 0;
        DwmSetWindowAttribute(hWnd, DWMWA_CLOAK, ref uncloak, sizeof(int));
    }
}
