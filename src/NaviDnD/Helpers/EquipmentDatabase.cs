using System.Text.Json;
using System.Text.RegularExpressions;

namespace NaviDnD.Helpers;

public class EquipmentInfo
{
    public required string Name { get; init; }
    public string NameEn { get; init; } = "";
    public string Category { get; init; } = ""; // "normal" | "magical"
    public string Type { get; init; } = "";
    public string TypeAdditions { get; init; } = "";
    public string DamageVal { get; init; } = "";
    public string DamageType { get; init; } = "";
    public List<string> Props { get; init; } = [];
    public string Ac { get; init; } = "";
    public string Weight { get; init; } = "";
    public string Cost { get; init; } = "";
    public string Rarity { get; init; } = ""; // only magic
    public string Attunement { get; init; } = "";
    public string Text { get; init; } = "";
    public string Source { get; init; } = "";

    // Name in game language (L.World).
    public string LocalizedName => L.WorldIsEnglish ? NameEn : Name;
}

// Equipment Directory DnD5e (GameData/DnD5e_equipment_BD.dtn - ordinary items,
// GameData/DnD5e_magic_equipment_BD.dtn - magical) - by analogy with SpellDatabase/MonsterDatabase. Fields are
// taken from "en" (mechanics: damageVal/ac/props/weight/coast/rarity/attunement) - the translation into Russian
// text of description is done by the AI itself (EquipmentTools.FindItem), as it already does for the rest
// mechanics; there is no separate Russian translation of formulas/properties in the source code.
public static class EquipmentDatabase
{
    private static readonly string MundaneFilePath =
        Path.Combine(AppConfig.AssetDirectory("GameData"), "DnD5e_equipment_BD.dtn");
    private static readonly string MagicFilePath =
        Path.Combine(AppConfig.AssetDirectory("GameData"), "DnD5e_magic_equipment_BD.dtn");

    private static readonly string[] RarityByIndex =
        ["", "Common", "Uncommon", "Rare", "Very Rare", "Legendary"];

    private static readonly Lazy<Dictionary<string, EquipmentInfo>> ByName = new(LoadAll);
    private static readonly Lazy<List<EquipmentInfo>> All = new(() => ByName.Value.Values.Distinct().ToList());

    public static IReadOnlyList<EquipmentInfo> AllItems => All.Value;

    // Exact search by name in any language (Russian, "nic", English).
    public static EquipmentInfo? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return ByName.Value.GetValueOrDefault(Normalize(name));
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();

    private static Dictionary<string, EquipmentInfo> LoadAll()
    {
        var result = new Dictionary<string, EquipmentInfo>();
        LoadFile(MundaneFilePath, "обычное", result);
        LoadFile(MagicFilePath, "магическое", result);
        return result;
    }

    private static void LoadFile(string filePath, string category, Dictionary<string, EquipmentInfo> result)
    {
        if (!File.Exists(filePath)) return;

        using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
        if (!doc.RootElement.TryGetProperty("itemsList", out var items)) return;

        foreach (var entry in items.EnumerateArray())
        {
            if (!entry.TryGetProperty("en", out var en)) continue;
            string nameEn = GetStr(en, "name");
            if (nameEn.Length == 0) continue;

            string nameRu = entry.TryGetProperty("ru", out var ru) ? GetStr(ru, "name") : "";
            if (nameRu.Length == 0) nameRu = nameEn;

            var info = new EquipmentInfo
            {
                Name = nameRu,
                NameEn = nameEn,
                Category = category,
                Type = GetStr(en, "type"),
                TypeAdditions = GetStr(en, "typeAdditions"),
                DamageVal = GetStr(en, "damageVal"),
                DamageType = GetStr(en, "damageType"),
                Props = GetStrArray(en, "props"),
                Ac = GetStr(en, "ac"),
                Weight = GetStr(en, "weight"),
                Cost = GetStr(en, "coast"),
                Rarity = GetRarityName(en),
                Attunement = GetStr(en, "attunement"),
                Text = CleanText(GetStr(en, "text")),
                Source = GetStr(en, "source"),
            };

            var key = Normalize(nameRu);
            if (!result.ContainsKey(key)) result[key] = info;

            string nic = entry.TryGetProperty("ru", out var ru2) ? GetStr(ru2, "nic") : "";
            if (nic.Length > 0) result.TryAdd(Normalize(nic), info);
            result.TryAdd(Normalize(nameEn), info);
        }
    }

    private static string GetRarityName(JsonElement en)
    {
        if (!en.TryGetProperty("rarity", out var v)) return "";
        int idx = v.ValueKind switch
        {
            JsonValueKind.Number => v.GetInt32(),
            JsonValueKind.String => int.TryParse(v.GetString(), out var n) ? n : 0,
            _ => 0
        };
        return idx > 0 && idx < RarityByIndex.Length ? RarityByIndex[idx] : "";
    }

    // The source stores "text" as HTML- markup (<ul><li>...</li></ul>, <br>) - remove tags and collapse spaces,
    // trim long magical effects so as not to inflate the tool's response.
    private static string CleanText(string text)
    {
        if (text.Length == 0) return text;
        string cleaned = Regex.Replace(text, "<[^>]+>", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.Length > 500 ? cleaned[..500] + "…" : cleaned;
    }

    private static List<string> GetStrArray(JsonElement en, string prop)
    {
        var result = new List<string>();
        if (!en.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String) result.Add(item.GetString() ?? "");
        return result;
    }

    private static string GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
