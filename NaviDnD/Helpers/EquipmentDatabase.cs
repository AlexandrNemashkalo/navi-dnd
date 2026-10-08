using System.Text.Json;
using System.Text.RegularExpressions;

namespace NaviDnD.Helpers;

public class EquipmentInfo
{
    public required string Name { get; init; }
    public string NameEn { get; init; } = "";
    public string Category { get; init; } = ""; // "обычное" | "магическое"
    public string Type { get; init; } = "";
    public string TypeAdditions { get; init; } = "";
    public string DamageVal { get; init; } = "";
    public string DamageType { get; init; } = "";
    public List<string> Props { get; init; } = [];
    public string Ac { get; init; } = "";
    public string Weight { get; init; } = "";
    public string Cost { get; init; } = "";
    public string Rarity { get; init; } = ""; // только магические
    public string Attunement { get; init; } = "";
    public string Text { get; init; } = "";
    public string Source { get; init; } = "";

    // Название на языке игры (L.World).
    public string LocalizedName => L.WorldIsEnglish ? NameEn : Name;
}

// Справочник снаряжения DnD5e (GameData/DnD5e_equipment_BD.dtn — обычные предметы,
// GameData/DnD5e_magic_equipment_BD.dtn — магические) — по аналогии с SpellDatabase/MonsterDatabase.
// Поля берутся из "en" (механика: damageVal/ac/props/weight/coast/rarity/attunement) — переводом
// в русский текст description занимается сам ИИ (EquipmentTools.FindItem), как он уже делает для
// остальной механики; отдельного русского перевода формул/свойств в исходнике нет.
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

    // Точный поиск по имени на любом языке (русское, "nic", английское).
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

    // Исходник хранит "text" как HTML-разметку (<ul><li>...</li></ul>, <br>) — убираем теги и
    // схлопываем пробелы, урезаем длинные магические эффекты, чтобы не раздувать ответ тула.
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
