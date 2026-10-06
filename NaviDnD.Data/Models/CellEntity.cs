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

    // Только у существ. Срабатывает в момент, когда существо НАЧИНАЕТ видеть героя (вход в поле
    // зрения), если оно не в инициативе. Постоянный: не снимается после срабатывания — при
    // следующей встрече сработает снова.
    public TriggerEffect? Spotted { get; set; }

    // Только у существ. Раз в раунд (через «Начался раунд N»), если существо видит героя.
    public TriggerEffect? OnRound { get; set; }
}

[System.Text.Json.Serialization.JsonConverter(typeof(LenientTriggerEffectConverter))]
public class TriggerEffect
{
    public string? Effect { get; set; }
    public bool? Once { get; set; }
}
