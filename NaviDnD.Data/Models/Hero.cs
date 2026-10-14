using System.Text.Json.Serialization;

namespace NaviDnD.Data.Models;

public class Hero : LivingEntity
{
    // Inspiration (Heroic Inspiration) - not tied to any ability/class, so not resource, but the same static
    // field as Hp/VisionFt (shown in the base block of the card).
    public bool? Inspiration { get; set; }

    // Result of the stealth check while the hero is hiding; null - does not hide. The engine only transfers it
    // to the context of the spotted trigger - the comparison with the perception of creatures is done by the AI
    // according to the TTRPG rules.
    public int? Stealth { get; set; }

    // A round until the end of which the hero's movement does not provoke attacks of opportunity (withdrawal).
    // The round number, not bool, expires on its own when the round is changed, a separate reset is not needed.
    public int? NoOpportunityAttacksRound { get; set; }

    // The hero died (the GM decides according to the TTRPG rules) - the game is over: input is closed, only exit to the menu.
    public bool? Dead { get; set; }

    // The level and experience are controlled by the engine (LevelRules): the GM assigns experience with the
    // xpAward patch, the level increases only in the “New Level” window by the player’s choice. From the GM’s
    // answer, level/xp is not applied - only from saving (Storage). null - 1st level / 0 experience.
    public int? Level { get; set; }

    public int? Xp { get; set; }

    // Started level increase (New Level window): GM options and hit rolls are saved so that canceling, exiting
    // to the menu or restarting does not ask the AI again and does not allow you to roll hit points. Only the
    // engine.
    public LevelUpDraft? LevelUpDraft { get; set; }

    // Hit points at zero (“0/12”) - unconscious/near death: the hero does not make his move, the GM leads it himself.
    [JsonIgnore]
    public bool IsDown => Hp is { Length: > 0 } hp && int.TryParse(hp.Split('/')[0].Trim(), out int cur) && cur <= 0;

    // Default [] (not null) - crash protection for UI (HeroDisplay, etc. read these lists without ?.), if the
    // hero's patch was interrupted in the middle (for example, the AI sent an incorrect field type further
    // along JSON) and some of the properties from ApplyHero did not have time to be applied at all.
    public List<HeroStat> Stats { get; set; } = [];

    public List<HeroInventory> Inventory { get; set; } = [];

    public List<string> EquipmentSlots { get; set; } = [];

    public List<HeroAbility> Abilities { get; set; } = [];

    public List<HeroResource> Resources { get; set; } = [];

    public List<HeroSkill> Skills { get; set; } = [];

    public List<HeroAction>? Actions { get; set; }

    public List<HeroSpell>? Spells { get; set; }

    // Special stat by key (StatKeys) or null.
    public HeroStat? Stat(string key) => Stats?.FirstOrDefault(s => s.Deleted != true && s.Key == key);
}

public class LevelUpDraft
{
    // At what level (a draft of another level is not suitable).
    public int Level { get; set; }

    // Wizard's answer with options (along with those added at the request of the player) - JSON LevelUp.LoadPlan.
    public string? Plan { get; set; }

    // Hit dice rolled (roll one).
    public int? HpDie { get; set; }
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

    // Readable text (note/letter/scroll) - if set, the picture panel in UI shows it page by page instead
    // of/together with Image, rather than Description (which remains a short string in the list).
    public string? Text { get; set; }

    public List<StatusEffect>? Effects { get; set; }
}

public class HeroStat : IPatchable
{
    public bool? Deleted { get; set; }

    // Special stat key (StatKeys: race, class, armorClass...) - the code finds it using it; Storage puts it by
    // name in any language. The rest of the stats are null.
    public string? Key { get; set; }

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

    // ResourceCategories.Abilities or .Spells - which subtab of the character card shows this resource
    // (HeroDisplay.DrawHeroCard); the word of the GM Storage leads to the key. The resource always refers to
    // one of two - there are practically no general ones (not tied to anything) in 5e.
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

    // 0 = spell (cantrip), 1-9 - spell level.
    public int Level { get; set; }

    public List<int> Color { get; set; }

    // Whether the spell has been prepared (relevant for classes with preparation, eg wizard/priest) is shown as
    // a dot in the list. The unprepared one is still visible in the list, just without a dot.
    public bool? Prepared { get; set; }
}

