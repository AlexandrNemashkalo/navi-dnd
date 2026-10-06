namespace NaviDnD.Data.Models;

public class StatusEffect : IPatchable
{
    public bool? Deleted { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public List<int>? Color { get; set; }

    // null = не ограничено раундами (см. UntilLongRest/бессрочно)
    public int? ExpiresAtRound { get; set; }

    // true = снимается на долгом отдыхе (движок это не отслеживает — ИИ сам убирает эффект,
    // когда повествует долгий отдых)
    public bool? UntilLongRest { get; set; }

    public TriggerEffect? OnRound { get; set; }
    public TriggerEffect? OnExpire { get; set; }
}
