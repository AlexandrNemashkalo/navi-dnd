using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaviDnD.Data.Art;

// V2 foundation. Not connected to saved games or V1 image rendering yet.
public sealed record ArtAsset(
    string Id, string RelativePath, string Style, string IdentityFamily,
    string Description, IReadOnlySet<string> Usages, IReadOnlyDictionary<string, string> Traits,
    bool TechnicalApproved = false, bool SemanticApproved = false, bool ArtApproved = false,
    string ContentHash = "");

public sealed class ArtCatalog
{
    public const int SchemaVersion = 1;
    public string Revision { get; }
    public IReadOnlyDictionary<string, ArtAsset> Assets { get; }

    private sealed record CatalogFile(int SchemaVersion, string Revision, List<AssetFile> Assets);
    private sealed record AssetFile(string Id, string RelativePath, string Style, string IdentityFamily,
        string Description, List<string> Usages, Dictionary<string, string> Traits,
        bool TechnicalApproved, bool SemanticApproved, bool ArtApproved, string ContentHash);

    // The serialized file uses lists; the live catalog freezes its own copies.
    // Catalog schema revisions are independent of saved-game StorageFormat.
    public static ArtCatalog Parse(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var file = JsonSerializer.Deserialize<CatalogFile>(json, options)
            ?? throw new JsonException("Missing art catalog.");
        if (file.SchemaVersion != SchemaVersion || file.Assets == null || file.Assets.Any(a => a == null))
            throw new JsonException("Unsupported or incomplete art catalog schema.");
        return new ArtCatalog(file.Revision, file.Assets.Select(a =>
        {
            if (a.Usages == null || a.Traits == null || a.Description == null || a.ContentHash == null ||
                a.Usages.Distinct(StringComparer.Ordinal).Count() != a.Usages.Count)
                throw new JsonException($"Incomplete or duplicate art metadata: {a.Id}");
            return new ArtAsset(a.Id, a.RelativePath, a.Style, a.IdentityFamily, a.Description,
                a.Usages.ToHashSet(StringComparer.Ordinal), a.Traits, a.TechnicalApproved, a.SemanticApproved, a.ArtApproved, a.ContentHash);
        }));
    }

    public ArtCatalog(string revision, IEnumerable<ArtAsset> assets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentNullException.ThrowIfNull(assets);
        Revision = revision;
        var entries = new Dictionary<string, ArtAsset>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var original in assets)
        {
            ArgumentNullException.ThrowIfNull(original);
            ArgumentNullException.ThrowIfNull(original.Usages);
            ArgumentNullException.ThrowIfNull(original.Traits);
            ArgumentException.ThrowIfNullOrWhiteSpace(original.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(original.Style);
            ArgumentException.ThrowIfNullOrWhiteSpace(original.IdentityFamily);
            if (original.ContentHash == null || original.ContentHash.Length != 64 || !original.ContentHash.All(Uri.IsHexDigit))
                throw new ArgumentException($"Invalid art content hash: {original.Id}");
            if (!SafeRelativePath(original.RelativePath))
                throw new ArgumentException($"Unsafe art path: {original.RelativePath}");
            if (!paths.Add(original.RelativePath))
                throw new ArgumentException($"Duplicate art path (including case): {original.RelativePath}");
            if (original.Usages.Count == 0 || original.Usages.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException($"Missing art usage: {original.Id}");
            if (original.Traits.Any(t => string.IsNullOrWhiteSpace(t.Key) || string.IsNullOrWhiteSpace(t.Value)))
                throw new ArgumentException($"Empty art trait: {original.Id}");
            if (original.SemanticApproved && string.IsNullOrWhiteSpace(original.Description))
                throw new ArgumentException($"Missing reviewed description: {original.Id}");
            // Snapshot caller-owned collections: edits outside this catalog cannot alter eligibility.
            var asset = original with
            {
                Usages = original.Usages.ToFrozenSet(StringComparer.Ordinal),
                Traits = original.Traits.ToFrozenDictionary(StringComparer.Ordinal),
                ContentHash = original.ContentHash.ToUpperInvariant(),
            };
            if (!entries.TryAdd(asset.Id, asset))
                throw new ArgumentException($"Duplicate art ID: {asset.Id}");
        }
        Assets = entries.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static bool SafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && !path.Any(c => "\\:<>\"|?*".Contains(c) || char.IsControl(c)) &&
        !path.StartsWith('/') && path.Split('/').All(SafeSegment);

    private static bool SafeSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')) return false;
        string basename = segment.Split('.')[0].ToUpperInvariant();
        return basename is not ("CON" or "PRN" or "AUX" or "NUL") &&
            !(basename.Length == 4 && (basename.StartsWith("COM", StringComparison.Ordinal) || basename.StartsWith("LPT", StringComparison.Ordinal)) &&
              basename[3] is >= '1' and <= '9');
    }

    internal static string StableKey(params string[] values)
    {
        // Length-prefix components: different tuples cannot alias by inserting a delimiter.
        var input = string.Concat(values.Select(v => Encoding.UTF8.GetByteCount(v).ToString(CultureInfo.InvariantCulture) + ":" + v));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}
