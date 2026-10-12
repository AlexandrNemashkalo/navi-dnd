using NaviDnD.Helpers;

namespace NaviDnD.Tests;

// Icons render the same way on every OS (SkiaSharp): a real game icon gives a non-empty braille picture.
public class SvgToBrailleConverterTests
{
    [Fact]
    public void GameIconRendersToBraille()
    {
        var lines = SvgToBrailleConverter.Convert("lorc/acorn", "", 18);
        Assert.NotNull(lines);
        Assert.Equal(9, lines!.Length);
        Assert.All(lines, l => Assert.Equal(18, l.Length));
        Assert.Contains(lines, l => l.Any(c => c > '⠀'));
    }
}
