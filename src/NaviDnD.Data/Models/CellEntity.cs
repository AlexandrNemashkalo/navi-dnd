namespace NaviDnD.Data.Models;
public class CellEntity : IPatchable
{
    public bool? Deleted { get; set; }

    public string Symbol { get; set; }

    public List<int> Position { get; set; }

    public string Name { get; set; }

    public List<int> Color { get; set; }

    public LightSource? Light { get; set; }
    // public bool? Explored { get; set; }

    public bool? Hidden { get; set; }

    public CellTriggers? Triggers { get; set; }
    public List<string>? AiInfo { get; set; }
    public string? Image { get; set; }

    public List<StatusEffect>? Effects { get; set; }
}

public class CellTriggers
{
    public TriggerEffect? OnStep { get; set; }
    public TriggerEffect? OnVisible { get; set; }

    // Only on creatures. Triggers at the moment when the creature STARTS seeing the hero (entering the field of
    // view), if it is not in the initiative. Permanent: not removed after triggering - will trigger again at
    // the next meeting.
    public TriggerEffect? Spotted { get; set; }

    // Only on creatures. Once per round (via "Round N has begun") if the creature sees the hero.
    public TriggerEffect? OnRound { get; set; }
}

[System.Text.Json.Serialization.JsonConverter(typeof(LenientTriggerEffectConverter))]
public class TriggerEffect
{
    public string? Effect { get; set; }
    public bool? Once { get; set; }
}
