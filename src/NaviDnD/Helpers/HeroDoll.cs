using SkiaSharp;

namespace NaviDnD.Helpers;

// Hero portrait of the Dungeon Crawl pack - full height, assembled like the hero of Dungeon Crawl itself: the body of
// the race + clothes, armor, hair and weapons of the class (layers of icons/dcss/player, 32×32 with transparency).
// Slug "dcss/doll/base/human_male+body/robe_blue+head/wizard_blue+hand_right/staff_mage" - the layers are stored in
// it, drawn by PixelArt like any tile. Recipes - Prompts/{RuleSet}/heroOptions.json: race "doll" (body),
// classes "dolls" (gear variants).
public static class HeroDoll
{
    public const string Prefix = CatalogSearch.DcssPrefix + "doll/";

    // Gear of a class: layers + hair (for a female body - its own), a beard only for a male body.
    public sealed record Gear(string Layers, string? Hair = null, string? HairFemale = null, string? Beard = null);

    // Drawing order as in Dungeon Crawl: the cloak is behind the body, the helmet is over the hair, weapons are on top.
    private static readonly string[] LayerOrder = ["cloak", "base", "boots", "legs", "body", "gloves", "hair", "beard", "head", "hand_right", "hand_left"];

    // body - the body of the race without gender ("human"), hair = false - a race without hair (dragonborn, kenku).
    public static string Slug(string body, bool female, Gear gear, bool hair = true)
    {
        var layers = new List<string> { $"base/{body}_{(female ? "female" : "male")}" };
        layers.AddRange(gear.Layers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (hair && (female ? gear.HairFemale : gear.Hair) is { } h) layers.Add("hair/" + h);
        if (hair && !female && gear.Beard is { } b) layers.Add("beard/" + b);
        return Prefix + string.Join('+', layers.OrderBy(Rank));
    }

    private static int Rank(string layer)
    {
        int i = Array.IndexOf(LayerOrder, layer[..Math.Max(0, layer.IndexOf('/'))]);
        return i < 0 ? LayerOrder.Length : i;
    }

    // Layers on one 32×32 canvas; a missing layer file - null (not a portrait with a hole).
    public static SKBitmap? Render(string slug)
    {
        string root = Path.Combine(CatalogSearch.DcssBasePath, "player");
        var bmp = new SKBitmap(new SKImageInfo(32, 32, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        foreach (string layer in slug[Prefix.Length..].Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            using var src = SKBitmap.Decode(Path.Combine(root, layer.Replace('/', Path.DirectorySeparatorChar) + ".png"));
            if (src == null) { bmp.Dispose(); return null; }
            using var image = SKImage.FromBitmap(src);
            canvas.DrawImage(image, SKRect.Create(0, 0, 32, 32), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }
        canvas.Flush();
        return bmp;
    }
}
