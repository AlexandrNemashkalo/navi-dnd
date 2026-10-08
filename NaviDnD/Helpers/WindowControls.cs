using System.Runtime.InteropServices;
using System.Text;
using NaviDnD.Data;

namespace NaviDnD.Helpers;

public static class WindowControls
{
    // Braille blank occupies one empty cell and keeps Consolas font fallback available
    // for the square even when hover gives it a separate foreground-color run.
    private const string ConsolasButtons = "  ━ \u2800▢  ✕ ";
    private const string DejaVuButtons = "  –  ▢  ✕ ";
    public static string Buttons => ButtonsForFont(ConsoleSetup.FontFace);

    public static string ButtonsForFont(string font) =>
        string.Equals(font, "DejaVu Sans Mono", StringComparison.OrdinalIgnoreCase)
            ? DejaVuButtons : ConsolasButtons;
    private static Action? _toggle, _close;
    private static Timer? _timer;
    private static volatile bool _minimized;
    private static readonly object _stateLock = new();
    private static bool _attentionNotified;
    private static DisplayConfig? _display;

    public static void Draw(DisplayConfig display, int hovered = -1)
    {
        _display = display;
        var frame = new StringBuilder();
        var baseFg = display.MainForeground;
        var baseBg = display.MainBackground;
        frame.Append($"\x1b[38;2;{baseFg[0]};{baseFg[1]};{baseFg[2]}m\x1b[48;2;{baseBg[0]};{baseBg[1]};{baseBg[2]}m");
        frame.Append(Buttons[0]);
        for (int button = 0; button < 3; button++)
        {
            var fg = button == hovered
                ? hovered == 2 ? new List<int> { 235, 110, 110 } : ColorHelper.Pale(display.MainForeground, 0.75)
                : display.MainForeground;
            frame.Append($"\x1b[38;2;{fg[0]};{fg[1]};{fg[2]}m");
            frame.Append(Buttons.AsSpan(1 + button * 3, 3));
        }
        var background = display.MainBackground;
        var foreground = display.MainForeground;
        frame.Append($"\x1b[38;2;{foreground[0]};{foreground[1]};{foreground[2]}m");
        // Send complete glyphs and their styles in one write, including hover redraws.
        string text = frame.ToString();
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected
            || !WriteConsoleW(GetStdHandle(-11), text, (uint)text.Length, out _, 0))
            ColorHelper.WriteColored(text, display.MainForeground, background);
    }

    public static void Draw(int hovered) => Draw(_display ?? new DisplayConfig(), hovered);

    public static int HitTest(int x, int y, int left, int width)
    {
        if (y != 1 || width < Buttons.Length) return -1;
        int offset = x - (left + width - Buttons.Length);
        return offset is >= 1 and < 10 ? (offset - 1) / 3 : -1;
    }

    public static void Start(Action toggle, Action close)
    {
        _toggle = toggle;
        _close = close;
        if (!OperatingSystem.IsWindows()) return;
        _timer ??= new Timer(_ =>
        {
            lock (_stateLock)
            {
                var window = GetConsoleWindow();
                bool minimized = IsIconic(window);
                if (!minimized && _attentionNotified)
                {
                    Flash(window, 0, 0);
                    _attentionNotified = false;
                }
                if (minimized == _minimized) return;
                _minimized = minimized;
                Sound.Suspended = minimized;
                Speech.SetWindowMinimized(minimized);
                Music.SetPaused(minimized);
                FullscreenBackdrop.SetMinimized(minimized);
            }
        }, null, 0, 100);
    }

    public static void NotifyIfMinimized()
    {
        if (!OperatingSystem.IsWindows()) return;
        lock (_stateLock)
        {
            var window = GetConsoleWindow();
            if (window == 0 || !IsIconic(window) || _attentionNotified) return;
            _attentionNotified = true;
            Flash(window, 2, 3); // FLASHW_TRAY: three flashes, without activation or sound.
        }
    }

    private static void Flash(nint window, uint flags, uint count)
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = window,
            dwFlags = flags, uCount = count
        };
        FlashWindowEx(ref info);
    }

    public static void Activate(int button)
    {
        Sound.PlayClick();
        if (button == 0 && OperatingSystem.IsWindows())
        {
            lock (_stateLock)
            {
                _minimized = true;
                Sound.Suspended = true;
                Speech.SetWindowMinimized(true);
                Music.SetPaused(true);
                FullscreenBackdrop.SetMinimized(true);
                ShowWindow(GetConsoleWindow(), 6);
            }
        }
        else if (button == 1) _toggle?.Invoke();
        else if (button == 2) _close?.Invoke();
    }

    [DllImport("kernel32.dll")] private static extern nint GetConsoleWindow();
    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public nint hwnd;
        public uint dwFlags, uCount, dwTimeout;
    }
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO info);
    [DllImport("kernel32.dll")] private static extern nint GetStdHandle(int handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool WriteConsoleW(nint handle, string text, uint length, out uint written, nint reserved);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
}
