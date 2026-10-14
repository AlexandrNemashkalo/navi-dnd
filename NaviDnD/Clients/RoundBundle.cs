using NaviDnD.Data.Models;

namespace NaviDnD;

// OwnerPath: "hero" or "map.entities". EntityId: index in map.entities (null for hero). EffectId: index of the
// effect within the owner's list Effects - used for patching by id.
public record EffectRef(string OwnerPath, int? EntityId, string OwnerName, int EffectId, StatusEffect Effect);

public class RoundBundle
{
    public int NewRound { get; init; }
    public List<EffectRef> ExpiredEffects { get; init; } = [];
    public List<EffectRef> OnRoundEffects { get; init; } = [];
    public List<(int id, ScheduledEvent ev)> FiredScheduledEvents { get; init; } = [];
    public List<(int areaId, Area area)> AreaRoundTriggers { get; init; } = [];
    public List<(int entityId, LivingEntity entity)> EntityRoundTriggers { get; init; } = [];

    public bool HasAny =>
        ExpiredEffects.Count > 0 || OnRoundEffects.Count > 0 ||
        FiredScheduledEvents.Count > 0 || AreaRoundTriggers.Count > 0 || EntityRoundTriggers.Count > 0;
}
