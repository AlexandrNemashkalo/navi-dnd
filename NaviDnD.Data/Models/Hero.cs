using System.Text.Json.Serialization;

namespace NaviDnD.Data.Models;

public class Hero : LivingEntity
{
    // Вдохновение (Heroic Inspiration) — не привязано ни к одной способности/классу, поэтому не
    // resource, а такое же статичное поле, как Hp/VisionFt (показывается в базовом блоке карточки).
    public bool? Inspiration { get; set; }

    // Итог проверки скрытности, пока герой прячется; null — не прячется. Движок только передаёт его
    // в контекст триггера spotted — сравнение с восприятием существ делает ИИ по правилам НРИ.
    public int? Stealth { get; set; }

    // Раунд, до конца которого движение героя не провоцирует атак по возможности (отход). Номер
    // раунда, а не bool — истекает сам при смене раунда, отдельный сброс не нужен.
    public int? NoOpportunityAttacksRound { get; set; }

    // Герой погиб (решает мастер по правилам НРИ) — игра окончена: ввод закрыт, только выход в меню.
    public bool? Dead { get; set; }

    // Хиты на нуле («0/12») — без сознания/при смерти: свой ход герой не делает, мастер ведёт его сам.
    [JsonIgnore]
    public bool IsDown => Hp is { Length: > 0 } hp && int.TryParse(hp.Split('/')[0].Trim(), out int cur) && cur <= 0;

    // Дефолт [] (не null) — защита от краша UI (HeroDisplay и т.п. читают эти списки без ?.),
    // если патч героя оборвался на середине (например ИИ прислал некорректный тип поля дальше по
    // JSON) и часть свойств из ApplyHero не успела примениться вообще.
    public List<HeroStat> Stats { get; set; } = [];

    public List<HeroInventory> Inventory { get; set; } = [];

    public List<string> EquipmentSlots { get; set; } = [];

    public List<HeroAbility> Abilities { get; set; } = [];

    public List<HeroResource> Resources { get; set; } = [];

    public List<HeroSkill> Skills { get; set; } = [];

    public List<HeroAction>? Actions { get; set; }

    public List<HeroSpell>? Spells { get; set; }
}

public class HeroInventory : IPatchable
{
    public bool? Deleted { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public int? Quantity { get; set; }

    public List<string>? EquipmentedSlot { get; set; }

    public List<int> Color { get; set; }

    public string? Image { get; set; }

    // Читаемый текст (записка/письмо/свиток) — если задан, панель картинки в UI показывает его
    // постранично вместо/вместе с Image, а не Description (та остаётся короткой строкой в списке).
    public string? Text { get; set; }

    public List<StatusEffect>? Effects { get; set; }
}

public class HeroStat : IPatchable
{
    public bool? Deleted { get; set; }

    public string Name { get; set; }

    [JsonConverter(typeof(LenientStringConverter))]
    public string Value { get; set; }
}

public class HeroAbility : IPatchable
{
    public bool? Deleted { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public List<int> Color { get; set; }
}

public class HeroResource : IPatchable
{
    public bool? Deleted { get; set; }

    public string Name { get; set; }

    [JsonConverter(typeof(LenientStringConverter))]
    public string Value { get; set; }

    // "Способности" или "Заклинания" — какая подвкладка карточки персонажа показывает этот ресурс
    // (HeroDisplay.DrawHeroCard). Ресурс всегда относится к одной из двух — общих (не привязанных
    // ни к чему) в 5e практически нет.
    public string? Category { get; set; }
}

public class HeroSkill : IPatchable
{
    public bool? Deleted { get; set; }

    public string Name { get; set; }

    [JsonConverter(typeof(LenientStringConverter))]
    public string Value { get; set; }
}

public class HeroAction : IPatchable
{
    public bool? Deleted { get; set; }

    public string Name { get; set; }

    public int Value { get; set; }

    public int MaxValue { get; set; }
}

public class HeroSpell : IPatchable
{
    public bool? Deleted { get; set; }

    public string Name { get; set; }

    // 0 = заговор (cantrip), 1-9 — уровень заклинания.
    public int Level { get; set; }

    public List<int> Color { get; set; }

    // Подготовлено ли заклинание (актуально для классов с подготовкой, напр. волшебник/жрец) —
    // показывается точкой в списке. Не подготовленное всё равно видно в списке, просто без точки.
    public bool? Prepared { get; set; }
}

