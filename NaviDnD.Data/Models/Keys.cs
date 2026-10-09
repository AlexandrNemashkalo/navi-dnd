namespace NaviDnD.Data.Models;

// Постоянные значения данных — не зависят от языка мира. Модель пишет слова на языке игры («Утро», «Morning»,
// «Класс Доспеха», «Armor Class») — Storage после каждого патча приводит их к ключам, миграция переводит старые
// сохранения. Код сравнивает только ключи; на экран и мастеру — подпись на нужном языке.

// Часть суток: 4 по кругу night → morning → day → evening.
public static class PartsOfDay
{
    public const string Night = "night", Morning = "morning", Day = "day", Evening = "evening";
    public static readonly string[] Cycle = [Night, Morning, Day, Evening];

    // Слово на любом языке → ключ; незнакомое (мастер придумал своё) — как есть.
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => Morning,
        "night" or "ночь" => Night,
        "morning" or "утро" => Morning,
        "day" or "день" or "afternoon" or "noon" => Day,
        "evening" or "вечер" => Evening,
        _ => value!.Trim(),
    };

    // Русская подпись (ключ перевода L): L.T — для экрана, L.W — мастеру и в историю.
    public static string Russian(string? key) => key switch
    {
        Night => "Ночь", Morning => "Утро", Day => "День", Evening => "Вечер", _ => key ?? "",
    };
}

// Особые характеристики героя (hero.stats), которые читает код. Остальные статы — без ключа, просто текст.
public static class StatKeys
{
    public const string Race = "race", Class = "class", Alignment = "alignment", Background = "background",
        ArmorClass = "armorClass", Darkvision = "darkvision", SpellAbility = "spellAbility", SpellSave = "spellSave",
        SpellAttack = "spellAttack", SpellsPrepared = "spellsPrepared", SpellsKnown = "spellsKnown";

    // Названия на обоих языках (без регистра). Длинные — раньше коротких: «Класс Доспеха» — не «Класс».
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

    // Ключ по названию стата: точное совпадение или название с уточнением в скобках («Класс Доспеха (щит)»).
    public static string? FromName(string? name)
    {
        string n = name?.Trim().ToLowerInvariant() ?? "";
        if (n.Length == 0) return null;
        foreach (var (key, synonym) in Names)
            if (n == synonym || n.StartsWith(synonym + " (") || n.StartsWith(synonym + "(")) return key;
        return null;
    }
}

// Стат «Класс» — только класс (и подкласс в скобках): уровень — поле hero.level. Мастер по старой привычке пишет
// «Воин 3 ур», «Wizard (level 2)» — Storage и миграция отрезают уровень.
public static class ClassLevel
{
    private static readonly System.Text.RegularExpressions.Regex Pattern = new(
        @"^(?<cls>.*?)[\s,(]*(?:(?:уровень|ур\.?|lvl\.?|level)\s*)?(?<n>\d{1,2})(?:\s*-?\s*(?:й|го))?\s*(?:уровня|уровень|ур\.?|lvl\.?|level)?\s*\)?\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly string[] ExperienceNames = ["опыт", "experience", "xp", "exp"];

    // Прежний стат «Опыт: 900/2700» — теперь поле hero.xp.
    public static bool IsExperienceStat(string? name) => ExperienceNames.Contains(name?.Trim().ToLowerInvariant());

    // «Воин (Чемпион) 3 ур» → («Воин (Чемпион)», 3); без уровня — (как есть, null).
    public static (string cls, int? level) Split(string? value)
    {
        string v = value?.Trim() ?? "";
        var m = Pattern.Match(v);
        return m.Success && m.Groups["cls"].Value.Trim().Length > 0
            ? (m.Groups["cls"].Value.Trim(), int.Parse(m.Groups["n"].Value))
            : (v, null);
    }
}

// Характеристики героя — обычные hero.stats без ключа («+2 (15)»): код находит их по названию на обоих языках
// (хиты и улучшение характеристик при повышении уровня).
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

    // «+2 (15)» → 15, «15» → 15; не разобрать — null.
    public static int? Score(HeroStat? stat)
    {
        string v = stat?.Value ?? "";
        var m = System.Text.RegularExpressions.Regex.Match(v, @"\((\d+)\)");
        if (m.Success) return int.Parse(m.Groups[1].Value);
        return int.TryParse(v.Trim(), out int n) ? n : null;
    }

    public static int Modifier(int score) => (int)Math.Floor((score - 10) / 2.0);

    public static string Signed(int n) => n >= 0 ? $"+{n}" : n.ToString();

    // Значение стата характеристики в формате героя: «+3 (17)».
    public static string Format(int score) => $"{Signed(Modifier(score))} ({score})";
}

// Размер и тип существа героя (обычные статы без ключа) — карточка показывает их одной строкой.
public static class CreatureNames
{
    public static bool IsSize(string? name) => name?.Trim().ToLowerInvariant() is "размер" or "size";
    public static bool IsType(string? name) => name?.Trim().ToLowerInvariant() is "тип существа" or "тип" or "creature type" or "type";
}

// Подвкладка карточки героя, где показывается ресурс (hero.resources[].category).
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

// Навыки героя: код ищет только Восприятие (пассивное восприятие).
public static class SkillNames
{
    public static bool IsPerception(string? name) =>
        name != null && (name.Contains("Восприятие", StringComparison.OrdinalIgnoreCase) || name.Contains("Perception", StringComparison.OrdinalIgnoreCase));
}
