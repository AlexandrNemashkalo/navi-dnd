using System.Text;
using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD;

// Library of worlds: Storage/Worlds/<id>/world.json - the geometry of the world and its chronicle (lor and
// places common to all games of this world), Storage/Worlds/<id>/locations/*.json - saved locations of places.
// Active world (Storage/Worlds/active.txt) - for a new game by default and for old saves without a game world.
public static class WorldLibrary
{
    public static string Dir => Path.Combine(AppConfig.DataRoot, "Storage", "Worlds");
    private static string ActivePath => Path.Combine(Dir, "active.txt");
    private static string WorldPath(string id) => Path.Combine(Dir, id, "world.json");
    public static string LocationsDir(string id) => Path.Combine(Dir, id, "locations");

    private static WorldMap? _current;
    private static readonly Dictionary<string, WorldMap> _loaded = [];
    public static event Action? Changed;

    public static WorldMap Current
    {
        get
        {
            if (_current != null) return _current;
            try
            {
                string? id = File.Exists(ActivePath) ? File.ReadAllText(ActivePath).Trim() : null;
                if (id != null) _current = Get(id);
            }
            catch { _current = null; }
            return _current ??= Create(WorldSizes.Small, new Random().Next());
        }
    }

    // World by id (game world) - read from disk once per launch. Doesn't create a new one.
    public static WorldMap? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_loaded.TryGetValue(id, out var w)) return w;
        w = LoadFrom(WorldPath(id));
        if (w != null) _loaded[id] = w;
        return w;
    }

    // Make the world active (default for new game).
    public static void SetActive(string id)
    {
        if (Get(id) is not { } w) return;
        _current = w;
        try { File.WriteAllText(ActivePath, id); } catch { }
        Changed?.Invoke();
    }

    // Library worlds for the profile of a new game: active and those where they have already played (there is a
    // chronicle) - empty worlds (debugging, F12) are not shown. id, name (chronicle or generator), size, places
    // in the chronicle.
    public static List<(string id, string name, string size, int places)> All()
    {
        var list = new List<(string, string, string, int)>();
        if (!Directory.Exists(Dir)) return list;
        string? active = File.Exists(ActivePath) ? File.ReadAllText(ActivePath).Trim() : null;
        foreach (var d in Directory.GetDirectories(Dir).OrderByDescending(Directory.GetLastWriteTimeUtc))
        {
            string id = Path.GetFileName(d);
            if (id != active && !HasChronicle(WorldPath(id))) continue;
            if (Get(id) is { } w)
                list.Add((w.Id, w.Chronicle.Name is { Length: > 0 } n ? n : w.Name, w.Size, w.Chronicle.Places.Count));
        }
        return list;
    }

    // Is there a chronicle in the world file (without parsing everything JSON): is empty - no name, no kingdoms.
    private static bool HasChronicle(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            string text = File.ReadAllText(path, Encoding.UTF8);
            int i = text.IndexOf("\"Chronicle\":", StringComparison.Ordinal);
            return i >= 0 && !text.AsSpan(i).StartsWith("\"Chronicle\":{\"Name\":null,\"Description\":null,\"DmNotes\":null,\"Kingdoms\":[]");
        }
        catch { return false; }
    }

    // Reading the world file (and the MCP- server - according to its path); old worlds are expanded (islands, places not on the water).
    public static WorldMap? LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var w = JsonSerializer.Deserialize<WorldMap>(File.ReadAllText(path, Encoding.UTF8));
            if (w == null) return null;
            WorldGenerator.AddIslands(w, new Random(w.Seed ^ 0x15a1));
            WorldGenerator.MovePlacesOffWater(w);
            return w;
        }
        catch { return null; }
    }

    public static void Save(WorldMap world)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(Dir, world.Id));
            File.WriteAllText(WorldPath(world.Id), JsonSerializer.Serialize(world), Encoding.UTF8);
        }
        catch { /*not saved - changes live until the end of the session*/ }
        _loaded[world.Id] = world;
        Changed?.Invoke();
    }

    // A world generated in advance (new game questionnaire: preview before selection) - to the library and active.
    public static void Register(WorldMap world)
    {
        Save(world);
        try { File.WriteAllText(ActivePath, world.Id); } catch { }
        _current = world;
        Changed?.Invoke();
    }

    // Games (slots “My Games”) that are played in this world: the number and name of the hero. Saving without a
    // game world - in the active world.
    public static List<(int slot, string hero)> GamesUsing(string id)
    {
        var list = new List<(int, string)>();
        string? active = File.Exists(ActivePath) ? File.ReadAllText(ActivePath).Trim() : null;
        for (int slot = 1; slot <= Storage.MaxGames; slot++)
        {
            string path = Storage.SlotPath(slot);
            if (!File.Exists(path)) continue;
            try
            {
                var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8));
                string? worldId = (string?)(root?["world"]?["id"] ?? root?["World"]?["Id"]);
                if (worldId == id || worldId == null && id == active)
                    list.Add((slot, (string?)(root?["hero"]?["name"] ?? root?["Hero"]?["Name"]) ?? L.F("game {0}", slot)));
            }
            catch { list.Add((slot, L.F("game {0}", slot))); }   // not read - better not delete
        }
        return list;
    }

    // Delete the world with its locations (check that there are no games in it - GamesUsing, before calling).
    public static bool Delete(string id)
    {
        try
        {
            string dir = Path.Combine(Dir, id);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { return false; }
        _loaded.Remove(id);
        if (_current?.Id == id || File.Exists(ActivePath) && File.ReadAllText(ActivePath).Trim() == id)
        {
            try { File.Delete(ActivePath); } catch { }
            _current = null;
        }
        Changed?.Invoke();
        return true;
    }

    // New world: generate, save, make active.
    public static WorldMap Create(string size, int seed)
    {
        var world = WorldGenerator.Generate(new WorldGenerator.Options(seed, size, Language: L.Language));
        try
        {
            Directory.CreateDirectory(Path.Combine(Dir, world.Id));
            File.WriteAllText(WorldPath(world.Id), JsonSerializer.Serialize(world), Encoding.UTF8);
            File.WriteAllText(ActivePath, world.Id);
        }
        catch { /*not saved - the world lives until the end of the session*/ }
        _loaded[world.Id] = world;
        _current = world;
        Changed?.Invoke();
        return world;
    }
}
