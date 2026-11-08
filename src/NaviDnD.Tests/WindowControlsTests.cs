using NaviDnD.Helpers;
using NaviDnD.Data;
using System.Text.RegularExpressions;

namespace NaviDnD.Tests;

public class WindowControlsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void ButtonsAndHoverColorsFollowTheSameFadeAsText(double opacity)
    {
        if (!OperatingSystem.IsWindows()) return;
        var display = new DisplayConfig { MainForeground = [100, 150, 200], MainBackground = [10, 20, 30] };
        var original = Console.Out;
        using var writer = new StringWriter();
        string output = "";
        try
        {
            Console.SetOut(writer);
            ColorHelper.WithForegroundOpacity(display.MainBackground, opacity, () =>
            {
                writer.GetStringBuilder().Clear();
                ColorHelper.WriteColored("MENU", display.MainForeground, display.MainBackground);
                WindowControls.Draw(display, hovered: 2);
                output = writer.ToString();
            });
        }
        finally { Console.SetOut(original); }
        static string Sequence(List<int> color) => $"\x1b[38;2;{color[0]};{color[1]};{color[2]}m";
        string normal = Sequence(ColorHelper.MixWith(display.MainBackground, display.MainForeground, opacity));
        string hover = Sequence(ColorHelper.MixWith(display.MainBackground, [235, 110, 110], opacity));
        Assert.Contains(normal + "MENU", output);
        Assert.Contains(hover + WindowControls.Buttons.Substring(7, 3), output);
        if (opacity == 0)
            Assert.All(Regex.Matches(output, "\\x1b\\[38;2;\\d+;\\d+;\\d+m").Cast<Match>(), match => Assert.Equal(normal, match.Value));
    }

    [Fact]
    public void EachFontKeepsItsSelectedGlyphsAndButtonWidth()
    {
        string consolas = WindowControls.ButtonsForFont("Consolas");
        string dejaVu = WindowControls.ButtonsForFont("DejaVu Sans Mono");
        Assert.Equal("  ━ \u2800▢  ✕ ", consolas);
        Assert.Equal("  –  ▢  ✕ ", dejaVu);
        Assert.Equal(consolas.Length, dejaVu.Length);
    }

    [Fact]
    public void EachButtonHasThreeColumnsAtRightEdge()
    {
        const int left = 2, width = 100;
        int start = left + width - WindowControls.Buttons.Length;
        for (int button = 0; button < 3; button++)
            for (int padding = 0; padding < 3; padding++)
                Assert.Equal(button, WindowControls.HitTest(start + 1 + button * 3 + padding, 1, left, width));
        Assert.Equal(-1, WindowControls.HitTest(start, 1, left, width));
        Assert.Equal(-1, WindowControls.HitTest(left + width, 1, left, width));
    }

    [Fact]
    public void BodyAndSmallWindowsDoNotActivateControls()
    {
        Assert.Equal(-1, WindowControls.HitTest(95, 2, 2, 100));
        Assert.Equal(-1, WindowControls.HitTest(1, 1, 0, 5));
    }
}
