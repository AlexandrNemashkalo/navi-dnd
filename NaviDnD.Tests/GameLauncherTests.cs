using NaviDnD.Installer;

namespace NaviDnD.Tests;

public class GameLauncherTests
{
    [Fact]
    public void BundledTerminalStartsGameDirectlyWithoutIntermediateConsole()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NaviDnD launch " + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "Terminal"));
        try
        {
            var legacy = GameLauncher.CreateStartInfo(directory);
            Assert.Equal("conhost.exe", Path.GetFileName(legacy.FileName));
            File.WriteAllText(Path.Combine(directory, "alacritty.exe"), "");
            File.WriteAllText(Path.Combine(directory, "Terminal", "alacritty-windows.toml"), "");
            var start = GameLauncher.CreateStartInfo(directory);
            Assert.Equal(Path.Combine(directory, "alacritty.exe"), start.FileName);
            Assert.False(start.UseShellExecute);
            Assert.Equal(directory, start.WorkingDirectory);
            Assert.Equal(new[] { "--config-file", Path.Combine(directory, "Terminal", "alacritty-windows.toml"),
                "--title", "NaviDnD — Alacritty", "-o", "window.position={x=-32000,y=-32000}", "-e", Path.Combine(directory, "NaviDnD.exe"),
                "--navidnd-alacritty", "--navidnd-quiet-start" }, start.ArgumentList);
        }
        finally { Directory.Delete(directory, true); }
    }
}
