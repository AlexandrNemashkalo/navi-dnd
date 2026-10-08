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
    // Та же статья по-английски (у справочника оба языка); null — перевода нет.
    public SpellInfo? En { get; init; }

    // Статья на языке игры: имя у героя и карточка — как пишет мастер (L.World).
    public SpellInfo Localized => L.WorldIsEnglish && En != null ? En : this;
}

// Справочник заклинаний DnD5e (GameData/DnD5e_spells_BD.dtn). Файл читается и парсится один раз за
// запуск процесса (Lazy) — дальше только поиск по кэшу, без обращения к диску. Public — переиспользуется
// и игрой (NaviDnD.Display.MouseUiHelper — карточка заклинания по точному русскому имени), и
// NaviDnD.McpServer (SpellTools.FindSpell — нечёткий поиск по английскому имени для ИИ-ДМ).
public static class SpellDatabase
{
    private static readonly string FilePath =
        Path.Combine(AppConfig.AssetDirectory("GameData"), "DnD5e_spells_BD.dtn");

    private static readonly Lazy<Dictionary<string, SpellInfo>> ByName = new(LoadByName);
    private static readonly Lazy<List<SpellInfo>> All = new(() => ByName.Value.Values.Distinct().ToList());

    public static IReadOnlyList<SpellInfo> AllSpells => All.Value;

    // Точный поиск по имени на любом языке (русское, "nic", английское) — карточка заклинания в игре: имя у героя
    // (HeroSpell.Name) — каноничное название из справочника на языке игры.
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
