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
