using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class WindowControlsTests
{
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
