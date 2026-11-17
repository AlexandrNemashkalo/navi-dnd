using NaviDnD.Data.Models;

namespace NaviDnD.Helpers;

// Retain identity unless an explicit character fact contradicts the bundled portrait metadata.
public static class NpcPortraits
{
    public static NpcRecord? Find(WorldState world, string? name) => world.Narrative?.Npcs
        .FirstOrDefault(n => n.Deleted != true && string.Equals(n.Name?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static void Assign(WorldState world)
    {
        if (ArtPictures.Current is not { } art) return;
        var npcs = world.Narrative?.Npcs ?? [];
        var used = npcs.Where(n => !string.IsNullOrEmpty(n.Portrait)).Select(n => n.Portrait!).ToHashSet(StringComparer.Ordinal);
        // A face can already belong to a map NPC before the journal learns their name.
        foreach (var entity in world.Map?.Entities ?? [])
            if (art.IsPortrait(entity.Image)) used.Add(entity.Image!);
        if (world.Hero?.Image is { } hero) used.Add(hero);
        foreach (var npc in npcs.Where(n => n.Deleted != true && !string.IsNullOrWhiteSpace(n.Name)))
        {
            var entity = world.Map?.Entities?.FirstOrDefault(e => e.Deleted != true && string.Equals(e.Name?.Trim(), npc.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            string? gender = NpcGenders.Known(npc.Gender) ?? NpcGenders.Known(entity?.Gender);
            if (!string.IsNullOrEmpty(npc.Portrait) && !art.PortraitNeedsGenderUpdate(npc.Portrait, gender)) continue;
            var same = npcs.FirstOrDefault(n => n != npc && n.Deleted != true && n.Portrait != null
                && !art.PortraitNeedsGenderUpdate(n.Portrait, gender)
                && string.Equals(n.Name?.Trim(), npc.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            npc.Portrait = same?.Portrait ?? art.ChooseNpcPortrait(npc.Name, gender, npc.Portrait ?? entity?.Image, used);
            if (npc.Portrait != null) used.Add(npc.Portrait);
        }
        // Map NPCs can precede their journal record. Persist the face in their existing image field.
        foreach (var entity in world.Map?.Entities ?? [])
        {
            if (entity.Deleted == true || !string.IsNullOrEmpty(entity.MonsterKey) || string.IsNullOrWhiteSpace(entity.Name)) continue;
            var record = Find(world, entity.Name);
            string? gender = NpcGenders.Known(record?.Gender) ?? NpcGenders.Known(entity.Gender);
            string? face = record?.Portrait ?? art.ChooseNpcPortrait(entity.Name, gender, entity.Image, used);
            if (face == null) continue;
            entity.Image = face;
            used.Add(face);
        }
    }
}
