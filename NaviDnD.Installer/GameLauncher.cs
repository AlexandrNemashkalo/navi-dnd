using System.Diagnostics;

namespace NaviDnD.Installer;

// Launch the terminal directly: a console-subsystem game executable would create
// a temporary console before it can hand off to Alacritty.
internal static class GameLauncher
{
    internal static ProcessStartInfo CreateStartInfo(string destination)
    {
        string executable = Path.Combine(destination, "NaviDnD.exe");
        string terminal = Path.Combine(destination, "alacritty.exe");
        string config = Path.Combine(destination, "Terminal", "alacritty-windows.toml");
        bool bundled = File.Exists(terminal) && File.Exists(config);
        var start = new ProcessStartInfo(bundled ? terminal : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe"))
        {
            UseShellExecute = false,
            WorkingDirectory = destination,
        };
        if (bundled)
            foreach (string argument in new[] { "--config-file", config, "--title", "NaviDnD — Alacritty",
                "-o", "window.position={x=-32000,y=-32000}", "-e" })
                start.ArgumentList.Add(argument);
        start.ArgumentList.Add(executable);
        if (bundled)
        {
            start.ArgumentList.Add("--navidnd-alacritty");
            start.ArgumentList.Add("--navidnd-quiet-start");
        }
        return start;
    }
}
