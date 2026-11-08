using NaviDnD.Data.Models;

namespace NaviDnD.Helpers;

// Assign once from the bundled face gallery, without model calls or changes to map symbols.
public static class NpcPortraits
{
    public static NpcRecord? Find(WorldState world, string? name) => world.Narrative?.Npcs
        .FirstOrDefault(n => n.Deleted != true && string.Equals(n.Name?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static void Assign(WorldState world)
    {
        if (ArtPictures.Current is not { } art) return;
        var npcs = world.Narrative?.Npcs ?? [];
        var used = npcs.Where(n => !string.IsNullOrEmpty(n.Portrait)).Select(n => n.Portrait!).ToHashSet(StringComparer.Ordinal);
        if (world.Hero?.Image is { } hero) used.Add(hero);
        foreach (var npc in npcs.Where(n => n.Deleted != true && !string.IsNullOrWhiteSpace(n.Name)))
        {
            if (!string.IsNullOrEmpty(npc.Portrait)) continue;
            var same = npcs.FirstOrDefault(n => n.Portrait != null && string.Equals(n.Name?.Trim(), npc.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            var entity = world.Map?.Entities?.FirstOrDefault(e => e.Deleted != true && string.Equals(e.Name?.Trim(), npc.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            npc.Portrait = same?.Portrait ?? art.ChooseNpcPortrait(npc.Name, npc.Gender, entity?.Image, used);
            if (npc.Portrait != null) used.Add(npc.Portrait);
        }
        // Map NPCs can precede their journal record. Persist the face in their existing image field.
        foreach (var entity in world.Map?.Entities ?? [])
        {
            if (entity.Deleted == true || !string.IsNullOrEmpty(entity.MonsterKey) || string.IsNullOrWhiteSpace(entity.Name)) continue;
            string? face = Find(world, entity.Name)?.Portrait ?? art.ChooseNpcPortrait(entity.Name, null, entity.Image, used);
            if (face == null) continue;
            entity.Image = face;
            used.Add(face);
        }
    }
}
