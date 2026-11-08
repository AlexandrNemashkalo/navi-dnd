namespace NaviDnD.Data.Models;

// Stable data keys do not depend on the world language. The model uses localized names (such as Morning or
// Armor Class); Storage normalizes them to keys after each patch, and migration converts old saves.
// Code compares keys; the UI and GM receive labels in the appropriate language.

// Part of the day: 4 in a circle night → morning → day → evening.
public static class PartsOfDay
{
    public const string Night = "night", Morning = "morning", Day = "day", Evening = "evening";
    public static readonly string[] Cycle = [Night, Morning, Day, Evening];

    // Word in any language → key; unfamiliar (the GM came up with his own) - as it is.
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => Morning,
        "night" or "ночь" => Night,
        "morning" or "утро" => Morning,
        "day" or "день" or "afternoon" or "noon" => Day,
        "evening" or "вечер" => Evening,
        _ => value!.Trim(),
    };

    // English label (translation key L): L.T - for the screen, L.W - for the GM and for history.
    public static string Label(string? key) => key switch
    {
        Night => "Night", Morning => "Morning", Day => "Day", Evening => "Evening", _ => key ?? "",
    };
}

// Special characteristics of the hero (hero.stats), which the code reads. The rest of the stats are without a key, just text.
public static class StatKeys
{
    public const string Race = "race", Class = "class", Alignment = "alignment", Background = "background",
        ArmorClass = "armorClass", Darkvision = "darkvision", SpellAbility = "spellAbility", SpellSave = "spellSave",
        SpellAttack = "spellAttack", SpellsPrepared = "spellsPrepared", SpellsKnown = "spellsKnown";

    // Names in both languages (no case). Long ones come before short ones: “Armor Class” is not “Class”.
    private static readonly (string key, string name)[] Names = new (string key, string[] names)[]
    {
        (ArmorClass, ["класс доспеха", "класс защиты", "кд", "armor class", "armour class", "ac"]),
        (Darkvision, ["тёмное зрение", "темное зрение", "тёмное зрение (darkvision)", "darkvision"]),
        (SpellAbility, ["заклинательная характеристика", "базовая характеристика заклинаний", "spellcasting ability"]),
        (SpellSave, ["спасбросок заклинания", "сл спасброска заклинаний", "сл заклинаний", "spell save dc", "spell save"]),
        (SpellAttack, ["атака заклинанием", "бонус атаки заклинанием", "spell attack bonus", "spell attack"]),
        (SpellsPrepared, ["подготовлено заклинаний", "подготовленные заклинания", "spells prepared", "prepared spells"]),
        (SpellsKnown, ["известно заклинаний", "известные заклинания", "spells known", "known spells"]),
        (Alignment, ["мировоззрение", "alignment"]),
        (Background, ["предыстория", "background"]),
        (Race, ["раса", "race", "species"]),
        (Class, ["класс", "class"]),
    }.SelectMany(k => k.names.Select(n => (k.key, n))).OrderByDescending(p => p.n.Length).ToArray();

    // Key by stat name: exact match or name with clarification in brackets (“Armor Class (shield)”).
    public static string? FromName(string? name)
    {
        string n = name?.Trim().ToLowerInvariant() ?? "";
        if (n.Length == 0) return null;
        foreach (var (key, synonym) in Names)
            if (n == synonym || n.StartsWith(synonym + " (") || n.StartsWith(synonym + "(")) return key;
        return null;
    }
}

// Stat “Class” - only class (and subclass in brackets): level - field hero.level. The GM, according to the old
// habit, writes “Warrior 3 lvl”, “Wizard (level 2)” - Storage and migration cuts off the level.
public static class ClassLevel
{
    private static readonly System.Text.RegularExpressions.Regex Pattern = new(
        @"^(?<cls>.*?)[\s,(]*(?:(?:уровень|ур\.?|lvl\.?|level)\s*)?(?<n>\d{1,2})(?:\s*-?\s*(?:й|го))?\s*(?:уровня|уровень|ур\.?|lvl\.?|level)?\s*\)?\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly string[] ExperienceNames = ["опыт", "experience", "xp", "exp"];

    // The previous stat “Experience: 900/2700” is now the field hero.xp.
    public static bool IsExperienceStat(string? name) => ExperienceNames.Contains(name?.Trim().ToLowerInvariant());

    // “Warrior (Champion) lvl 3” → (“Warrior (Champion)”, 3); without level - (as is, null).
    public static (string cls, int? level) Split(string? value)
    {
        string v = value?.Trim() ?? "";
        var m = Pattern.Match(v);
        return m.Success && m.Groups["cls"].Value.Trim().Length > 0
            ? (m.Groups["cls"].Value.Trim(), int.Parse(m.Groups["n"].Value))
            : (v, null);
    }
}

// Hero stats are regular hero.stats without a key (“+2 (15)”): the code finds them by name in both languages
// (hits and stat improvements when leveling up).
public static class AbilityNames
{
    public const string Str = "str", Dex = "dex", Con = "con", Int = "int", Wis = "wis", Cha = "cha";
    public static readonly string[] All = [Str, Dex, Con, Int, Wis, Cha];

    private static readonly Dictionary<string, string[]> Names = new()
    {
        [Str] = ["сила", "strength"], [Dex] = ["ловкость", "dexterity"], [Con] = ["телосложение", "constitution"],
        [Int] = ["интеллект", "intelligence"], [Wis] = ["мудрость", "wisdom"], [Cha] = ["харизма", "charisma"],
    };

    private static readonly string[] ProficiencyNames = ["бонус мастерства", "proficiency bonus"];

    public static HeroStat? Find(Hero hero, string ability) => FindByNames(hero, Names[ability]);

    public static HeroStat? Proficiency(Hero hero) => FindByNames(hero, ProficiencyNames);

    private static HeroStat? FindByNames(Hero hero, string[] names) =>
        hero.Stats?.FirstOrDefault(s => s.Deleted != true && names.Contains(s.Name?.Trim().ToLowerInvariant()));

    // “+2 (15)” → 15, “15” → 15; can't make it out - null.
    public static int? Score(HeroStat? stat)
    {
        string v = stat?.Value ?? "";
        var m = System.Text.RegularExpressions.Regex.Match(v, @"\((\d+)\)");
        if (m.Success) return int.Parse(m.Groups[1].Value);
        return int.TryParse(v.Trim(), out int n) ? n : null;
    }

    public static int Modifier(int score) => (int)Math.Floor((score - 10) / 2.0);

    public static string Signed(int n) => n >= 0 ? $"+{n}" : n.ToString();

    // Characteristic stat value in hero format: “+3 (17).”
    public static string Format(int score) => $"{Signed(Modifier(score))} ({score})";
}

// The size and type of the hero's creature (regular stats without a key) - the card shows them in one line.
public static class CreatureNames
{
    public static bool IsSize(string? name) => name?.Trim().ToLowerInvariant() is "размер" or "size";
    public static bool IsType(string? name) => name?.Trim().ToLowerInvariant() is "тип существа" or "тип" or "creature type" or "type";
}

// Subtab of the hero card, where the resource is shown (hero.resources[].category).
public static class ResourceCategories
{
    public const string Abilities = "abilities", Spells = "spells";

    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "способности" or "умения" or "abilities" or "features" or "feature" or "ability" => Abilities,
        "заклинания" or "spells" or "spell" or "magic" => Spells,
        _ => value!.Trim(),
    };
}

// Hero Skills: The code only looks for Perception (passive perception).
public static class SkillNames
{
    public static bool IsPerception(string? name) =>
        name != null && (name.Contains("Восприятие", StringComparison.OrdinalIgnoreCase) || name.Contains("Perception", StringComparison.OrdinalIgnoreCase));
}
