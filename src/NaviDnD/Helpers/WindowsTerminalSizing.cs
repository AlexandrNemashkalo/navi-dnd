using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NaviDnD.Helpers;

// Shared by the GUI launcher, installer and game so all entry points use the same scale.
public static class WindowsTerminalSizing
{
    public const string SessionConfigVariable = "NAVIDND_TERMINAL_CONFIG";
    public static readonly int[] Presets = [10, 11, 13];
    public static int PresetIndex(int requested, double fit) =>
        Enumerable.Range(0, Presets.Length).MinBy(i => Math.Abs(Presets[i] - (requested > 0 ? requested : fit)));
    public static double FontSize(int requested, double fit) => Math.Min(Presets[PresetIndex(requested, fit)], fit);

    public static int CorrectedLineOffset(int offset, int cellWidth, int cellHeight) => offset + 2 * cellWidth - cellHeight;

    public static string LiveConfigText(string config, double size, int lineOffset = -1) =>
        "[general]\nimport = [" + JsonSerializer.Serialize(Path.GetFullPath(config)) + "]\nlive_config_reload = true\n\n[font]\nsize = "
        + size.ToString(CultureInfo.InvariantCulture) + "\noffset = { x = 0, y = " + lineOffset.ToString(CultureInfo.InvariantCulture) + " }\n";

    // Call only when actually starting a terminal; command builders remain side-effect free.
    public static void PrepareLiveConfig(System.Diagnostics.ProcessStartInfo start)
    {
        int at = start.ArgumentList.IndexOf("--config-file");
        if (at < 0) return;
        string source = start.ArgumentList[at + 1];
        int fontAt = start.ArgumentList.ToList().FindIndex(s => s.StartsWith("font.size=", StringComparison.Ordinal));
        if (fontAt < 1) return;
        double size = double.Parse(start.ArgumentList[fontAt][10..], CultureInfo.InvariantCulture);
        string path = Path.Combine(Path.GetTempPath(), "navidnd-terminal-" + Guid.NewGuid().ToString("N") + ".toml");
        File.WriteAllText(path, LiveConfigText(source, size));
        start.ArgumentList.RemoveAt(fontAt);
        start.ArgumentList.RemoveAt(fontAt - 1);
        start.ArgumentList[at + 1] = path;
        start.Environment[SessionConfigVariable] = path;
    }
    public static double Fit(int width, int height, double dpiScale)
    {
        // Leave room for the taskbar and frame; conservative Consolas cell bounds in pixels.
        double points = Math.Min(width * .98 / (150 * .75), height * .98 / (63 * 1.4)) / Math.Max(1, dpiScale);
        return Math.Clamp(Math.Floor(points * 2) / 2, .5, 30);
    }

    public static double ScreenFit()
    {
        if (!OperatingSystem.IsWindows()) return 10;
        nint previous = SetThreadDpiAwarenessContext(new nint(-4));
        try
        {
            var work = new Rect();
            if (!SystemParametersInfo(0x30, 0, ref work, 0)) return 10;
            return Fit(work.Right - work.Left, work.Bottom - work.Top, GetDpiForSystem() / 96.0);
        }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }

    public static string FontOverride(string directory)
    {
        double fit = ScreenFit();
        // Every new window selects the best preset for the current display, not a stale saved size.
        double size = FontSize(0, fit);
        return "font.size=" + size.ToString(CultureInfo.InvariantCulture);
    }

    public static int ReadSize(string directory)
    {
        try
        {
            string root = directory;
            if (!Directory.Exists(Path.Combine(root, "Prompts")))
                for (var parent = new DirectoryInfo(directory); parent != null; parent = parent.Parent)
                    if (parent.Exists && parent.GetFiles("*.csproj").Length > 0) { root = parent.FullName; break; }
            string path = Path.Combine(root, "Storage", "settings.json");
            if (!File.Exists(path)) return 0;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.TryGetProperty("FullscreenFontSize", out var value)
                && value.TryGetInt32(out int size) && size >= 0 ? size : 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return 0; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint parameter, ref Rect value, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
}
