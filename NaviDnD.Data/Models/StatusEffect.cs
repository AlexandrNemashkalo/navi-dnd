namespace NaviDnD.Data.Models;

public class StatusEffect : IPatchable
{
    public bool? Deleted { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public List<int>? Color { get; set; }

    // null = not limited by rounds (see UntilLongRest/ unlimited)
    public int? ExpiresAtRound { get; set; }

    // true = removed during a long rest (the engine does not track this - the AI itself removes the effect when
    // a long rest is indicated)
    public bool? UntilLongRest { get; set; }

    public TriggerEffect? OnRound { get; set; }
    public TriggerEffect? OnExpire { get; set; }
}
