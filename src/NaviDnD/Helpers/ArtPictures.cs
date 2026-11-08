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
    private readonly Dictionary<string, List<Binding>> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _engineAliases = new(StringComparer.Ordinal);
    private readonly List<string> _portraits = [];
    public IReadOnlyList<string> Portraits => _portraits;
    private readonly Dictionary<string, List<string>> _portraitGroups = new(StringComparer.Ordinal);
    public IReadOnlyList<string> PortraitsForGroup(string group) =>
        _portraitGroups.TryGetValue(group, out var portraits) ? portraits : [];
    private sealed record Binding(string ImageId, HashSet<string> Categories, string Usage);

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
            }
            foreach (var row in file.GetProperty("bindings").EnumerateArray())
            {
                string id = row.GetProperty("imageId").GetString()!;
                string usage = row.GetProperty("usage").GetString()!;
                if (!result._paths.ContainsKey(id) || usage is not ("item" or "map_actor" or "portrait" or "prop" or "terrain_tile" or "place_view")) continue;
                var binding = new Binding(id, row.GetProperty("categories").EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal), usage);
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
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            result._names.Clear(); result._paths.Clear(); result._engineAliases.Clear(); result._portraits.Clear(); result._portraitGroups.Clear(); // Invalid optional pack preserves the existing renderer.
        }
        return result;
    }

    public List<string> Find(string name, string? category = null)
    {
        if (!_names.TryGetValue(Normalize(name), out var bindings)) return [];
        return bindings.Where(b => category == null || b.Categories.Contains(category))
            .Select(b => Prefix + b.ImageId).Distinct(StringComparer.Ordinal).ToList();
    }

    public string? ResolvePath(string slug) => slug.StartsWith(Prefix, StringComparison.Ordinal)
        ? _paths.GetValueOrDefault(slug[Prefix.Length..]) : null;

    public string? EngineIcon(string slug) => _engineAliases.GetValueOrDefault(slug);

    // Custom portraits and assigned art retain their identity. Only a known reference illustration is upgraded.
    internal static string? Prefer(string? name, string category, string? existing)
    {
        if (CatalogSearch.IconPack != CatalogSearch.DcssPack || string.IsNullOrWhiteSpace(name)) return existing;
        if (!string.IsNullOrEmpty(existing) && (existing.StartsWith(Prefix, StringComparison.Ordinal)
            || existing.StartsWith(SvgToBrailleConverter.PortraitsPrefix, StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(existing))) return existing;
        return Current?.Find(name, category).FirstOrDefault() ?? existing;
    }
}
