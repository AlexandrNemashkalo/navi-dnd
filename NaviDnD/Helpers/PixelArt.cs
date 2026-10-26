using System.Runtime.CompilerServices;
using SkiaSharp;

namespace NaviDnD.Helpers;

// Small color sprites (Dungeon Crawl tiles 32×32) - in color with half blocks: a character is two pixels one
// above the other, '▀' - the top one in the character color, the bottom one in the background color. A
// character is twice as tall as wide, so the pixels are square, and a 32×32 tile fits into the same 36×18
// block as a braille picture, without losing detail or color.
//
// The lines stay ordinary strings (the panels copy, align and cut them as before), the colors of the cells are
// attached to the line instance itself: whoever draws a picture line asks Colors(line).
public static class PixelArt
{
    // Fg - the color of the character, Bg - the background; null - the background of the panel (transparent pixel).
    public readonly record struct Cell(List<int>? Fg, List<int>? Bg);

    private static readonly ConditionalWeakTable<string, Cell[]> LineColors = new();

    // Not larger than 64×64 - pixel art; photos and large pictures remain braille silhouettes.
    public static bool IsSprite(SKBitmap src) => src.Width <= 64 && src.Height <= 64;

    public static Cell[]? Colors(string? line) => line != null && LineColors.TryGetValue(line, out var cells) ? cells : null;

    // widthChars × widthChars pixels → widthChars characters × widthChars/2 lines. Enlarges by a whole number
    // of times without blurring (32 → 36: 1×, margins of 2 pixels), reduces with averaging (card 16×8).
    public static string[] Render(SKBitmap src, int widthChars)
    {
        int size = widthChars, rows = widthChars / 2;
        using var bmp = new SKBitmap(new SKImageInfo(size, rows * 2, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(bmp))
        using (var image = SKImage.FromBitmap(src))
        {
            canvas.Clear(SKColors.Transparent);
            double scale = Math.Min(size / (double)src.Width, rows * 2 / (double)src.Height);
            bool enlarge = scale >= 1;
            if (enlarge) scale = Math.Floor(scale);
            int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
            var dest = SKRect.Create((size - w) / 2, (rows * 2 - h) / 2, w, h);
            canvas.DrawImage(image, dest, enlarge
                ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
                : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        var palette = new Dictionary<uint, List<int>>();
        List<int>? Pixel(int x, int y)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Alpha < 128) return null;
            uint key = (uint)(c.Red << 16 | c.Green << 8 | c.Blue);
            if (!palette.TryGetValue(key, out var rgb)) palette[key] = rgb = [c.Red, c.Green, c.Blue];
            return rgb;
        }

        var lines = new string[rows];
        for (int r = 0; r < rows; r++)
        {
            var chars = new char[size];
            var cells = new Cell[size];
            for (int x = 0; x < size; x++)
            {
                var top = Pixel(x, r * 2);
                var bottom = Pixel(x, r * 2 + 1);
                (chars[x], cells[x]) = (top, bottom) switch
                {
                    (null, null) => (' ', default),
                    (_, null) => ('▀', new Cell(top, null)),
                    (null, _) => ('▄', new Cell(bottom, null)),
                    _ => ('▀', new Cell(top, bottom)),
                };
            }
            lines[r] = new string(chars);
            LineColors.AddOrUpdate(lines[r], cells);
        }
        return lines;
    }

    // Draws a picture line from the cursor: in its colors, if it is a sprite, otherwise false (the caller draws
    // it as usual). Transparent cells - the background of the panel.
    public static bool Write(string line, List<int>? panelBg)
    {
        if (Colors(line) is not { } cells) return false;
        int i = 0;
        while (i < line.Length)
        {
            var cell = i < cells.Length ? cells[i] : default;
            int start = i;
            while (i < line.Length && (i < cells.Length ? cells[i] : default) == cell) i++;
            ColorHelper.WriteColored(line[start..i], cell.Fg ?? panelBg, cell.Bg ?? panelBg);
        }
        return true;
    }
}
