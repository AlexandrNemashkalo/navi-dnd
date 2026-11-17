using System.Text.Json;
using NaviDnD.Data.Models;

namespace NaviDnD;

// Hero-specific exploration stays outside the shared world's maps and the GM patch protocol.
public sealed class ExplorationMemory
{
    public sealed class Location
    {
        public List<int[]> Cells { get; set; } = [];
        public List<string>? SeenObjects { get; set; }
    }

    public sealed class Document
    {
        public int FormatVersion { get; set; } = StorageFormat.Current;
        public Dictionary<string, Location> Locations { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private Document _document = new();
    private bool _initialized;
    private string? _active;

    public static string? LocationKey(WorldState state)
    {
        if (!GameWorld.HasLocation(state)) return null;
        var world = state.World;
        // A generated map keeps its first seed while it grows and when it is restored.
        // Coordinates identify unnamed encounters; a named place keeps its key across travel.
        string site = world?.Place?.Trim().ToUpperInvariant() is { Length: > 0 } name
            ? name : $"{world?.X},{world?.Y}";
        string geometry = state.Map.Chunks is { Count: > 0 } chunks ? $"seed:{chunks[0].Seed}" : "rooms";
        return JsonSerializer.Serialize(new[] { world?.Id ?? "", site, geometry });
    }

    public void Select(Storage storage)
    {
        // Resolve only this world's historical names. LocationKey itself stays independent
        // of WorldLibrary because storage migrations also use it against arbitrary roots.
        var geo = GameWorld.Geo(storage.WorldState);
        string? next = LocationKey(storage.WorldState);
        if (_initialized && next == _active) return;
        if (_initialized) Capture(storage);
        if (geo != null && storage.WorldState.World is { } world)
        {
            CanonicalizeLocations(geo, world.Id);
            _active = CanonicalKey(_active, geo, world.Id);
            next = CanonicalKey(next, geo, world.Id);
        }
        // An alias-only change is still the same scene, including detection and reaction state.
        if (_initialized && next == _active)
        {
            if (next != null && _document.Locations.TryGetValue(next, out var sameLocation)) Restore(storage, sameLocation);
            return;
        }
        if (_initialized) storage.ResetLocationRuntime();
        bool first = !_initialized;
        _active = next;
        _initialized = true;
        storage.VisibleCells.Clear();
        if (next != null && _document.Locations.TryGetValue(next, out var location))
            Restore(storage, location);
        else if (!first || next == null)
        {
            storage.ExploredCells.Clear();
            storage.SeenObjects = null;
        }
    }

    private static void Restore(Storage storage, Location location)
    {
        storage.ExploredCells = location.Cells.Where(c => c is { Length: 2 })
            .Select(c => (c[0], c[1])).ToHashSet();
        storage.SeenObjects = location.SeenObjects?.ToHashSet(StringComparer.Ordinal);
    }

    private void CanonicalizeLocations(WorldMap geo, string worldId)
    {
        var canonical = new Dictionary<string, Location>(StringComparer.Ordinal);
        foreach (var (key, location) in _document.Locations)
        {
            string resolved = CanonicalKey(key, geo, worldId)!;
            if (!canonical.TryGetValue(resolved, out var existing)) canonical[resolved] = location;
            else
            {
                // The tuple retains world and geometry, so aliases never combine different maps.
                existing.Cells = existing.Cells.Concat(location.Cells).Where(c => c is { Length: 2 })
                    .Select(c => (x: c[0], y: c[1])).Distinct().OrderBy(c => c.x).ThenBy(c => c.y)
                    .Select(c => new[] { c.x, c.y }).ToList();
                if (existing.SeenObjects != null || location.SeenObjects != null)
                    existing.SeenObjects = (existing.SeenObjects ?? []).Concat(location.SeenObjects ?? [])
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            }
        }
        _document.Locations = canonical;
    }

    private static string? CanonicalKey(string? key, WorldMap geo, string worldId)
    {
        if (key == null) return null;
        try
        {
            var parts = JsonSerializer.Deserialize<string[]>(key);
            if (parts is not { Length: 3 } || parts[0] != worldId || parts[1] == null || parts[2] == null) return key;
            if (GameWorld.ResolvePlace(geo, parts[1]) is not { } place) return key;
            parts[1] = place.Name.Trim().ToUpperInvariant();
            return JsonSerializer.Serialize(parts);
        }
        catch (JsonException) { return key; }
    }

    private void Capture(Storage storage)
    {
        if (_active == null) return;
        _document.Locations[_active] = new()
        {
            Cells = storage.ExploredCells.OrderBy(c => c.X).ThenBy(c => c.Y).Select(c => new[] { c.X, c.Y }).ToList(),
            SeenObjects = storage.SeenObjects?.Order(StringComparer.Ordinal).ToList(),
        };
    }

    public string Serialize(Storage storage)
    {
        Select(storage);
        Capture(storage);
        return JsonSerializer.Serialize(_document, Options);
    }

    public void Load(Storage storage, string? json)
    {
        _document = new();
        _initialized = true;
        _active = null;
        storage.ExploredCells.Clear();
        storage.VisibleCells.Clear();
        storage.SeenObjects = null;
        if (json != null)
        {
            var loaded = JsonSerializer.Deserialize<Document>(json, Options);
            if (loaded?.FormatVersion != StorageFormat.Current || loaded.Locations == null)
                throw new InvalidDataException("Exploration data requires migration.");
            _document = loaded;
        }
        Select(storage);
    }
}
