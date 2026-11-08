using NaviDnD.Data.Models;
using SkiaSharp;
using System.Text;

namespace NaviDnD.Display;

public readonly record struct SceneFire(float X, float Y, float Radius, float Height);

public sealed record AdventureScene(string Key, string Atlas, int Tile, int Variant, string Light, string Title)
{
    // Authored positions of actual flames in each atlas tile, normalized before viewport cropping.
    // Windows, sunsets, lanterns and warm-colored objects must not turn into light sources.
    private static readonly Dictionary<(string, int), SceneFire[]> FireLayouts = new()
    {
        [("camp", 0)] = [new(.50f, .85f, .30f, .16f)],
        [("camp", 1)] = [new(.49f, .82f, .30f, .20f)],
        [("dungeon", 0)] = [new(.22f, .37f, .25f, .09f), new(.665f, .40f, .13f, .045f)],
        [("dungeon", 1)] = [new(.274f, .37f, .27f, .10f)],
        [("tavern", 0)] = [new(.318f, .653f, .26f, .14f)],
        [("tavern", 1)] = [new(.865f, .65f, .25f, .15f)],
        [("house", 1)] = [new(.845f, .65f, .25f, .12f)],
        [("temple", 0)] = [new(.505f, .53f, .24f, .10f)],
        [("tower", 0)] = [new(.097f, .46f, .24f, .09f)],
    };
    public static readonly string[] NatureKeys =
        ["ocean", "sea", "lake", "beach", "plains", "meadow", "forest", "deep-forest", "hills", "mountain", "peaks", "swamp", "desert", "tundra", "snow", "taiga"];
    public static readonly string[] PlaceKeys =
        ["capital", "city", "town", "village", "castle", "port", "ruins", "dungeon", "cave", "shrine", "tavern", "house", "shop", "temple", "tower", "camp"];
    private static readonly char[] Biomes =
        [WorldBiomes.Ocean, WorldBiomes.Sea, WorldBiomes.Lake, WorldBiomes.Beach, WorldBiomes.Plains, WorldBiomes.Grass,
         WorldBiomes.Forest, WorldBiomes.DeepForest, WorldBiomes.Hills, WorldBiomes.Mountains, WorldBiomes.Peaks,
         WorldBiomes.Swamp, WorldBiomes.Desert, WorldBiomes.Tundra, WorldBiomes.Snow, WorldBiomes.SnowForest];

    public static int GalleryCount => (NatureKeys.Length + PlaceKeys.Length) * 2;
    public int GalleryIndex { get; init; } = -1;

    // Temporary gallery is presentation-only: it never changes the hero's location.
    public static AdventureScene Preview(int index, string light)
    {
        index = ((index % GalleryCount) + GalleryCount) % GalleryCount;
        int tile = index / 2;
        bool nature = tile < NatureKeys.Length;
        if (!nature) tile -= NatureKeys.Length;
        string key = (nature ? NatureKeys : PlaceKeys)[tile];
        string title = L.T(nature ? WorldBiomes.Get(Biomes[tile]).Name : WorldPlaceTypes.Label(key));
        return new(key, nature ? "nature" : "places", tile, index % 2, light, title) { GalleryIndex = index };
    }

    // Presentation only: select from known world data, never from prose or hidden GM notes.
    public static AdventureScene Select(WorldState state, WorldMap? visibleWorld, int variation = 0)
    {
        var link = state.World;
        var place = visibleWorld?.Places.FirstOrDefault(p => !p.Hidden && !string.IsNullOrEmpty(link?.Place)
            && p.Name.Equals(link.Place, StringComparison.OrdinalIgnoreCase)
            && (link.X == null || (link.X == p.X && link.Y == p.Y)));
        int x = link?.X ?? place?.X ?? 0, y = link?.Y ?? place?.Y ?? 0;
        char biome = visibleWorld != null && y >= 0 && y < visibleWorld.Biomes.Count
            && x >= 0 && x < visibleWorld.Biomes[y].Length ? visibleWorld.Biomes[y][x] : WorldBiomes.Plains;
        string type = (place?.Type ?? "").Trim().ToLowerInvariant();
        type = type switch
        {
            "inn" => "tavern", "building" or "manor" => "house", "market" => "shop",
            "fortress" or "barracks" => "castle", "crypt" => "dungeon", "camping" => "camp",
            _ => type,
        };
        int tile = Array.IndexOf(PlaceKeys, type);
        string atlas = tile >= 0 ? "places" : "nature";
        if (tile < 0) tile = Math.Max(0, Array.IndexOf(Biomes, biome));
        string key = (atlas == "places" ? PlaceKeys : NatureKeys)[tile];
        uint hash = 2166136261;
        foreach (char c in $"{link?.Id}|{x}|{y}|{link?.Place}|{key}") hash = unchecked((hash ^ c) * 16777619);
        int variant = (int)((hash + (uint)variation) & 1);
        return new(key, atlas, tile, variant, state.Time.PartOfDay,
            place?.Name ?? L.T(WorldBiomes.Get(biome).Name));
    }

    public bool Interior => Key is "dungeon" or "cave" or "tavern" or "house" or "shop" or "temple" or "tower";
    public bool Snow => Key is "peaks" or "snow" or "taiga";
    public IReadOnlyList<SceneFire> Fires => FireLayouts.TryGetValue((Key, Variant), out var fires) ? fires : [];
    public bool Animated => Snow || Fires.Count > 0;
}

public enum AdventureSceneStyle { Braille, Mosaic, HalfBlock, Quadrants }

// All frames are computed on the UI thread and cached; animation never touches game state.
public sealed class AdventureSceneRenderer
{
    public const int FrameCount = 128;
    public const int FrameIntervalMs = 50;
    private string? _cacheKey;
    private string[][] _frames = [];
    private byte[]? _fireMasks;
    private static readonly Dictionary<string, SKBitmap> Atlases = [];
    private static readonly object AtlasLock = new();

    // Patch individual cells, rather than resending an entire illustrated row for one glint.
    // Keeping animation traffic small also keeps the ConPTY input/output queues responsive.
    public static string Patch(IReadOnlyList<string> previous, IReadOnlyList<string> next, int left, int top)
    {
        var output = new StringBuilder();
        for (int y = 0; y < next.Count; y++)
        {
            if (y < previous.Count && previous[y] == next[y]) continue;
            var old = y < previous.Count ? Cells(previous[y]) : [];
            var cells = Cells(next[y]);
            bool contiguous = false;
            string? colors = null;
            for (int x = 0; x < cells.Count; x++)
            {
                if (x < old.Count && old[x] == cells[x]) { contiguous = false; continue; }
                if (!contiguous) output.Append($"\x1b[{top + y + 1};{left + x + 1}H");
                if (colors != cells[x].colors) output.Append(cells[x].colors);
                output.Append(cells[x].glyph);
                colors = cells[x].colors;
                contiguous = true;
            }
        }
        return output.ToString();
    }

    private static List<(char glyph, string colors)> Cells(string row)
    {
        var result = new List<(char, string)>();
        string foreground = "", background = "";
        for (int i = 0; i < row.Length; i++)
        {
            if (row[i] == '\x1b')
            {
                int end = row.IndexOf('m', i);
                if (end < 0) break;
                string color = row[i..(end + 1)];
                if (color.StartsWith("\x1b[38;")) foreground = color;
                else if (color.StartsWith("\x1b[48;")) background = color;
                i = end;
            }
            else result.Add((row[i], foreground + background));
        }
        return result;
    }
    private static SKBitmap Atlas(string name)
    {
        lock (AtlasLock)
        {
            if (Atlases.TryGetValue(name, out var bitmap)) return bitmap;
            using var stream = typeof(AdventureScene).Assembly.GetManifestResourceStream($"NaviDnD.Scenes.{name}.png")
                ?? throw new InvalidOperationException($"Missing scene atlas {name}.");
            return Atlases[name] = SKBitmap.Decode(stream) ?? throw new InvalidOperationException($"Invalid scene atlas {name}.");
        }
    }

    public string[] Render(AdventureScene scene, int columns, int rows, IReadOnlyList<int> background,
        int phase = 0, AdventureSceneStyle style = AdventureSceneStyle.Quadrants)
    {
        if (columns <= 0 || rows <= 0) return [];
        string key = $"{scene.Atlas}/{scene.Tile}/{scene.Variant}/{scene.Light}/{columns}/{rows}/{style}/{string.Join(',', background)}";
        if (_cacheKey != key)
        {
            _cacheKey = key;
            _frames = new string[FrameCount][];
            _fireMasks = null;
        }
        phase = ((phase % FrameCount) + FrameCount) % FrameCount;
        if (!scene.Animated) phase = 0;
        if (_frames[phase] != null) return _frames[phase];
        bool stableFire = style == AdventureSceneStyle.Quadrants && scene.Fires.Count > 0;
        // Use one partition for the entire light cycle, even when the first requested frame is not zero.
        // Re-clustering slightly different colors every frame makes isolated quadrants jump.
        if (stableFire && _fireMasks == null && phase != 0)
            Render(scene, columns, rows, background, 0, style);
        bool chooseFireMasks = stableFire && _fireMasks == null;
        if (chooseFireMasks) _fireMasks = new byte[columns * rows];
        using var pixels = Raster(scene, columns * (style == AdventureSceneStyle.HalfBlock ? 1 : 2),
            rows * (style is AdventureSceneStyle.HalfBlock or AdventureSceneStyle.Quadrants ? 2 : 4), phase,
            (float)columns / (rows * 2));
        var result = new string[rows];
        int[] bits = [0, 3, 1, 4, 2, 5, 6, 7];
        var theme = new SKColor((byte)background[0], (byte)background[1], (byte)background[2]);
        for (int cy = 0; cy < rows; cy++)
        {
            var line = new StringBuilder();
            SKColor? lastFg = null, lastBg = null;
            for (int cx = 0; cx < columns; cx++)
            {
                char glyph;
                SKColor fg, bg;
                if (style == AdventureSceneStyle.HalfBlock)
                {
                    fg = pixels.GetPixel(cx, cy * 2); bg = pixels.GetPixel(cx, cy * 2 + 1); glyph = '▀';
                }
                else if (style == AdventureSceneStyle.Quadrants)
                {
                    SKColor[] cell = [pixels.GetPixel(cx * 2, cy * 2), pixels.GetPixel(cx * 2 + 1, cy * 2),
                        pixels.GetPixel(cx * 2, cy * 2 + 1), pixels.GetPixel(cx * 2 + 1, cy * 2 + 1)];
                    double best = double.MaxValue;
                    int bestMask = 0;
                    fg = bg = cell[0];
                    // Exhaustive two-color partition, only seven possibilities for four quadrants.
                    int fixedMask = stableFire && !chooseFireMasks ? _fireMasks![cy * columns + cx] : 0;
                    for (int mask = fixedMask > 0 ? fixedMask : 1; mask < (fixedMask > 0 ? fixedMask + 1 : 8); mask++)
                    {
                        var a = cell.Where((_, i) => (mask & (1 << i)) != 0).ToList();
                        var b = cell.Where((_, i) => (mask & (1 << i)) == 0).ToList();
                        var ca = Mean(a, cell[0]); var cb = Mean(b, cell[3]);
                        double error = 0;
                        for (int i = 0; i < 4; i++)
                        {
                            var c = (mask & (1 << i)) != 0 ? ca : cb;
                            error += Math.Pow(c.Red - cell[i].Red, 2) + Math.Pow(c.Green - cell[i].Green, 2) + Math.Pow(c.Blue - cell[i].Blue, 2);
                        }
                        if (error < best) { best = error; bestMask = mask; fg = ca; bg = cb; }
                    }
                    if (chooseFireMasks) _fireMasks![cy * columns + cx] = (byte)bestMask;
                    glyph = " ▘▝▀▖▌▞▛▗▚▐▜▄▙▟█"[bestMask];
                }
                else
                {
                    var cell = new SKColor[8];
                    for (int k = 0; k < 8; k++) cell[k] = pixels.GetPixel(cx * 2 + k % 2, cy * 4 + k / 2);
                    var ordered = cell.OrderBy(Luma).ToArray();
                    double threshold = (Luma(ordered[1]) + Luma(ordered[6])) / 2;
                    int mask = 0;
                    var bright = new List<SKColor>(); var dark = new List<SKColor>();
                    for (int k = 0; k < 8; k++)
                    {
                        bool lit = style == AdventureSceneStyle.Braille
                            ? Luma(cell[k]) > 25 + new[] { 0, 90, 135, 45, 65, 155, 110, 20 }[k] * .6
                            : Luma(cell[k]) >= threshold;
                        if (lit) { mask |= 1 << bits[k]; bright.Add(cell[k]); }
                        else dark.Add(cell[k]);
                    }
                    fg = Mean(bright, ordered[7]);
                    bg = style == AdventureSceneStyle.Braille ? theme : Mean(dark, ordered[0]);
                    glyph = (char)(0x2800 + mask);
                }
                // Reuse a bounded palette across frames, preserving pixel-art shading and
                // reducing redundant changes between nearly identical colors.
                fg = Palette(fg);
                if (style != AdventureSceneStyle.Braille) bg = Palette(bg);
                if (fg != lastFg) { line.Append($"\x1b[38;2;{fg.Red};{fg.Green};{fg.Blue}m"); lastFg = fg; }
                if (bg != lastBg) { line.Append($"\x1b[48;2;{bg.Red};{bg.Green};{bg.Blue}m"); lastBg = bg; }
                line.Append(glyph);
            }
            result[cy] = line.ToString();
        }
        return _frames[phase] = result;
    }

    private static double Luma(SKColor c) => c.Red * .2126 + c.Green * .7152 + c.Blue * .0722;
    // Small color steps keep fading lights smooth instead of jumping by 17 levels.
    private static SKColor Palette(SKColor c) => new(
        (byte)Math.Min(255, (c.Red + 2) / 4 * 4), (byte)Math.Min(255, (c.Green + 2) / 4 * 4), (byte)Math.Min(255, (c.Blue + 2) / 4 * 4));
    private static SKColor Mean(List<SKColor> colors, SKColor fallback) => colors.Count == 0 ? fallback :
        new((byte)colors.Average(c => c.Red), (byte)colors.Average(c => c.Green), (byte)colors.Average(c => c.Blue));

    public static SKBitmap Raster(AdventureScene scene, int width, int height, int phase = 0, float? physicalAspect = null)
    {
        phase = ((phase % FrameCount) + FrameCount) % FrameCount;
        double progress = (double)phase / FrameCount;
        double angle = progress * Math.Tau;
        var atlas = Atlas($"{scene.Atlas}-{(scene.Variant == 0 ? "a" : "b")}");
        float tw = atlas.Width / 4f, th = atlas.Height / 4f;
        // Crop to cover the actual viewport without borders; skip atlas seams.
        var src = new SKRect(scene.Tile % 4 * tw + 2, scene.Tile / 4 * th + 2,
            (scene.Tile % 4 + 1) * tw - 2, (scene.Tile / 4 + 1) * th - 2);
        float ratio = physicalAspect ?? (float)width / height;
        if (src.Width / src.Height > ratio) { float d = (src.Width - src.Height * ratio) / 2; src.Left += d; src.Right -= d; }
        else { float d = (src.Height - src.Width / ratio) / 2; src.Top += d; src.Bottom -= d; }
        var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        using (var image = SKImage.FromBitmap(atlas))
            canvas.DrawImage(image, src, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear));
        var fires = scene.Fires.Select(f => new SceneFire(
            (scene.Tile % 4 * tw + f.X * tw - src.Left) * width / src.Width,
            (scene.Tile / 4 * th + f.Y * th - src.Top) * height / src.Height,
            f.Radius * tw,
            f.Height * th * height / src.Height)).ToArray();
        // A slow, continuous rise and fall, shared by the flame and the light on nearby surfaces.
        double pulse = .5 - .5 * Math.Cos(Math.Min(phase, FrameCount - phase) * Math.Tau / FrameCount);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var p = bitmap.GetPixel(x, y);
            double r = p.Red, g = p.Green, b = p.Blue;
            if (!scene.Interior)
            {
                if (scene.Light == PartsOfDay.Night) { r *= .48; g *= .57; b *= .78; }
                else if (scene.Light == PartsOfDay.Evening) { r *= .95; g *= .78; b *= .84; }
            }
            double light = 0;
            foreach (var fire in fires)
            {
                double dx = (x - fire.X) * src.Width / width / fire.Radius;
                double dy = (y - fire.Y + fire.Height * .4) * src.Height / height / fire.Radius;
                double falloff = Math.Max(0, 1 - dx * dx - dy * dy);
                light += (pulse - .5) * .44 * falloff * falloff;
            }
            bitmap.SetPixel(x, y, new SKColor((byte)Math.Clamp(r * (1 + light), 0, 255),
                (byte)Math.Clamp(g * (1 + light * .75), 0, 255), (byte)Math.Clamp(b * (1 + light * .4), 0, 255)));
        }
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = new SKColor(200, 220, 235, 145), IsAntialias = true })
        {
            if (scene.Snow)
                for (int i = 0; i < 22; i++)
                {
                    float x = (float)((i * 47 + 2 + Math.Sin(angle + i) * 1.5) % width);
                    float y = (float)((i * 29 + progress * height) % height);
                    float fade = Math.Clamp(Math.Min(y, height - y) / 2, 0, 1);
                    paint.Color = new SKColor(200, 220, 235, (byte)(145 * fade));
                    canvas.DrawCircle(x, y, .75f, paint);
                }
        }
        return bitmap;
    }

}
