using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class WindowsAlacrittyTests
{
    [Theory]
    [InlineData(null, false, true)]
    [InlineData(null, true, false)]
    [InlineData("--alacritty", true, true)]
    [InlineData("--classic-console", false, false)]
    [InlineData("--navidnd-conhost", false, false)]
    [InlineData("--navidnd-alacritty", false, false)]
    public void DefaultLaunchKeepsExplicitClassicHostAndUiTests(string? flag, bool uiTest, bool expected)
        => Assert.Equal(expected, WindowsAlacritty.ShouldLaunch(flag == null ? [] : [flag], uiTest));

    [Fact]
    public void LaunchPreservesArgumentsAndRemovesOtherHostFlags()
    {
        var start = WindowsAlacritty.CreateStartInfo(@"C:\Terminal with spaces\alacritty.exe",
            @"C:\Game with spaces\NaviDnD.exe", @"C:\Game with spaces\terminal.toml",
            [WindowsAlacritty.LaunchFlag, ConsoleHostLauncher.RelaunchFlag, "literal & value", "русский"], @"C:\Game with spaces");
        Assert.False(start.UseShellExecute);
        Assert.Equal(@"C:\Game with spaces", start.WorkingDirectory);
        Assert.Equal(new[] { "--config-file", @"C:\Game with spaces\terminal.toml", "--title", WindowsAlacritty.Title,
            "-e", @"C:\Game with spaces\NaviDnD.exe", WindowsAlacritty.ChildFlag, "literal & value", "русский" }, start.ArgumentList);
    }

    [Fact]
    public void DotnetLaunchKeepsAssemblyBeforeGameFlags()
    {
        var start = WindowsAlacritty.CreateStartInfo("alacritty.exe", "dotnet.exe", "config.toml",
            [WindowsAlacritty.ChildFlag, "--example"], "work", "game.dll");
        Assert.Equal(new[] { "--config-file", "config.toml", "--title", WindowsAlacritty.Title,
            "-e", "dotnet.exe", "game.dll", WindowsAlacritty.ChildFlag, "--example" }, start.ArgumentList);
    }
}
