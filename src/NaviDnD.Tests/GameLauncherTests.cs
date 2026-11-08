using NaviDnD.Installer;
using NaviDnD.Helpers;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

namespace NaviDnD.Tests;

public class GameLauncherTests
{
    [Fact]
    public void ShortcutAndTaskbarUseGuiLauncherWithoutRecursingIntoIt()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NaviDnD GUI " + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "Terminal"));
        try
        {
            string launcher = Path.Combine(directory, "NaviDnD.Launcher.exe");
            File.WriteAllText(launcher, "");
            File.WriteAllText(Path.Combine(directory, "alacritty.exe"), "");
            File.WriteAllText(Path.Combine(directory, "Terminal", "alacritty-windows.toml"), "");
            var shortcut = GameLauncher.CreateStartInfo(directory);
            var taskbar = WindowsAlacritty.CreateTaskbarStartInfo(Path.Combine(directory, "NaviDnD.exe"), directory: directory);
            Assert.Equal(launcher, shortcut.FileName);
            Assert.Equal(launcher, taskbar.FileName);
            Assert.Empty(taskbar.ArgumentList);
            var terminal = GameLauncher.CreateTerminalStartInfo(directory);
            Assert.Equal(Path.Combine(directory, "alacritty.exe"), terminal.FileName);
            Assert.Contains(Path.Combine(directory, "NaviDnD.exe"), terminal.ArgumentList);
            Assert.DoesNotContain(launcher, terminal.ArgumentList);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void BuiltLauncherDoesNotAllocateAConsole()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = SourceRoot();
        string output = Path.GetRelativePath(Path.Combine(root, "NaviDnD.Tests", "bin"), AppContext.BaseDirectory);
        string launcher = Path.Combine(root, "NaviDnD", "bin", output, "NaviDnD.Launcher.exe");
        using var file = File.OpenRead(launcher);
        using var pe = new PEReader(file);
        Assert.Equal(Subsystem.WindowsGui, pe.PEHeaders.PEHeader!.Subsystem);
    }

    private static string SourceRoot([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, ".."));

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
                "--title", "NaviDnD", "-o", "window.position={x=-32000,y=-32000}", "-o", WindowsTerminalSizing.FontOverride(directory), "-e", Path.Combine(directory, "NaviDnD.exe"),
                "--navidnd-alacritty", "--navidnd-quiet-start" }, start.ArgumentList);
        }
        finally { Directory.Delete(directory, true); }
    }
}
