using System.Text.Json;

namespace NaviDnD.Helpers;

public class SpellInfo
{
    public required string Name { get; init; }
    public string NameEn { get; init; } = "";
    public int Level { get; init; }
    public string School { get; init; } = "";
    public string CastingTime { get; init; } = "";
    public string Range { get; init; } = "";
    public string Components { get; init; } = "";
    public string Materials { get; init; } = "";
    public string Duration { get; init; } = "";
    public string Text { get; init; } = "";
    public string Source { get; init; } = "";
    // The same article in English (the reference book has both languages); null - no translation.
    public SpellInfo? En { get; init; }

    // Article in the language of the game: the hero’s name and card - as the GM writes (L.World).
    public SpellInfo Localized => L.WorldIsEnglish && En != null ? En : this;
}

// Directory of spells DnD5e (GameData/DnD5e_spells_BD.dtn). The file is read and parsed once per process launch
// (Lazy) - then only search in the cache, without accessing the disk. Public - reused by the game
// (NaviDnD.Display.MouseUiHelper - spell card by exact Russian name), and NaviDnD.McpServer
// (SpellTools.FindSpell - fuzzy search by English name for AI-DM).
public static class SpellDatabase
{
    private static readonly string FilePath =
        Path.Combine(AppConfig.AssetDirectory("GameData"), "DnD5e_spells_BD.dtn");

    private static readonly Lazy<Dictionary<string, SpellInfo>> ByName = new(LoadByName);
    private static readonly Lazy<List<SpellInfo>> All = new(() => ByName.Value.Values.Distinct().ToList());

    public static IReadOnlyList<SpellInfo> AllSpells => All.Value;

    // Exact search by name in any language (Russian, "nic", English) - a spell card in the game: the hero's
    // name (HeroSpell.Name) - the canonical name from the reference book in the game language.
    public static SpellInfo? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return ByName.Value.GetValueOrDefault(Normalize(name));
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();

    private static Dictionary<string, SpellInfo> LoadByName()
    {
        var result = new Dictionary<string, SpellInfo>();
        if (!File.Exists(FilePath)) return result;

        using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
        if (!doc.RootElement.TryGetProperty("allSpells", out var spells)) return result;

        foreach (var entry in spells.EnumerateArray())
        {
            if (!entry.TryGetProperty("ru", out var ru)) continue;
            string name = GetStr(ru, "name");
            if (name.Length == 0) continue;

            bool hasEn = entry.TryGetProperty("en", out var en);
            string nameEn = hasEn ? GetStr(en, "name") : "";
            SpellInfo? english = nameEn.Length == 0 ? null : new SpellInfo
            {
                Name = nameEn,
                NameEn = nameEn,
                Level = int.TryParse(GetStr(en, "level"), out var enLvl) ? enLvl : int.TryParse(GetStr(ru, "level"), out var ruLvl) ? ruLvl : 0,
                School = GetStr(en, "school"),
                CastingTime = GetStr(en, "castingTime"),
                Range = GetStr(en, "range"),
                Components = GetStr(en, "components"),
                Materials = GetStr(en, "materials"),
                Duration = GetStr(en, "duration"),
                Text = GetStr(en, "text"),
                Source = GetStr(en, "source"),
            };

            var info = new SpellInfo
            {
                Name = name,
                NameEn = nameEn,
                En = english,
                Level = int.TryParse(GetStr(ru, "level"), out var lvl) ? lvl : 0,
                School = GetStr(ru, "school"),
                CastingTime = GetStr(ru, "castingTime"),
                Range = GetStr(ru, "range"),
                Components = GetStr(ru, "components"),
                Materials = GetStr(ru, "materials"),
                Duration = GetStr(ru, "duration"),
                Text = GetStr(ru, "text"),
                Source = GetStr(ru, "source"),
            };

            result[Normalize(name)] = info;
            string nic = GetStr(ru, "nic");
            if (nic.Length > 0) result.TryAdd(Normalize(nic), info);
            if (nameEn.Length > 0) result.TryAdd(Normalize(nameEn), info);
        }
        return result;
    }

    private static string GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
