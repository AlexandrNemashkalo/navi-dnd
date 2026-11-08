using System.Reflection;
using System.Text.RegularExpressions;
using NaviDnD.Data;
using NaviDnD.Display;
using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class TerminalLayoutTests
{
    [Theory]
    [InlineData("HRO", "HRO")]
    [InlineData("ОРК", "ОРК")]
    [InlineData("🐉X", "?X ")]
    [InlineData("中AB", "?AB")]
    [InlineData("e\u0301X", "e?X")]
    [InlineData("A\nB", "A?B")]
    public void MapSymbolsNeverChangeCellWidth(string source, string expected)
    {
        Assert.Equal(expected, MapSymbolText.Format(source));
        Assert.Equal(5, MapSymbolText.Initiative(source, true).Length);
        Assert.Equal(5, MapSymbolText.Initiative(source, false).Length);
    }

    [Fact]
    public void ChangingCurrentTurnKeepsEndTurnButtonAndRightBorderInPlace()
    {
        var storage = new Storage();
        storage.WorldState.Hero = new() { Symbol = "HRO", Hp = "10/10", Color = [150, 150, 150] };
        storage.WorldState.Combat = new() { Active = true, CurrentTurn = "HRO",
            Initiative = [new() { Symbol = "HRO" }, new() { Symbol = "ORC" }] };
        var legend = new LegendDisplay(storage, new DisplayConfig(), new AppConfig());
        var build = typeof(LegendDisplay).GetMethod("BuildCombatLines", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string Capture()
        {
            var lines = (List<Action>)build.Invoke(legend, [60])!;
            var original = Console.Out;
            using var output = new StringWriter();
            try { Console.SetOut(output); lines[0](); }
            finally { Console.SetOut(original); }
            return Regex.Replace(output.ToString(), "\u001b\\[[0-9;]*m", "");
        }
        string heroTurn = Capture();
        Assert.Equal(60 - LegendDisplay.EndTurnText.Length - 1, legend.EndTurnButton.x0);
        Assert.Equal(legend.EndTurnButton.x0, heroTurn.IndexOf("[F10]", StringComparison.Ordinal));
        storage.WorldState.Combat.CurrentTurn = "ORC";
        string enemyTurn = Capture();
        Assert.Contains("> HRO ORC ", heroTurn);
        Assert.Contains(" HRO > ORC", enemyTurn);
        Assert.Equal(59, heroTurn.Length);
        Assert.Equal((-1, -1), legend.EndTurnButton);
        Assert.Equal(heroTurn.IndexOf("> HRO", StringComparison.Ordinal), enemyTurn.IndexOf(" HRO ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1920, 1040, 1)]
    [InlineData(2560, 1400, 1)]
    [InlineData(2560, 1400, 1.5)]
    [InlineData(3840, 2120, 2)]
    [InlineData(1280, 680, 2)]
    public void AutoScaleFitsWorkAreaWithDpi(int width, int height, double dpi)
    {
        double font = WindowsTerminalSizing.Fit(width, height, dpi);
        Assert.True(150 * .75 * font * dpi <= width * .98);
        Assert.True(63 * 1.4 * font * dpi <= height * .98);
        if (width == 2560 && dpi == 1) Assert.True(font > 10);
    }

    [Fact]
    public void MacIgnoresSavedManualFontSize()
    {
        var method = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.MacApp")!
            .GetMethod("TerminalArguments", BindingFlags.Static | BindingFlags.NonPublic)!;
        var auto = (List<string>)method.Invoke(null, [new AppConfig(), Array.Empty<string>()])!;
        var manual = (List<string>)method.Invoke(null, [new AppConfig { FullscreenFontSize = 8 }, Array.Empty<string>()])!;
        Assert.Equal(auto, manual);
    }

    [Theory]
    [InlineData("{\"FullscreenFontSize\":8}", 8)]
    [InlineData("{\"FullscreenFontSize\":-1}", 0)]
    [InlineData("{\"FullscreenFontSize\":\"bad\"}", 0)]
    [InlineData("broken", 0)]
    public void LauncherReadsOnlyValidSizeWithoutChangingSettings(string json, int expected)
    {
        string directory = Path.Combine(Path.GetTempPath(), "NaviDnD size " + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "Storage"));
        Directory.CreateDirectory(Path.Combine(directory, "Prompts"));
        string file = Path.Combine(directory, "Storage", "settings.json");
        try
        {
            File.WriteAllText(file, json);
            Assert.Equal(expected, WindowsTerminalSizing.ReadSize(directory));
            Assert.Equal(json, File.ReadAllText(file));
        }
        finally { Directory.Delete(directory, true); }
    }
}
