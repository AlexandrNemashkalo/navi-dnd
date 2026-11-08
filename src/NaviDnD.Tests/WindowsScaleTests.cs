using System.Diagnostics;
using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class WindowsScaleTests
{
    [Theory]
    [InlineData(0, 0, 1920, 1040, 1200, 1008, 360, 16)]
    [InlineData(0, 40, 1920, 1080, 1200, 1008, 360, 56)]
    [InlineData(60, 0, 1920, 1080, 1200, 1008, 390, 36)]
    public void InitialWindowFitsWorkAreaWithTaskbarOnDifferentEdges(int left, int top, int right, int bottom,
        int width, int height, int expectedX, int expectedY)
    {
        var position = WindowsAlacritty.CenterInWorkArea(left, top, right, bottom, width, height);
        Assert.Equal((expectedX, expectedY), position);
        Assert.True(position.x + width <= right);
        Assert.True(position.y + height <= bottom);
    }

    [Theory]
    [InlineData(1440, 860, 1, 0)]
    [InlineData(1920, 1040, 1, 1)]
    [InlineData(2560, 1400, 1, 2)]
    [InlineData(3840, 2120, 2, 1)]
    public void StartupSelectsPresetForResolutionAndDpi(int width, int height, double dpi, int expected)
    {
        Assert.Equal(expected, WindowsTerminalSizing.PresetIndex(0, WindowsTerminalSizing.Fit(width, height, dpi)));
    }

    [Theory]
    [InlineData(5, 11, -2)]
    [InlineData(7, 14, -1)]
    [InlineData(9, 19, -2)]
    [InlineData(14, 29, -2)]
    public void LineOffsetMakesArtworkPixelsSquare(int width, int height, int expected)
    {
        int offset = WindowsTerminalSizing.CorrectedLineOffset(-1, width, height);
        Assert.Equal(expected, offset);
        Assert.Equal(2 * width, height + offset + 1);
        Assert.Contains($"y = {offset}", WindowsTerminalSizing.LiveConfigText("config.toml", 13, offset));
    }

    [Fact]
    public void StartupIgnoresPreviouslySavedSize()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NaviDnD auto " + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "Storage"));
        try
        {
            string settings = Path.Combine(directory, "Storage", "settings.json");
            File.WriteAllText(settings, "{\"FullscreenFontSize\":8}");
            string compact = WindowsTerminalSizing.FontOverride(directory);
            File.WriteAllText(settings, "{\"FullscreenFontSize\":13}");
            Assert.Equal(compact, WindowsTerminalSizing.FontOverride(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void ExactlyThreePresetsAndOldSizesResolveToOneOfThem()
    {
        Assert.Equal(new[] { 10, 11, 13 }, WindowsTerminalSizing.Presets);
        Assert.Equal(0, WindowsTerminalSizing.PresetIndex(6, 13));
        Assert.Equal(1, WindowsTerminalSizing.PresetIndex(0, 11));
        Assert.Equal(2, WindowsTerminalSizing.PresetIndex(0, 13));
        Assert.Equal(2, WindowsTerminalSizing.PresetIndex(30, 13));
        Assert.Equal(9.5, WindowsTerminalSizing.FontSize(13, 9.5));
    }

    [Fact]
    public void LiveConfigurationCanChangeFontWithoutCommandLineOverridingIt()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NaviDnD scale " + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string config = Path.Combine(directory, "alacritty.toml");
        var start = new ProcessStartInfo("alacritty.exe");
        foreach (string arg in new[] { "--config-file", config, "-o", WindowsAlacritty.QuietPosition,
            "-o", "font.size=10", "-e", "game.exe", "--test" }) start.ArgumentList.Add(arg);
        string? session = null;
        try
        {
            File.WriteAllText(config, "[font]\nsize = 10\n");
            WindowsTerminalSizing.PrepareLiveConfig(start);
            session = start.Environment[WindowsTerminalSizing.SessionConfigVariable];
            Assert.NotNull(session);
            Assert.Equal(session, start.ArgumentList[1]);
            Assert.DoesNotContain(start.ArgumentList, s => s.StartsWith("font.size="));
            Assert.Contains(WindowsAlacritty.QuietPosition, start.ArgumentList);
            Assert.Equal(new[] { "-e", "game.exe", "--test" }, start.ArgumentList.TakeLast(3));
            Assert.Contains("live_config_reload = true", File.ReadAllText(session!));
            File.WriteAllText(session!, WindowsTerminalSizing.LiveConfigText(config, 13));
            Assert.Contains("size = 13", File.ReadAllText(session!));
            Assert.Equal("[font]\nsize = 10\n", File.ReadAllText(config));
        }
        finally
        {
            if (session != null) File.Delete(session);
            Directory.Delete(directory, true);
        }
    }
}
