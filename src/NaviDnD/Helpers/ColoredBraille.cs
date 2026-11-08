using SkiaSharp;

namespace NaviDnD.Helpers;

// Binary geometry and RGB are independent: only the mask creates dots. A terminal
// cell has one foreground color, so average only the original colors of its lit dots.
public static class ColoredBraille
{
    public static string[] Render(SKBitmap mask, SKBitmap? colors, int widthChars, SKBitmap? background = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(widthChars, 2);
        if (colors != null && (colors.Width != mask.Width || colors.Height != mask.Height))
            throw new ArgumentException("Line colors must match the mask dimensions.", nameof(colors));
        if (background != null && (background.Width != mask.Width || background.Height != mask.Height))
            throw new ArgumentException("Background must match the mask dimensions.", nameof(background));

        int width = widthChars * 2, height = widthChars / 2 * 4;
        double scale = Math.Min(width / (double)mask.Width, height / (double)mask.Height);
        // Keep native dots when enlarging: 64x64 fits in the 72x72 picture panel with margins.
        if (scale >= 1) scale = Math.Floor(scale);
        int w = Math.Max(1, (int)(mask.Width * scale)), h = Math.Max(1, (int)(mask.Height * scale));
        int left = (width - w) / 2, top = (height - h) / 2;
        var lines = new string[height / 4];
        var palette = new Dictionary<int, List<int>>();
        List<int> Rgb(int r, int g, int b)
        {
            int key = r << 16 | g << 8 | b;
            if (!palette.TryGetValue(key, out var rgb)) palette[key] = rgb = [r, g, b];
            return rgb;
        }
        for (int row = 0; row < lines.Length; row++)
        {
            var chars = new char[widthChars];
            var cells = new PixelArt.Cell[widthChars];
            for (int col = 0; col < widthChars; col++)
            {
                int code = 0, count = 0, red = 0, green = 0, blue = 0;
                int bgAlpha = 0, bgRed = 0, bgGreen = 0, bgBlue = 0;
                for (int py = 0; py < 4; py++)
                    for (int px = 0; px < 2; px++)
                    {
                        int canvasX = col * 2 + px, canvasY = row * 4 + py;
                        if (background != null)
                        {
                            // The backdrop fills the entire picture area in both axes.
                            // Keep the symbol's native scale and centering independent.
                            int bx = Math.Min(background.Width - 1, (int)((canvasX + 0.5) * background.Width / width));
                            int by = Math.Min(background.Height - 1, (int)((canvasY + 0.5) * background.Height / height));
                            var bg = background.GetPixel(bx, by);
                            bgAlpha += bg.Alpha;
                            bgRed += bg.Red * bg.Alpha; bgGreen += bg.Green * bg.Alpha; bgBlue += bg.Blue * bg.Alpha;
                        }
                        int x = canvasX - left, y = canvasY - top;
                        if (x < 0 || x >= w || y < 0 || y >= h) continue;
                        int sx = Math.Min(mask.Width - 1, (int)((x + 0.5) * mask.Width / w));
                        int sy = Math.Min(mask.Height - 1, (int)((y + 0.5) * mask.Height / h));
                        var dot = mask.GetPixel(sx, sy);
                        if (dot.Alpha < 128 || 0.22 * dot.Red + 0.72 * dot.Green + 0.06 * dot.Blue >= 128) continue;
                        int bit = px == 0 ? py switch { 0 => 0, 1 => 1, 2 => 2, _ => 6 }
                                          : py switch { 0 => 3, 1 => 4, 2 => 5, _ => 7 };
                        code |= 1 << bit;
                        if (colors?.GetPixel(sx, sy) is { Alpha: >= 128 } color)
                        {
                            red += color.Red; green += color.Green; blue += color.Blue; count++;
                        }
                    }
                chars[col] = (char)(0x2800 + code);
                var fg = count > 0 ? Rgb(red / count, green / count, blue / count)
                    : code != 0 && (colors != null || background != null) ? Rgb(230, 230, 230) : null;
                var cellBg = bgAlpha > 0 ? Rgb(bgRed / bgAlpha, bgGreen / bgAlpha, bgBlue / bgAlpha) : null;
                // Include empty dots and outside margins in coverage: each cell is eight samples.
                cells[col] = new(fg, cellBg, (byte)(bgAlpha / 8));
            }
            lines[row] = new string(chars);
            if (colors != null || background != null) PixelArt.AttachColors(lines[row], cells);
        }
        return lines;
    }
}
