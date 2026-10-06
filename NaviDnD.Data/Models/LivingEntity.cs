namespace NaviDnD.Data.Models;

public class LivingEntity : CellEntity
{
    public string Hp { get; set; } = "";
    public int? SpeedMax { get; set; }
    public int? SpeedLeft { get; set; }
    /// <summary>
    /// Дальность зрения в футах для обнаружения героя.
    /// -1 = без ограничений, 0 = слепой, null = стандарт 30ft, >0 = радиус в футах (1 клетка = 5 фут).
    /// </summary>
    public int? VisionFt { get; set; }

    // Дальность зрения в темноте. В неосвещённой клетке существо видит героя только в её пределах.
    // null — не задано (темнота не учитывается, как в старых сейвах), 0 — в темноте не видит.
    public int? DarkvisionFt { get; set; }

    // Ссылка на вид в MonsterDatabase (GameData/DnD5e_monsters_BD.dtn), например "Гоблин" — НЕ полный
    // statblock (по аналогии с HeroSpell.Name → SpellDatabase). Полные статы движок сам достаёт из
    // базы по этому ключу. Только для враждебных существ карты — у героя/NPC не используется.
    public string? MonsterKey { get; set; }
}
