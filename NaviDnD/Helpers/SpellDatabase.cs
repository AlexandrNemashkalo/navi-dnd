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

    // Точный поиск по русскому имени (или "nic") — используется карточкой заклинания в игре, где
    // имя у героя (HeroSpell.Name) должно совпадать с каноничным русским названием буквально.
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

            string nameEn = entry.TryGetProperty("en", out var en) ? GetStr(en, "name") : "";

            var info = new SpellInfo
            {
                Name = name,
                NameEn = nameEn,
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
        }
        return result;
    }

    private static string GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
