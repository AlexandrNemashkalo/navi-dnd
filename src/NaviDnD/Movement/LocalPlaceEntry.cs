using NaviDnD.Data.Models;

namespace NaviDnD;

// Enter a known place already under the hero's world-map marker; this is not another journey.
public static class LocalPlaceEntry
{
    public static WorldPlace? Find(WorldState state, (int x, int y)? target) => Find(state, target, GameWorld.ForMaster(state));

    // UI selection uses the already composed visible world without mutating the hero or link.
    public static WorldPlace? Find(WorldState state, (int x, int y)? target, WorldMap? world)
    {
        if (state.Hero == null || state.World == null || target is not { } cell
            || GameWorld.HeroTile(state) != cell || state.Combat?.Active == true || GameWorld.HasLocation(state)
            || world == null) return null;
        var places = world.Places.Where(p => !p.Hidden && !string.IsNullOrWhiteSpace(p.Name)
            && (p.X, p.Y) == cell).Take(2).ToList();
        if (places.Count != 1) return null;
        var place = places[0];
        return place.Name.Equals(state.World.Place, StringComparison.OrdinalIgnoreCase)
            || place.FormerNames.Contains(state.World.Place ?? "", StringComparer.OrdinalIgnoreCase) ? null : place;
    }

    public static bool TryEnter(WorldState state, (int x, int y)? target, out string report)
    {
        report = "";
        if (Find(state, target) is not { } place) return false;
        var link = state.World!;
        link.Place = place.Name;
        if (!link.Visited.Contains(place.Name, StringComparer.OrdinalIgnoreCase)) link.Visited.Add(place.Name);
        link.Version++;
        bool archived = GameWorld.Geo(state) is { } geo && GameWorld.HasSavedLocation(geo, place.Name);
        report = "## LOCAL ENTRY\nThe player explicitly chose to approach this known place on their current world-map cell. "
            + "The engine already set world.place and marked the visit. No travel time, food, rest, effects, or scheduled events were spent or advanced; do not charge them, roll a travel encounter, or change world coordinates.\n"
            + $"Local arrival: {place.Name} — {WorldPlaceTypes.Label(place.Type)}. {place.Description}\n"
            + (archived
                ? "This place has an archived location. Use its restored map; never regenerate or replace it. "
                : "No tactical map is loaded. Describe the immediate approach; call plan_location only if exploration or tactics needs it. ")
            + "Briefly describe arrival and await the player's next choice; entry does not accept a quest or resolve the scene.";
        return true;
    }
}
