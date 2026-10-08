using Svg;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace NaviDnD.Helpers;

public static class SvgToBrailleConverter
{
    private static readonly string IconsBasePath =
        Path.Combine(AppConfig.AssetDirectory("icons"), "000000", "ffffff", "1x1");

    // Cache: imagePath → braille lines
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> _cache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task>> _preparing = new();
    private static readonly SemaphoreSlim PrepareGate = new(1, 1);

    // Decode first-use pictures off the UI loop. Completed pictures are applied only by UI polling.
    public static Task PrepareAsync(string? imagePath, int widthChars = 36)
    {
        if (string.IsNullOrEmpty(imagePath) || _cache.ContainsKey(imagePath + "|" + widthChars))
            return Task.CompletedTask;
        return _preparing.GetOrAdd(imagePath + "|" + widthChars, _ => new Lazy<Task>(() => Task.Run(async () =>
        {
            await PrepareGate.WaitAsync();
            try { Convert(imagePath, "", widthChars); }
            catch { /* A missing/broken picture keeps the existing fallback. */ }
            finally { PrepareGate.Release(); }
        }))).Value;
    }

    /// <summary>
    /// Converts an SVG icon to Braille art lines.
    /// widthChars: output width in terminal characters (each char = 2 braille pixels).
    /// Returns null if the file is not found or rendering fails.
    /// </summary>
    public static string[]? Convert(string imagePath, string name, int widthChars = 36)
    {
        string cacheKey = imagePath + "|" + widthChars;
        if (_cache.TryGetValue(cacheKey, out var cached)) return AppendName(cached, name, widthChars);

        string fullPath = ResolveFullPath(imagePath);
        if (!File.Exists(fullPath)) return null;

        if (!fullPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var raster = ConvertRaster(fullPath, widthChars);
            if (raster == null) return null;
            _cache[cacheKey] = raster;
            return AppendName(raster, name, widthChars);
        }

        try
        {
            var svgDoc = SvgDocument.Open(fullPath);

            // Each braille char covers 2×4 pixels.
            // For a square icon: pixelW = widthChars*2, pixelH = pixelW/2*4 = pixelW*2
            // But terminal chars are typically ~2× taller than wide, so for a square display:
            //   actual width  = widthChars chars × 8px/char = widthChars×8 px
            //   actual height = (pixelH/4) chars × 16px/char = (pixelH/4)×16 px
            // For square: widthChars×8 = (pixelH/4)×16 → pixelH = widthChars×2 px
            // And pixelW = widthChars×2, so image is square in pixels → square in display. ✓
            int pixelW = widthChars * 2;
            int pixelH = pixelW; // square image in pixels

            using Bitmap bmp = svgDoc.Draw(pixelW, pixelH);
            if (bmp == null) return null;

            int charsW = pixelW / 2;
            int charsH = pixelH / 4;

            var lines = new List<string>(charsH + 2);
            var bmpData = bmp.LockBits(
                new Rectangle(0, 0, pixelW, pixelH),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                IntPtr ptr = bmpData.Scan0;
                int stride = bmpData.Stride;

                for (int cy = 0; cy < charsH; cy++)
                {
                    var sb = new StringBuilder(charsW);
                    for (int cx = 0; cx < charsW; cx++)
                    {
                        int code = 0;
                        for (int py = 0; py < 4; py++)
                        {
                            for (int px = 0; px < 2; px++)
                            {
                                int x = cx * 2 + px;
                                int y = cy * 4 + py;
                                int byteOff = y * stride + x * 4;

                                // Format32bppArgb in memory: B G R A
                                byte b = Marshal.ReadByte(ptr, byteOff + 0);
                                byte g = Marshal.ReadByte(ptr, byteOff + 1);
                                byte r = Marshal.ReadByte(ptr, byteOff + 2);
                                byte a = Marshal.ReadByte(ptr, byteOff + 3);

                                // Luminance (perceptual, from the JS source)
                                float grey = 0.22f * r + 0.72f * g + 0.06f * b;

                                // Dark pixels (black symbol on white background) become dots
                                bool lit = a >= 128 && grey < 128f;
                                if (lit)
                                    code |= 1 << BrailleBit(px, py);
                            }
                        }
                        sb.Append((char)(0x2800 + code));
                    }
                    lines.Add(sb.ToString());
                }
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            _cache[cacheKey] = lines.ToArray();
            return AppendName(_cache[cacheKey], name, widthChars);
        }
        catch
        {
            return null;
        }
    }

    private static string[] AppendName(string[] brailleLines, string name, int widthChars)
    {
        if (string.IsNullOrEmpty(name)) return brailleLines;
        // Подпись может быть в несколько строк через '\n' (название + свойства).
        var parts = name.Split('\n');
        var result = new string[brailleLines.Length + 1 + parts.Length];
        brailleLines.CopyTo(result, 0);
        result[brailleLines.Length] = "";
        for (int i = 0; i < parts.Length; i++)
            result[brailleLines.Length + 1 + i] = parts[i].Length > widthChars ? parts[i][..widthChars] : parts[i];
        return result;
    }

    // Свои картинки игрока (анкета новой игры) лежат в Storage/Portraits и хранятся как
    // "portraits/<файл>" — PNG/JPG/BMP/GIF или SVG. Остальное — слаг game-icons ("author/name").
    public const string PortraitsPrefix = "portraits/";
    public static string PortraitsDir => Path.Combine(AppConfig.ProjectRoot, "Storage", "Portraits");

    public static bool IsSupportedImageFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".svg" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

    // Растровая картинка → браиль: вписываем в квадрат с белым фоном, точки — где темнее среднего
    // (порог по средней яркости — фото и рисунки с любым фоном дают читаемый силуэт).
    private static string[]? ConvertRaster(string fullPath, int widthChars)
    {
        try
        {
            using var src = new Bitmap(fullPath);
            int pixelW = widthChars * 2, pixelH = pixelW;
            using var bmp = new Bitmap(pixelW, pixelH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                double scale = Math.Min(pixelW / (double)src.Width, pixelH / (double)src.Height);
                int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
                g.DrawImage(src, (pixelW - w) / 2, (pixelH - h) / 2, w, h);
            }

            var grey = new float[pixelW, pixelH];
            double sum = 0;
            for (int y = 0; y < pixelH; y++)
                for (int x = 0; x < pixelW; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    grey[x, y] = 0.22f * c.R + 0.72f * c.G + 0.06f * c.B;
                    sum += grey[x, y];
                }
            float threshold = (float)(sum / (pixelW * pixelH));

            var lines = new List<string>(pixelH / 4);
            for (int cy = 0; cy < pixelH / 4; cy++)
            {
                var sb = new StringBuilder(pixelW / 2);
                for (int cx = 0; cx < pixelW / 2; cx++)
                {
                    int code = 0;
                    for (int py = 0; py < 4; py++)
                        for (int px = 0; px < 2; px++)
                            if (grey[cx * 2 + px, cy * 4 + py] < threshold)
                                code |= 1 << BrailleBit(px, py);
                    sb.Append((char)(0x2800 + code));
                }
                lines.Add(sb.ToString());
            }
            return lines.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveFullPath(string imagePath)
    {
        if (imagePath.StartsWith(PortraitsPrefix, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(PortraitsDir, imagePath[PortraitsPrefix.Length..]);
        if (Path.IsPathRooted(imagePath)) return imagePath;

        string normalized = imagePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        if (!normalized.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            normalized += ".svg";
        return Path.Combine(IconsBasePath, normalized);
    }

    // Maps a pixel position (px ∈ {0,1}, py ∈ {0..3}) within a 2×4 braille cell
    // to the corresponding bit index in the Unicode Braille codepoint offset (U+2800+).
    //
    // Braille dot layout:       Unicode bit positions:
    //   dot1  dot4               bit0  bit3
    //   dot2  dot5               bit1  bit4
    //   dot3  dot6               bit2  bit5
    //   dot7  dot8               bit6  bit7
    //
    // Left column (px=0): py0→bit0, py1→bit1, py2→bit2, py3→bit6
    // Right column (px=1): py0→bit3, py1→bit4, py2→bit5, py3→bit7
    private static int BrailleBit(int px, int py) => (px == 0)
        ? py switch { 0 => 0, 1 => 1, 2 => 2, _ => 6 }
        : py switch { 0 => 3, 1 => 4, 2 => 5, _ => 7 };
}
