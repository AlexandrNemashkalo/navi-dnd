using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class FormRowCacheTests
{
    [Fact]
    public void UnchangedColorsDoNotRedrawButTypingAndColorChangesDo()
    {
        var cache = new FormRowCache();
        char[,] text = { { 'A', ' ' } };
        var fg = new List<int>?[1, 2]; var bg = new List<int>?[1, 2];
        bool Changed() => cache.Changed(0, 0, 2, text, fg, bg, [200, 180, 160], [18, 18, 22]);
        Assert.True(Changed());
        Assert.False(Changed());
        fg[0, 0] = [200, 180, 160];
        Assert.False(Changed());
        text[0, 1] = 'Б';
        Assert.True(Changed());
        Assert.False(Changed());
        fg[0, 0]![0] = 220;
        Assert.True(Changed());
        bg[0, 1] = [42, 42, 42];
        Assert.True(Changed());
        text[0, 1] = ' ';
        Assert.True(Changed());
        cache.Invalidate();
        Assert.True(Changed());
    }

    [Fact]
    public void MapGapAndSeparateRowsDoNotInvalidateUnchangedText()
    {
        var cache = new FormRowCache();
        char[,] text = { { 'L', 'M', 'R' }, { 'N', ' ', ' ' } };
        var fg = new List<int>?[2, 3]; var bg = new List<int>?[2, 3];
        bool Changed(int row, int from, int to) => cache.Changed(row, from, to, text, fg, bg, [200, 180, 160], [18, 18, 22]);
        Assert.True(Changed(0, 0, 1)); Assert.True(Changed(0, 2, 3)); Assert.True(Changed(1, 0, 3));
        text[0, 1] = 'X';
        Assert.False(Changed(0, 0, 1)); Assert.False(Changed(0, 2, 3)); Assert.False(Changed(1, 0, 3));
        Assert.True(Changed(0, 0, 3));
    }
}
