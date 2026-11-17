using System.Text.Json;
using NaviDnD.Data.Models;

namespace NaviDnD;

// Restore a location as a snapshot, with the arrival position resolved before Storage repairs occupancy.
public static class LocationRestoration
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string SerializeSnapshot(MapConfig map, GameTime time)
    {
        var snapshot = JsonSerializer.SerializeToNode(map, SnapshotOptions)!.AsObject();
        snapshot["snapshotTime"] = JsonSerializer.SerializeToNode(time, SnapshotOptions);
        return snapshot.ToJsonString(SnapshotOptions);
    }

    public static bool CanRetryAtTarget(WorldState state, (int x, int y)? target) =>
        state.Combat?.Active != true && !GameWorld.HasLocation(state)
        && (target == null || target == GameWorld.HeroTile(state))
        && state.World?.Place is { Length: > 0 } name && GameWorld.Geo(state) is { } world
        && GameWorld.HasSavedLocation(world, name);

    public static bool TryRestoreCurrentPlace(Storage storage)
    {
        var state = storage.WorldState;
        if (state.Combat?.Active == true || GameWorld.HasLocation(state) || state.World?.Place is not { Length: > 0 } name
            || GameWorld.Geo(state) is not { } world || GameWorld.SavedLocationJson(world, name) is not { } json)
            return false;
        return TryRestore(storage, json);
    }

    public static bool TryRestore(Storage storage, string json)
    {
        MapConfig? map;
        GameTime? savedTime;
        try
        {
            using var snapshot = JsonDocument.Parse(json);
            if (snapshot.RootElement.ValueKind != JsonValueKind.Object
                || !snapshot.RootElement.TryGetProperty("snapshotTime", out var clock)) return false;
            if (clock.ValueKind != JsonValueKind.Null
                && (clock.ValueKind != JsonValueKind.Object
                    || !clock.TryGetProperty("day", out _)
                    || !clock.TryGetProperty("partOfDay", out _)
                    || !clock.TryGetProperty("totalRounds", out _))) return false;
            savedTime = clock.ValueKind == JsonValueKind.Null ? null : clock.Deserialize<GameTime>(Options);
            map = snapshot.RootElement.Deserialize<MapConfig>(Options);
        }
        catch (JsonException) { return false; }
        if (map == null || !HasUsableStructure(map) || FindArrival(map) is not { } arrival) return false;
        if (savedTime != null && !RebaseEffects(map, savedTime, storage.WorldState.Time)) return false;

        var patch = JsonSerializer.Serialize(new { map, hero = new { position = new[] { arrival.col, arrival.row } } });
        storage.SelectExplorationLocation();
        GameWorld.ClearMap(storage.WorldState.Map);
        storage.ApplySavedState(patch);
        storage.SelectExplorationLocation();
        return true;
    }

    private static long? AbsoluteRound(GameTime time)
    {
        if (string.IsNullOrWhiteSpace(time.PartOfDay)) return null;
        int part = Array.IndexOf(PartsOfDay.Cycle, PartsOfDay.Normalize(time.PartOfDay));
        return part < 0 || time.Day < 1 || time.TotalRounds < 1 ? null
            : ((long)time.Day - 1) * 14400 + part * 3600L + time.TotalRounds - 1;
    }

    private static bool RebaseEffects(MapConfig map, GameTime saved, GameTime current)
    {
        if (AbsoluteRound(saved) is not { } then || AbsoluteRound(current) is not { } now) return false;
        // Shared worlds can be revisited by a hero whose calendar is earlier: freeze remaining duration,
        // never grow it or replay every missed round as an off-screen simulation.
        long elapsed = Math.Max(0, now - then);
        foreach (var owner in (map.Entities ?? []).Cast<CellEntity>().Concat(map.Objects ?? []).Where(e => e.Deleted != true))
            foreach (var effect in owner.Effects ?? [])
                if (effect.Deleted != true && effect.ExpiresAtRound is int deadline)
                {
                    effect.ExpiresAtRound = (int)Math.Clamp((long)current.TotalRounds + deadline - saved.TotalRounds - elapsed, 0, int.MaxValue);
                    if (current.TotalRounds > effect.ExpiresAtRound && effect.OnExpire == null) effect.Deleted = true;
                }
        return true;
    }

    // Deserialization accepts null elements even in non-nullable lists; map indexes cannot use them.
    // Reject an incomplete archive rather than silently deleting its contents or clearing the current map.
    private static bool HasUsableStructure(MapConfig map)
    {
        static bool HasNull<T>(IEnumerable<T>? values) => values?.Any(value => value is null) == true;
        if (map.Colors == null || HasNull(map.Chunks) || HasNull(map.Rooms) || HasNull(map.Doors)
            || HasNull(map.Area) || HasNull(map.Entities) || HasNull(map.Objects) || HasNull(map.Furniture)
            || HasNull(map.Stairs) || HasNull(map.TriggerCells)) return false;
        return !(map.Chunks?.Any(c => c.Exits == null || HasNull(c.Exits) || HasNull(c.Terrain)) == true
            || map.Rooms?.Any(r => HasNull(r.Positions)) == true
            || map.Area?.Any(a => HasNull(a.Positions)) == true
            || map.Furniture?.Any(f => HasNull(f.Positions)) == true
            || map.Entities?.Any(e => HasNull(e.Effects)) == true
            || map.Objects?.Any(o => HasNull(o.Effects)) == true);
    }

    private static (int col, int row)? FindArrival(MapConfig map)
    {
        var state = new WorldState { Map = map };
        bool OnMap(int c, int r) => c >= 1 && c <= map.Cols && r >= 1 && r <= map.Rows
            && map.TerrainAt(c, r) is not { BlocksMove: true }
            && (map.TerrainAt(c, r) != null || map.RoomAt(c, r) != null || map.AreasAt(c, r).Any(a => a.Deleted != true));
        bool Free(int c, int r) => OnMap(c, r)
            && !(map.Entities ?? []).Any(e => e.Deleted != true && e.Position is { Count: >= 2 } p && p[0] == c && p[1] == r);
        var entries = (map.Chunks ?? []).SelectMany(c => c.Exits.Where(e => e.External).Select(e => e.AnchorCell(c)))
            .Concat((map.Doors ?? []).Where(d => d.IsWorldExit == true || d.IsEntry == true)
                .SelectMany(d => new[] { d.From, d.To }).Where(p => p is { Count: >= 2 }).Select(p => (p[0], p[1])))
            .Distinct().ToList();

        // Older room/area maps may have no explicit exit metadata.
        if (entries.Count == 0)
            entries = (from r in Enumerable.Range(1, map.Rows)
                       from c in Enumerable.Range(1, map.Cols)
                       where map.FloorAt(c, r) == 0 && Free(c, r)
                       select (c, r)).Take(1).ToList();
        foreach (var (c, r) in entries)
        {
            if (!OnMap(c, r)) continue;
            if (Free(c, r)) return (c, r);
            // Do not move the resident NPC or teleport through a wall to make room for the hero.
            foreach (var (dc, dr) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
                if (Free(c + dc, r + dr) && MovementCalculator.CanMoveTo(state, c, r, c + dc, r + dr, -1))
                    return (c + dc, r + dr);
        }
        return null;
    }
}
