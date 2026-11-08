using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class ConsoleRegionBufferTests
{
    [Fact]
    public void WindowsLineEndingsCannotEraseTheLeftPanelOrBorder()
    {
        var region = new ConsoleRegionBuffer();
        Assert.Equal("\x1b[9;101Hdie  ", region.Update(100, 8, 5, ["die\r\n"], ""));
        Assert.Empty(region.Update(100, 8, 5, ["die"], ""));
    }

    [Fact]
    public void DiceAssetsAndAnimationFramesContainNoCarriageReturns()
    {
        Assert.DoesNotContain("\r", NaviDnD.Display.DiceArt.D20);
        Assert.DoesNotContain("\r", NaviDnD.Display.DiceArt.D20Pair);
        foreach (var row in NaviDnD.Display.DiceArt.GetD20Lines()
            .Concat(NaviDnD.Display.DiceArt.GetD20PairLines(5, 12))
            .Concat(NaviDnD.Display.DiceArt.GetNoisyD20Lines(20, .5f, new Random(1))))
            Assert.DoesNotContain("\r", row);
    }

    [Fact]
    public void UpdatesOnlyChangedRowsAndClearsShorterText()
    {
        var region = new ConsoleRegionBuffer();
        region.Update(2, 4, 5, ["abcde", "fixed"], "");
        Assert.Equal("", region.Update(2, 4, 5, ["abcde", "fixed"], ""));
        Assert.Equal("\x1b[5;3Hab   ", region.Update(2, 4, 5, ["ab", "fixed"], ""));
    }

    [Fact]
    public void NeighborChangesDoNotInvalidateTextButGeometryAndColorsDo()
    {
        var text = new ConsoleRegionBuffer();
        var picture = new ConsoleRegionBuffer();
        text.Update(0, 0, 3, ["abc"], "");
        picture.Update(4, 0, 3, ["one"], "");
        picture.Update(4, 0, 3, ["two"], "");
        Assert.Empty(text.Update(0, 0, 3, ["abc"], ""));
        Assert.NotEmpty(text.Update(0, 1, 3, ["abc"], ""));
        Assert.Equal("\x1b[2;1H\x1b[31mabc", text.Update(0, 1, 3, ["\x1b[31mabc"], ""));
        text.Invalidate();
        Assert.NotEmpty(text.Update(0, 1, 3, ["\x1b[31mabc"], ""));
    }
}
