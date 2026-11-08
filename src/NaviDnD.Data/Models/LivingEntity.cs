namespace NaviDnD.Data.Models;

public class LivingEntity : CellEntity
{
    public string Hp { get; set; } = "";
    public int? SpeedMax { get; set; }
    public int? SpeedLeft { get; set; }
    /// <summary>
    ///Range of vision in feet to detect the hero.
    ///-1 = no limit, 0 = blind, null = standard 30ft, >0 = radius in feet (1 square = 5 ft).
    /// </summary>
    public int? VisionFt { get; set; }

    // Vision range in the dark. In an unlit cell, the creature sees the hero only within its confines. null -
    // not specified (darkness is not taken into account, as in old saves), 0 - cannot see in the dark.
    public int? DarkvisionFt { get; set; }

    // Link to a species in MonsterDatabase (GameData/DnD5e_monsters_BD.dtn), for example “Goblin” - NOT full
    // statblock (by analogy with HeroSpell.Name → SpellDatabase). The engine itself retrieves full stats from
    // the database using this key. Only for hostile creatures maps - the hero/NPC is not used.
    public string? MonsterKey { get; set; }
}
