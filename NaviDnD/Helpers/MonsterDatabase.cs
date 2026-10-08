using System.Text.Json;

namespace NaviDnD.Helpers;

public class MonsterInfo
{
    public required string Name { get; init; }
    public string NameEn { get; init; } = "";
    public string Size { get; init; } = "";
    public string Type { get; init; } = "";
    public string Alignment { get; init; } = "";
    public string Ac { get; init; } = "";
    public string Hp { get; init; } = "";
    public string Speed { get; init; } = "";
    public string Str { get; init; } = "";
    public string Dex { get; init; } = "";
    public string Con { get; init; } = "";
    public string Int { get; init; } = "";
    public string Wis { get; init; } = "";
    public string Cha { get; init; } = "";
    public string Save { get; init; } = "";
    public string Skill { get; init; } = "";
    public string Senses { get; init; } = "";
    public string Passive { get; init; } = "";
    public string Languages { get; init; } = "";
    public string Cr { get; init; } = "";
    public string Vulnerable { get; init; } = "";
    public string Resist { get; init; } = "";
    public string Immune { get; init; } = "";
    public string ConditionImmune { get; init; } = "";
    public string Spells { get; init; } = "";
    public List<(string Name, string Text)> Traits { get; init; } = [];
    public List<(string Name, string Text)> Actions { get; init; } = [];
    public List<(string Name, string Text)> Reactions { get; init; } = [];
    public string LegendaryIntro { get; init; } = "";
    public List<(string Name, string Text)> Legendary { get; init; } = [];

    // Название на языке игры (L.World); статья справочника — только по-русски.
    public string LocalizedName => L.WorldIsEnglish && NameEn.Length > 0 ? NameEn : Name;
}

// Справочник монстров DnD5e (GameData/DnD5e_monsters_BD.dtn). По аналогии с SpellDatabase: файл
// читается и парсится один раз за процесс (Lazy), дальше только поиск по кэшу. В отличие от
// заклинаний, ru/en имя тут не разделены — одно поле "Гоблин (Goblin)", парсим сами. "trait"/
// "action" в исходнике то объект, то массив объектов — нормализуем к списку в обоих случаях.
// Урезанный набор полей — механика боя + краткое описание, без "fiction" (лор), "legendary"/"lair"
// (боссовая механика, пока не нужна).
public static class MonsterDatabase
{
    private static readonly string FilePath =
        Path.Combine(AppConfig.AssetDirectory("GameData"), "DnD5e_monsters_BD.dtn");

    private static readonly Lazy<Dictionary<string, MonsterInfo>> ByName = new(LoadByName);
    private static readonly Lazy<List<MonsterInfo>> All = new(() => ByName.Value.Values.Distinct().ToList());

    public static IReadOnlyList<MonsterInfo> AllMonsters => All.Value;

    // Точный поиск по имени на любом языке (monsterKey мастер пишет на языке игры) — полные статы для карты.
    public static MonsterInfo? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return ByName.Value.GetValueOrDefault(Normalize(name));
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();

    private static Dictionary<string, MonsterInfo> LoadByName()
    {
        var result = new Dictionary<string, MonsterInfo>();
        if (!File.Exists(FilePath)) return result;

        using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
        if (!doc.RootElement.TryGetProperty("dataList", out var monsters)) return result;

        foreach (var entry in monsters.EnumerateArray())
        {
            string rawName = GetStr(entry, "name");
            if (rawName.Length == 0) continue;

            var (nameRu, nameEn) = SplitName(rawName);

            var info = new MonsterInfo
            {
                Name = nameRu,
                NameEn = nameEn,
                Size = GetStr(entry, "size"),
                Type = GetStr(entry, "type"),
                Alignment = GetStr(entry, "alignment"),
                Ac = GetStr(entry, "ac"),
                Hp = GetStr(entry, "hp"),
                Speed = GetStr(entry, "speed"),
                Str = GetStr(entry, "str"),
                Dex = GetStr(entry, "dex"),
                Con = GetStr(entry, "con"),
                Int = GetStr(entry, "int"),
                Wis = GetStr(entry, "wis"),
                Cha = GetStr(entry, "cha"),
                Save = GetStr(entry, "save"),
                Skill = GetStr(entry, "skill"),
                Senses = GetStr(entry, "senses"),
                Passive = GetStr(entry, "passive"),
                Languages = GetStr(entry, "languages"),
                Cr = GetStr(entry, "cr"),
                Vulnerable = GetStr(entry, "vulnerable"),
                Resist = GetStr(entry, "resist"),
                Immune = GetStr(entry, "immune"),
                ConditionImmune = GetStr(entry, "conditionImmune"),
                Spells = GetStr(entry, "spells"),
                Traits = ReadNamedList(entry, "trait"),
                Actions = ReadNamedList(entry, "action"),
                Reactions = ReadNamedList(entry, "reaction"),
                // "legendary": { text: вступление, list: [{name,text}] }
                LegendaryIntro = entry.TryGetProperty("legendary", out var leg) && leg.ValueKind == JsonValueKind.Object
                    ? GetStr(leg, "text") : "",
                Legendary = entry.TryGetProperty("legendary", out var leg2) && leg2.ValueKind == JsonValueKind.Object
                    ? ReadNamedList(leg2, "list") : [],
            };

            result.TryAdd(Normalize(nameRu), info);
            if (nameEn.Length > 0) result.TryAdd(Normalize(nameEn), info);
        }
        return result;
    }

    // "Гоблин (Goblin)" → ("Гоблин", "Goblin"). Нет скобок (редкие статьи без перевода) — en пустой.
    private static (string ru, string en) SplitName(string rawName)
    {
        int open = rawName.IndexOf('(');
        int close = rawName.LastIndexOf(')');
        if (open <= 0 || close <= open) return (rawName.Trim(), "");
        string ru = rawName[..open].Trim();
        string en = rawName[(open + 1)..close].Trim();
        return (ru, en);
    }

    // "trait"/"action" в исходнике — то один объект {name,text}, то массив таких объектов.
    private static List<(string Name, string Text)> ReadNamedList(JsonElement parent, string prop)
    {
        var result = new List<(string, string)>();
        if (!parent.TryGetProperty(prop, out var val)) return result;

        if (val.ValueKind == JsonValueKind.Object)
        {
            AddNamedEntry(result, val);
        }
        else if (val.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in val.EnumerateArray())
                AddNamedEntry(result, item);
        }
        return result;
    }

    private static void AddNamedEntry(List<(string, string)> result, JsonElement el)
    {
        string name = GetStr(el, "name");
        string text = ReadTextField(el);
        if (name.Length == 0 && text.Length == 0) return;
        result.Add((name, text));
    }

    // "text" тоже то строка, то массив строк (несколько абзацев) — склеиваем через перенос строки.
    private static string ReadTextField(JsonElement el)
    {
        if (!el.TryGetProperty("text", out var text)) return "";
        if (text.ValueKind == JsonValueKind.String) return text.GetString() ?? "";
        if (text.ValueKind == JsonValueKind.Array)
            return string.Join("\n", text.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? ""));
        return "";
    }

    private static string GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
