using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NaviDnD.Helpers;

// NaviDnD.app on macOS. Finder launches the application without a terminal - then the game replaces itself with
// its terminal (Alacritty from Contents/MacOS, settings - Contents/Resources/alacritty.toml): icon NaviDnD in
// Dock, window without buttons macOS exactly under the game grid or the entire screen, font size - from the
// game settings. In it starts the game again - with TerminalFlag. The terminal window cannot be changed after
// launch - changing the mode and font in the settings restarts it (Relaunch).
[SupportedOSPlatform("macos")]
internal static class MacApp
{
    internal const string TerminalFlag = "--navidnd-terminal";
    private const string SettingsFlag = "--navidnd-settings=";
    // release-version.txt from application root: patches update NaviDnD.app in its entirety, along with the label.
    internal const string VersionFile = "Contents/Resources/" + Installer.Patch.DefaultVersionFile;
    private const int Columns = 150, Rows = 63;

    // Font - Menlo (available on any Mac): with it the game grid is even. The width of the cell and the height of the line are in fractions of a point.
    private const string Font = "Menlo";
    private const double CellWidth = 0.602, CellHeight = 1.164;
    private const string TopFlag = "--navidnd-top=";

    internal static string BundlePath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));

    // The game runs in its own terminal: it draws the window buttons itself (minimize - hide the application, close - exit).
    internal static bool InOwnTerminal { get; private set; }

    // Settings item to return to after restarting the window; null - normal launch. Take it once.
    private static int? SettingsFocus { get; set; }

    internal static int? TakeSettingsFocus()
    {
        int? focus = SettingsFocus;
        SettingsFocus = null;
        return focus;
    }

    // true - this process has done its job: the terminal is launched instead of it or the application is opened again from the real place.
    internal static bool Launch(string[] args)
    {
        if (!AppConfig.InMacApp) return false;
        if (args.Contains(TerminalFlag))
        {
            InOwnTerminal = true;
            if (args.FirstOrDefault(a => a.StartsWith(SettingsFlag, StringComparison.Ordinal)) is { } focus
                && int.TryParse(focus[SettingsFlag.Length..], out int item)) SettingsFocus = item;
            if (args.FirstOrDefault(a => a.StartsWith(TopFlag, StringComparison.Ordinal)) is { } top
                && int.TryParse(top[TopFlag.Length..], out int rows)) UnixTerminal.ReservedTopRows = rows;
            return false;
        }
        if (!Console.IsInputRedirected) return false;   // launched from the terminal manually - play in it
        string bundle = BundlePath;
        // macOS launches the downloaded application, opened directly from “Downloads”, from a temporary
        // read-only copy (App Translocation) - updates cannot be written there. We remove quarantine from the
        // real application and open it.
        if (bundle.Contains("/AppTranslocation/", StringComparison.Ordinal) && OriginalPath(bundle) is { } original && original != bundle)
        {
            UnixTools.Run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", original], TimeSpan.FromMinutes(1));
            UnixTools.Run("/usr/bin/open", ["-n", original], TimeSpan.FromSeconds(30));
            return true;
        }
        UnixTools.ClearQuarantine(bundle, Path.Combine(AppContext.BaseDirectory, "libSkiaSharp.dylib"));
        var config = new AppConfig();
        config.LoadUserSettings();
        string?[] argv = [.. TerminalArguments(config, args), null];
        execv(argv[0]!, argv);
        return false;   // the terminal did not start - we play without it (the window will not be visible, but there is no need to fall)
    }

    // New window with current settings and return to settings to the same item; this window closes with the game.
    internal static void Relaunch(int settingsItem)
    {
        // Launch Services owns the replacement app; it must not inherit this terminal's session or pipes.
        // The new launcher reads the saved font/window settings and replaces itself with Alacritty.
        if (RequestRelaunch(BundlePath, settingsItem,
            args => UnixTools.Run("/usr/bin/open", args, TimeSpan.FromSeconds(30))?.ExitCode))
            Environment.Exit(0);
    }

    internal static bool RequestRelaunch(string bundle, int settingsItem, Func<string[], int?> launch) =>
        launch(["-n", bundle, "--args", SettingsFlag + settingsItem]) == 0;

    // Font sizes for settings: auto and those at which the game fits on the screen in the selected mode.
    internal static int[] FontSizes(AppConfig config)
    {
        double fit = Fit(config.Fullscreen, Screen());
        return [0, .. new[] { 10, 11, 12, 13, 14, 15, 16, 18, 20, 22 }.Where(size => size <= fit)];
    }

    // The largest typeface, in which 150 × 63 cells are placed: the entire screen - except for the cutout at
    // the top (MacBook with a “bang”), the window - under the menu bar and with an indent.
    private static double Fit(bool fullscreen, (double Width, double Height, double Scale, double Notch) screen)
    {
        double width = screen.Width - (fullscreen ? 0 : 16), height = screen.Height - (fullscreen ? screen.Notch : 48);
        for (double size = 30; size >= 8; size -= 0.5)
            if (Columns * Cell(size, CellWidth, screen.Scale) <= width && Rows * Cell(size, CellHeight, screen.Scale) <= height) return size;
        return 8;
    }

    // Cell Alacritty - in whole physical pixels.
    private static double Cell(double size, double factor, double scale) => Math.Ceiling(size * factor * scale) / scale;

    private static List<string> TerminalArguments(AppConfig config, IEnumerable<string> gameArguments)
    {
        var screen = Screen();
        double fit = Fit(config.Fullscreen, screen);
        double font = config.FullscreenFontSize > 0 ? Math.Min(config.FullscreenFontSize, fit) : fit;
        double cellWidth = Cell(font, CellWidth, screen.Scale), cellHeight = Cell(font, CellHeight, screen.Scale);
        var background = new Data.DisplayConfig().MainBackground;
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var arguments = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "alacritty"), "--config-file", Path.Combine(BundlePath, "Contents", "Resources", "alacritty.toml"),
            "-o", $"font.normal.family=\"{Font}\"", "-o", $"font.size={font.ToString(invariant)}",
            "-o", $"colors.primary.background=\"#{background[0]:x2}{background[1]:x2}{background[2]:x2}\"",
        };
        var game = new List<string> { Path.Combine(AppContext.BaseDirectory, "NaviDnD"), TerminalFlag };
        if (config.Fullscreen)
        {
            // The terminal is full screen, the background around the game is its background (like “full screen”
            // on Windows). The game itself stands in the center of the grid (UnixTerminal), skips lines under
            // the cutout; Alacritty divides the rest of the pixels equally.
            arguments.AddRange(["-o", "window.startup_mode=\"SimpleFullscreen\"", "-o", "window.dynamic_padding=true"]);
            if (screen.Notch > 0) game.Add(TopFlag + (int)Math.Ceiling(screen.Notch / cellHeight));
        }
        else
        {
            // Window - in the center of the screen below the menu bar; position for Alacritty - in physical pixels.
            double windowWidth = Columns * cellWidth, windowHeight = Rows * cellHeight;
            int x = (int)Math.Max(0, (screen.Width - windowWidth) / 2 * screen.Scale);
            int y = (int)Math.Max(0, (40 + (screen.Height - 40 - windowHeight) / 2) * screen.Scale);
            arguments.AddRange(["-o", $"window.position={{ x = {x}, y = {y} }}"]);
        }
        arguments.Add("-e");
        arguments.AddRange(game);
        arguments.AddRange(gameArguments.Where(a => a != TerminalFlag && !a.StartsWith(TopFlag, StringComparison.Ordinal)));
        return arguments;
    }

    // The size of the main screen in pixels, their scale (Retina - 2) and the height of the cutout at the top (safe area of the screen).
    private static (double Width, double Height, double Scale, double Notch) Screen()
    {
        double width = 1440, height = 900, scale = 2, notch = 0;
        try
        {
            uint display = CGMainDisplayID();
            var bounds = CGDisplayBounds(display);
            if (bounds.Width > 0 && bounds.Height > 0) (width, height) = (bounds.Width, bounds.Height);
            nint mode = CGDisplayCopyDisplayMode(display);
            if (mode != 0)
            {
                nuint points = CGDisplayModeGetWidth(mode), pixels = CGDisplayModeGetPixelWidth(mode);
                if (points > 0 && pixels >= points) scale = (double)pixels / points;
                CGDisplayModeRelease(mode);
            }
            NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            nint main = objc_msgSend(objc_getClass("NSScreen"), sel_registerName("mainScreen"));
            if (main != 0 && (objc_msgSend(main, sel_registerName("respondsToSelector:"), sel_registerName("safeAreaInsets")) & 0xFF) != 0)
                notch = Math.Max(0, objc_msgSend_insets(main, sel_registerName("safeAreaInsets")).Top);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        return (width, height, scale, notch);
    }

    // Real application path for temporary copy (Security.framework).
    private static string? OriginalPath(string translocated)
    {
        try
        {
            byte[] path = System.Text.Encoding.UTF8.GetBytes(translocated);
            nint url = CFURLCreateFromFileSystemRepresentation(0, path, path.Length, true);
            if (url == 0) return null;
            try
            {
                nint original = SecTranslocateCreateOriginalPathForURL(url, 0);
                if (original == 0) return null;
                try
                {
                    var buffer = new byte[4096];
                    return CFURLGetFileSystemRepresentation(original, true, buffer, buffer.Length)
                        ? System.Text.Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0)) : null;
                }
                finally { CFRelease(original); }
            }
            finally { CFRelease(url); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    // Game terminal is the parent process; you can hide or check it without special permissions (NSRunningApplication).
    internal static void Hide() => SendToTerminal("hide");

    internal static bool IsHidden() => (SendToTerminal("isHidden") & 0xFF) != 0;   // BOOL - low byte

    private static nint SendToTerminal(string selector)
    {
        try
        {
            if (!InOwnTerminal) return 0;
            NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            nint type = objc_getClass("NSRunningApplication");
            nint app = objc_msgSend(type, sel_registerName("runningApplicationWithProcessIdentifier:"), getppid());
            return app == 0 ? 0 : objc_msgSend(app, sel_registerName(selector));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return 0; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect { public double X, Y, Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NSEdgeInsets { public double Top, Left, Bottom, Right; }

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [DllImport(CoreGraphics)] private static extern uint CGMainDisplayID();
    [DllImport(CoreGraphics)] private static extern CGRect CGDisplayBounds(uint display);
    [DllImport(CoreGraphics)] private static extern nint CGDisplayCopyDisplayMode(uint display);
    [DllImport(CoreGraphics)] private static extern nuint CGDisplayModeGetWidth(nint mode);
    [DllImport(CoreGraphics)] private static extern nuint CGDisplayModeGetPixelWidth(nint mode);
    [DllImport(CoreGraphics)] private static extern void CGDisplayModeRelease(nint mode);
    [DllImport(CoreFoundation)] private static extern nint CFURLCreateFromFileSystemRepresentation(nint allocator, byte[] path, nint length, bool directory);
    [DllImport(CoreFoundation)] private static extern bool CFURLGetFileSystemRepresentation(nint url, bool resolve, byte[] buffer, nint length);
    [DllImport(CoreFoundation)] private static extern void CFRelease(nint value);
    [DllImport("/System/Library/Frameworks/Security.framework/Security")] private static extern nint SecTranslocateCreateOriginalPathForURL(nint url, nint error);
    [DllImport("/usr/lib/libobjc.dylib")] private static extern nint objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.dylib")] private static extern nint sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.dylib")] private static extern nint objc_msgSend(nint receiver, nint selector);
    [DllImport("/usr/lib/libobjc.dylib")] private static extern nint objc_msgSend(nint receiver, nint selector, int value);
    [DllImport("/usr/lib/libobjc.dylib")] private static extern nint objc_msgSend(nint receiver, nint selector, nint value);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] private static extern NSEdgeInsets objc_msgSend_insets(nint receiver, nint selector);
    [DllImport("libc")] private static extern int getppid();
    [DllImport("libc", SetLastError = true)] private static extern int execv(string path, string?[] argv);
}
