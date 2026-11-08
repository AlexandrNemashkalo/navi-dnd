using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class AnsiColorRunsTests
{
    private const string Red = "\x1b[38;2;220;50;50m";
    private const string White = "\x1b[38;2;240;240;240m";
    private const string Black = "\x1b[48;2;0;0;0m";

    [Fact]
    public void KeepsVisibleColorsAndFinalStateWithoutPerCellRestores()
    {
        string cell = Red + Black + "#" + White;
        string frame = string.Concat(Enumerable.Repeat(cell, 200));
        string compact = AnsiColorRuns.Compact(frame);
        Assert.Equal(Red + Black + new string('#', 200) + White, compact);
        Assert.True(compact.Length < frame.Length / 10);
    }

    [Fact]
    public void PreservesResetsCursorMovesAndRealColorChanges()
    {
        string frame = Red + "a" + White + "b\x1b[0m" + White + "c\x1b[4;5H" + Red + "d";
        Assert.Equal(frame, AnsiColorRuns.Compact(frame));
    }
}
