using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NaviDnD.Helpers;

// NaviDnD.app на macOS. Finder запускает приложение без терминала — тогда игра заменяет себя своим терминалом
// (Alacritty из Contents/MacOS, настройки — Contents/Resources/alacritty.toml): значок NaviDnD в Dock, окно без кнопок
// macOS ровно под сетку игры или весь экран, размер шрифта — из настроек игры. В нём игра запускается снова — с TerminalFlag.
// Окно терминала после запуска не изменить — смена режима и шрифта в настройках перезапускает его (Relaunch).
[SupportedOSPlatform("macos")]
internal static class MacApp
{
    internal const string TerminalFlag = "--navidnd-terminal";
    private const string SettingsFlag = "--navidnd-settings=";
    // release-version.txt от корня приложения: патчи обновляют NaviDnD.app целиком, вместе с подписью.
    internal const string VersionFile = "Contents/Resources/" + Installer.Patch.DefaultVersionFile;
    private const int Columns = 150, Rows = 63;

    // Шрифт — Menlo (есть на любом Mac): с ним сетка игры ровная. Ширина клетки и высота строки — в долях кегля.
    private const string Font = "Menlo";
    private const double CellWidth = 0.602, CellHeight = 1.164;
    private const string TopFlag = "--navidnd-top=";

    internal static string BundlePath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));

    // Игра идёт в своём терминале: кнопки окна рисует она сама (свернуть — скрыть приложение, закрыть — выход).
    internal static bool InOwnTerminal { get; private set; }

    // Пункт настроек, на который вернуться после перезапуска окна; null — обычный запуск. Берётся один раз.
    private static int? SettingsFocus { get; set; }

    internal static int? TakeSettingsFocus()
    {
        int? focus = SettingsFocus;
        SettingsFocus = null;
        return focus;
    }

    // true — этот процесс своё сделал: терминал запущен вместо него или приложение открыто заново с настоящего места.
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
        if (!Console.IsInputRedirected) return false;   // запущено из терминала вручную — играем в нём
        string bundle = BundlePath;
        // Скачанное приложение, открытое прямо из «Загрузок», macOS запускает из временной копии только для чтения
        // (App Translocation) — обновления туда не записать. Снимаем карантин с настоящего приложения и открываем его.
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
        return false;   // терминал не запустился — играем без него (окна не будет видно, но и падать незачем)
    }

    // Новое окно с текущими настройками и возврат в настройки на тот же пункт; это окно закрывается вместе с игрой.
    internal static void Relaunch(AppConfig config, int settingsItem)
    {
        var arguments = TerminalArguments(config, [SettingsFlag + settingsItem]);
        var start = new System.Diagnostics.ProcessStartInfo(arguments[0])
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in arguments.Skip(1)) start.ArgumentList.Add(argument);
        using var terminal = System.Diagnostics.Process.Start(start);
        if (terminal == null) return;
        terminal.StandardInput.Close();
        Environment.Exit(0);
    }

    // Размеры шрифта для настроек: авто и те, при которых игра помещается на экран в выбранном режиме.
    internal static int[] FontSizes(AppConfig config)
    {
        double fit = Fit(config.Fullscreen, Screen());
        return [0, .. new[] { 10, 11, 12, 13, 14, 15, 16, 18, 20, 22 }.Where(size => size <= fit)];
    }

    // Самый крупный кегль, при котором 150 × 63 клеток помещаются: весь экран — кроме выреза сверху (MacBook с
    // «чёлкой»), окно — под полосой меню и с отступом.
    private static double Fit(bool fullscreen, (double Width, double Height, double Scale, double Notch) screen)
    {
        double width = screen.Width - (fullscreen ? 0 : 16), height = screen.Height - (fullscreen ? screen.Notch : 48);
        for (double size = 30; size >= 8; size -= 0.5)
            if (Columns * Cell(size, CellWidth, screen.Scale) <= width && Rows * Cell(size, CellHeight, screen.Scale) <= height) return size;
        return 8;
    }

    // Клетка Alacritty — в целых физических пикселях.
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
            // Терминал — на весь экран, фон вокруг игры — её фон (как «весь экран» на Windows). Игра сама встаёт по
            // центру сетки (UnixTerminal), строки под вырезом пропускает; остаток пикселей Alacritty делит поровну.
            arguments.AddRange(["-o", "window.startup_mode=\"SimpleFullscreen\"", "-o", "window.dynamic_padding=true"]);
            if (screen.Notch > 0) game.Add(TopFlag + (int)Math.Ceiling(screen.Notch / cellHeight));
        }
        else
        {
            // Окно — по центру экрана под полосой меню; позиция для Alacritty — в физических пикселях.
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

    // Размер главного экрана в точках, их масштаб (Retina — 2) и высота выреза сверху (безопасная зона экрана).
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

    // Путь настоящего приложения для временной копии (Security.framework).
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

    // Терминал игры — родительский процесс; скрыть или проверить его можно без особых разрешений (NSRunningApplication).
    internal static void Hide() => SendToTerminal("hide");

    internal static bool IsHidden() => (SendToTerminal("isHidden") & 0xFF) != 0;   // BOOL — младший байт

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
