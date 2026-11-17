using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace NaviDnD.Helpers;

// Offline-reviewed illustrations: same named lookup for AI references, MCP and cards.
// Saved image strings remain stable, content-addressed slugs; no player-data migration.
public sealed class ArtPictures
{
    public const string Prefix = "art32/";
    public static ArtPictures? Current { get; set; }
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sourceSprites = new(StringComparer.Ordinal);
    private readonly HashSet<string> _portraitImages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Binding>> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _engineAliases = new(StringComparer.Ordinal);
    private readonly List<string> _portraits = [];
    public IReadOnlyList<string> Portraits => _portraits;
    private readonly Dictionary<string, List<string>> _portraitGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _portraitGenders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _npcSpriteGroups = new(StringComparer.Ordinal);
    public IReadOnlyList<string> PortraitsForGroup(string group) =>
        _portraitGroups.TryGetValue(group, out var portraits) ? portraits : [];
    private sealed record Binding(string ImageId, HashSet<string> Categories, string Usage, string? Palette);

    private static string Normalize(string name) => string.Join(' ', name.Normalize(NormalizationForm.FormKC)
        .Replace('ё', 'е').Replace('Ё', 'Е').Replace('’', '\'').Replace('—', '-').ToLowerInvariant()
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static ArtPictures Load(string ruleSet, string? libraryRoot = null)
    {
        var result = new ArtPictures();
        if (string.IsNullOrWhiteSpace(ruleSet) || !ruleSet.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return result;
        string root = libraryRoot ?? AppConfig.AssetDirectory("ArtLibrary");
        string manifest = Path.Combine(root, "RuleSets", ruleSet, "runtime-pictures.json");
        if (!File.Exists(manifest)) return result;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            var file = document.RootElement;
            if (file.GetProperty("schemaVersion").GetInt32() != 1 || file.GetProperty("ruleSet").GetString() != ruleSet) return result;
            foreach (var image in file.GetProperty("images").EnumerateArray())
            {
                string id = image.GetProperty("id").GetString()!;
                string relative = image.GetProperty("path").GetString()!;
                if (id.Length != 64 || !id.All(Uri.IsHexDigit) || relative != "Runtime/Images/" + id + ".png") continue;
                string path = Path.Combine(root, "Runtime", "Images", id + ".png");
                if (!File.Exists(path)) continue;
                byte[] bytes = File.ReadAllBytes(path);
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(id, StringComparison.OrdinalIgnoreCase)
                    || !id.Equals(image.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase)) continue;
                using var bitmap = SKBitmap.Decode(bytes);
                if (bitmap?.Width != 32 || bitmap.Height != 32) continue;
                result._paths.Add(id, path);
                if (image.TryGetProperty("sourceAssetId", out var source) && source.GetString() is { } sourceId)
                    result._sourceSprites[Prefix + id] = sourceId;
            }
            foreach (var row in file.GetProperty("bindings").EnumerateArray())
            {
                string id = row.GetProperty("imageId").GetString()!;
                string usage = row.GetProperty("usage").GetString()!;
                if (!result._paths.ContainsKey(id) || usage is not ("item" or "map_actor" or "portrait" or "prop" or "terrain_tile" or "place_view")) continue;
                if (usage == "portrait") result._portraitImages.Add(Prefix + id);
                string? palette = row.TryGetProperty("palette", out var p) ? p.GetString() : null;
                if (!MetalPalettes.Contains(palette ?? "")) palette = null;
                var binding = new Binding(id, row.GetProperty("categories").EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal), usage, palette);
                foreach (var name in row.GetProperty("names").EnumerateArray())
                {
                    string key = Normalize(name.GetString()!);
                    if (!result._names.TryGetValue(key, out var values)) result._names[key] = values = [];
                    if (!values.Contains(binding)) values.Add(binding);
                }
            }
            if (file.TryGetProperty("portraits", out var portraits))
                foreach (var portrait in portraits.EnumerateArray())
                    if (portrait.GetProperty("framing").GetString() is "head" or "head_shoulders"
                        && portrait.GetProperty("imageId").GetString() is { } id && result._paths.ContainsKey(id))
                    {
                        result._portraits.Add(Prefix + id);
                        result._portraitImages.Add(Prefix + id);
                        if (portrait.TryGetProperty("gender", out var gender))
                            result._portraitGenders[Prefix + id] = gender.GetString() ?? "unknown";
                        if (portrait.TryGetProperty("group", out var group) && group.GetString() is { Length: > 0 } key)
                        {
                            if (!result._portraitGroups.TryGetValue(key, out var list)) result._portraitGroups[key] = list = [];
                            list.Add(Prefix + id);
                        }
                    }
            if (file.TryGetProperty("engineAliases", out var aliases))
                foreach (var alias in aliases.EnumerateObject())
                    if (alias.Value.GetString() is { } id && result._paths.ContainsKey(id))
                        result._engineAliases.Add(alias.Name, Prefix + id);
            if (file.TryGetProperty("npcSpriteGroups", out var groups))
                foreach (var group in groups.EnumerateObject())
                    result._npcSpriteGroups[group.Name] = group.Value.GetString()!;
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            result._names.Clear(); result._paths.Clear(); result._engineAliases.Clear(); result._portraits.Clear(); result._portraitGroups.Clear();
            result._portraitGenders.Clear(); result._npcSpriteGroups.Clear(); // Invalid optional pack preserves the existing renderer.
            result._sourceSprites.Clear(); result._portraitImages.Clear();
        }
        return result;
    }

    public List<string> Find(string name, string? category = null)
    {
        if (!_names.TryGetValue(Normalize(name), out var bindings)) return [];
        return bindings.Where(b => category == null || b.Categories.Contains(category))
            .Select(b => Prefix + b.ImageId + (b.Palette == null ? "" : "~" + b.Palette)).Distinct(StringComparer.Ordinal).ToList();
    }

    public string? ResolvePath(string slug)
    {
        if (!slug.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        string[] parts = slug[Prefix.Length..].Split('~');
        if (parts.Length > 2 || (parts.Length == 2 && !MetalPalettes.Contains(parts[1]))) return null;
        return _paths.GetValueOrDefault(parts[0]);
    }

    private static readonly HashSet<string> MetalPalettes = ["silver", "copper", "electrum", "platinum"];

    internal static void ApplyPalette(SKBitmap bitmap, string slug)
    {
        if (!slug.StartsWith(Prefix, StringComparison.Ordinal)) return;
        string palette = slug.Split('~').Last();
        if (!MetalPalettes.Contains(palette)) return;
        var (dark, mid, light) = palette switch
        {
            "copper" => (new SKColor(67, 30, 22), new SKColor(185, 98, 52), new SKColor(255, 211, 155)),
            "electrum" => (new SKColor(66, 62, 40), new SKColor(181, 170, 113), new SKColor(249, 245, 208)),
            "platinum" => (new SKColor(35, 51, 70), new SKColor(160, 191, 211), new SKColor(243, 253, 255)),
            _ => (new SKColor(43, 45, 52), new SKColor(166, 171, 185), new SKColor(251, 251, 255))
        };
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var source = bitmap.GetPixel(x, y);
                if (source.Alpha == 0) continue;
                double value = Math.Max(source.Red, Math.Max(source.Green, source.Blue)) / 255d;
                var a = value < .65 ? dark : mid;
                var b = value < .65 ? mid : light;
                double t = value < .65 ? value / .65 : (value - .65) / .35;
                bitmap.SetPixel(x, y, new SKColor((byte)(a.Red + (b.Red - a.Red) * t),
                    (byte)(a.Green + (b.Green - a.Green) * t), (byte)(a.Blue + (b.Blue - a.Blue) * t), source.Alpha));
            }
    }

    public string? EngineIcon(string slug) => _engineAliases.GetValueOrDefault(slug);

    public static string? TerrainImage(Data.Models.TerrainKind kind) =>
        CatalogSearch.IconPack == CatalogSearch.DcssPack
            ? Current?.Find(kind.Name, "terrain").FirstOrDefault() ?? Data.Models.TerrainCatalog.Image(kind)
            : Data.Models.TerrainCatalog.Image(kind);

    public bool IsPortrait(string? slug) => slug != null && (_portraitImages.Contains(slug)
        || slug.StartsWith(SvgToBrailleConverter.PortraitsPrefix, StringComparison.OrdinalIgnoreCase)
        || Path.IsPathRooted(slug));

    public string? ChooseNpcPortrait(string name, string? gender, string? original, ISet<string> used)
    {
        gender = Data.Models.NpcGenders.Known(gender);
        string? portraitGroup = null;
        if (IsPortrait(original))
        {
            if (!PortraitNeedsGenderUpdate(original, gender)) return original;
            portraitGroup = _portraitGroups.FirstOrDefault(p => p.Value.Contains(original!)).Key;
            if (portraitGroup == null) return original;
        }
        else if (original != null) original = _sourceSprites.GetValueOrDefault(original, original);
        // Explicit sprite families belong to the rule-set manifest; never infer species from localized prose.
        string? group = portraitGroup ?? (string.IsNullOrEmpty(original) ? "human" : _npcSpriteGroups
            .OrderByDescending(p => p.Key.Length).FirstOrDefault(p => original.StartsWith(p.Key, StringComparison.Ordinal)).Value);
        if (group == null) return null; // Keep non-humanoid or unknown creatures intact.
        var pool = PortraitsForGroup(group);
        if (gender is "male" or "female")
        {
            var matching = pool.Where(p => _portraitGenders.GetValueOrDefault(p) == gender).ToArray();
            // An unclassified face may fit; a face explicitly classified otherwise does not.
            pool = matching.Length > 0 ? matching : pool.Where(p =>
                _portraitGenders.GetValueOrDefault(p) is null or "unknown").ToArray();
        }
        else
            pool = pool.Where(p => _portraitGenders.GetValueOrDefault(p) is null or "unknown").ToArray();
        if (pool.Count == 0) return null;
        uint hash = 2166136261;
        foreach (char c in name.Trim().ToUpperInvariant()) hash = unchecked((hash ^ c) * 16777619);
        for (int offset = 0; offset < pool.Count; offset++)
        {
            string candidate = pool[(int)((hash + (uint)offset) % (uint)pool.Count)];
            if (!used.Contains(candidate)) return candidate;
        }
        return pool[(int)(hash % (uint)pool.Count)];
    }

    public bool PortraitConflictsWithGender(string? portrait, string? gender) =>
        portrait != null && Data.Models.NpcGenders.Known(gender) is { } known
        && _portraitGenders.TryGetValue(portrait, out var assigned)
        && Data.Models.NpcGenders.Known(assigned) is { } portraitGender && known != portraitGender;

    public bool PortraitNeedsGenderUpdate(string? portrait, string? gender)
    {
        if (PortraitConflictsWithGender(portrait, gender)) return true;
        if (portrait == null || Data.Models.NpcGenders.Known(gender) is not { } known
            || !_portraitGenders.TryGetValue(portrait, out var assigned)
            || Data.Models.NpcGenders.Known(assigned) != null) return false;
        return _portraitGroups.Any(group => group.Value.Contains(portrait)
            && group.Value.Any(face => _portraitGenders.GetValueOrDefault(face) == known));
    }

    // Custom files and portraits retain their identity; named inventory references follow corrected bindings.
    internal static string? Prefer(string? name, string category, string? existing)
    {
        if (CatalogSearch.IconPack != CatalogSearch.DcssPack || string.IsNullOrWhiteSpace(name)) return existing;
        if (!string.IsNullOrEmpty(existing) && ((category is not ("item" or "feature") && existing.StartsWith(Prefix, StringComparison.Ordinal))
            || existing.StartsWith(SvgToBrailleConverter.PortraitsPrefix, StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(existing))) return existing;
        return Current?.Find(name, category).FirstOrDefault() ?? existing;
    }
}
