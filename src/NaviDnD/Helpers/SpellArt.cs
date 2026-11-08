using System.Collections.Concurrent;
using System.Text.Json;
using SkiaSharp;
using NaviDnD.Data.Models;
using System.Text;

namespace NaviDnD.Helpers;

public enum SpellArtMode { Braille, Pixels }

// A separate entry point prevents spell-only color/mask resources from becoming item pictures.
// Rule-specific names and assignments live in the art manifest, not in the engine or saves.
public sealed class SpellArt
{
    internal static SpellArt? Current { get; set; }
    public SpellArtMode DefaultMode { get; private set; } = SpellArtMode.Braille;
    private readonly string _images;
    private sealed record Icon(string Id, string Mask, string Color, string Background, string Pixel, int[] NameColor);
    private readonly Dictionary<string, Icon> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Icon> _aliases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string[]> _cache = new();

    private SpellArt(string images) => _images = images;

    private sealed class PreviewSpell : HeroSpell { }
    internal static bool IsPreview(HeroSpell spell) => spell is PreviewSpell;

    // Temporary in-game gallery: UI rows only, never attached to Hero.Spells or saved.
    // Real spells keep their names, preparation state and colors; match both world languages.
    internal List<HeroSpell> WithPreviews(IEnumerable<HeroSpell> spells)
    {
        var result = spells.ToList();
        var present = result.Select(s => GetIconId(s.Name)).Where(id => id != null).ToHashSet(StringComparer.Ordinal);
        foreach (var (canonicalName, icon) in _icons)
        {
            if (!present.Add(icon.Id)) continue;
            var info = SpellDatabase.Find(canonicalName);
            result.Add(new PreviewSpell
            {
                Name = info?.Localized.Name ?? canonicalName,
                Level = info?.Level ?? 0,
                Color = [.. icon.NameColor],
            });
        }
        return result;
    }

    public static SpellArt Load(string ruleSet, SpellArtMode? mode = null)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "SpellArt");
        if (!Directory.Exists(root)) root = Path.Combine(AppConfig.ProjectRoot, "SpellArt"); // macOS Resources
        var result = new SpellArt(Path.Combine(root, "Images", "bg3-spells"));
        if (string.IsNullOrWhiteSpace(ruleSet) || ruleSet.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) return result;
        string manifest = Path.Combine(root, "RuleSets", ruleSet, "spell-icons.json");
        if (!File.Exists(manifest)) return result;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            var data = document.RootElement;
            if (data.GetProperty("schemaVersion").GetInt32() != 2
                || data.GetProperty("usage").GetString() != "spell"
                || data.GetProperty("ruleSet").GetString() != ruleSet) return result;
            result.DefaultMode = mode ?? (data.GetProperty("defaultRenderMode").GetString() switch
            {
                "braille" => SpellArtMode.Braille,
                "pixels" => SpellArtMode.Pixels,
                _ => throw new InvalidDataException("Unknown spell art render mode."),
            });
            foreach (var icon in data.GetProperty("icons").EnumerateArray())
            {
                if (icon.GetProperty("usage").GetString() != "spell") continue;
                string? name = icon.GetProperty("name").GetString();
                string? id = icon.GetProperty("id").GetString();
                string? mask = icon.GetProperty("maskFile").GetString();
                string? color = icon.GetProperty("colorFile").GetString();
                string? background = icon.GetProperty("backgroundFile").GetString();
                string? pixel = icon.GetProperty("pixelFile").GetString();
                int[] nameColor = icon.GetProperty("nameColor").EnumerateArray().Select(c => c.GetInt32()).ToArray();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(id)
                    || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
                    || !SafeFile(mask) || !SafeFile(color) || !SafeFile(background) || !SafeFile(pixel)
                    || nameColor.Length != 3 || nameColor.Any(c => c is < 0 or > 255)) continue;
                var binding = new Icon(id, mask!, color!, background!, pixel!, nameColor);
                result._icons.Add(name.Trim(), binding);
                void Alias(string? value)
                {
                    if (string.IsNullOrWhiteSpace(value)) return;
                    string key = NormalizeName(value);
                    if (result._aliases.TryGetValue(key, out var previous) && previous.Id != id)
                        throw new InvalidDataException("Ambiguous spell icon alias: " + value);
                    result._aliases[key] = binding;
                }
                Alias(name); Alias(id);
                foreach (var alias in icon.GetProperty("aliases").EnumerateArray()) Alias(alias.GetString());
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            result._icons.Clear(); // A broken optional pack keeps the normal spell artwork.
            result._aliases.Clear();
        }
        return result;
    }

    private static bool SafeFile(string? file) => !string.IsNullOrEmpty(file)
        && file.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.')
        && !file.Contains("..", StringComparison.Ordinal) && file.EndsWith(".png", StringComparison.Ordinal);

    // Exact normalized aliases only: no keyword search and no dependence on the selected icon pack.
    private static string NormalizeName(string name) => string.Join(' ', name.Normalize(NormalizationForm.FormKC)
        .ToLowerInvariant().Replace('ё', 'е').Replace('’', '\'').Replace('‘', '\'').Replace('ʼ', '\'')
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private Icon? Resolve(string? name) => string.IsNullOrWhiteSpace(name) ? null : _aliases.GetValueOrDefault(NormalizeName(name));
    public string? GetIconId(string? spellName) => Resolve(spellName)?.Id;
    public List<int>? NameColor(string? spellName) => Resolve(spellName) is { } icon ? [.. icon.NameColor] : null;

    public string[]? Render(string? spellName, int widthChars = 36, SpellArtMode? mode = null)
    {
        if (Resolve(spellName) is not { } files) return null;
        mode ??= DefaultMode;
        string key = files.Id + "|" + mode + "|" + widthChars;
        if (_cache.TryGetValue(key, out var cached)) return cached;
        try
        {
            if (mode == SpellArtMode.Pixels)
            {
                using var pixels = SKBitmap.Decode(Path.Combine(_images, files.Pixel));
                return pixels == null ? null : _cache.GetOrAdd(key, _ => PixelArt.Render(pixels, widthChars));
            }
            using var mask = SKBitmap.Decode(Path.Combine(_images, files.Mask));
            using var color = SKBitmap.Decode(Path.Combine(_images, files.Color));
            using var background = SKBitmap.Decode(Path.Combine(_images, files.Background));
            if (mask == null || color == null || background == null) return null;
            return _cache.GetOrAdd(key, _ => ColoredBraille.Render(mask, color, widthChars, background));
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            return null;
        }
    }
}
