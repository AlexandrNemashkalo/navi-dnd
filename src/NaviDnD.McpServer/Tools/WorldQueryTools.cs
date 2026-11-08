using System.ComponentModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using ModelContextProtocol.Server;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.McpServer;

// World map for the GM: world_query - information about the place nearby, the path; add_place - a new place on
// the map (the cell is selected by the code). Game world = geometry (Storage/Worlds/<world.id>/world.json next
// to the state file) + game layer (world in the state file: concept, places, paths). The tools do not write
// anything themselves - add_place returns a patch, which the GM puts in response (like advance_round).
[McpServerToolType]
public sealed class WorldQueryTools(McpServerConfig config)
{
    private static readonly object _logLock = new();

    private static readonly JsonSerializerOptions _readOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    [McpServerTool]
    [Description("Query the world map: hero-known places and secret GM plans, kingdoms, roads, distances.\nquery formats: place:<name> (identity, owner, location, description, surroundings, secret); near:<name> (places within about 6 travel days, direction/time); route:<from>;<to> (walking time, roads, kingdoms crossed); overview (concept, kingdoms, places).\nA road cell is about a quarter-day of travel. Names may be partial; use actual names from the game state.")]
    public string WorldQuery(string query)
    {
        Log($"world_query → \"{query}\"");
        try
        {
            var (world, _, _) = LoadWorld();
            if (world == null) return "No world is loaded.";
            string q = query.Trim();
            int colon = q.IndexOf(':');
            string mode = (colon >= 0 ? q[..colon] : q).Trim().ToLowerInvariant();
            string arg = colon >= 0 ? q[(colon + 1)..].Trim() : "";
            switch (mode)
            {
                case "place" or "место":
                    return WorldAtlas.FindPlace(world, arg) is { } p ? WorldAtlas.PlaceInfo(world, p, withDmNotes: true) : $"Place «{arg}» does not exist.";
                case "near" or "рядом":
                    return WorldAtlas.FindPlace(world, arg) is { } n ? WorldAtlas.Near(world, n) : $"Place «{arg}» does not exist.";
                case "route" or "путь":
                    var parts = arg.Split(';', 2);
                    if (parts.Length < 2) return "Format: route:From;To";
                    var a = WorldAtlas.FindPlace(world, parts[0]);
                    var b = WorldAtlas.FindPlace(world, parts[1]);
                    if (a == null || b == null) return $"Place «{(a == null ? parts[0] : parts[1]).Trim()}» does not exist.";
                    return WorldAtlas.Route(world, a, b) is { } r ? $"{a.Name} → {b.Name}: {r.Summary}" : $"Cannot walk from «{a.Name}» to «{b.Name}» (water blocks the route).";
                default:
                    return WorldAtlas.Overview(world, withDmNotes: true);
            }
        }
        catch (Exception ex)
        {
            Log($"world_query error: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Add a world-map place the hero learned of or is directed to (settlement, ruins, lair, shrine, etc.). Code selects a terrain-appropriate site (settlements near water/roads, lairs in wilderness) and adds a trail. Returns a root world patch: include it unchanged or merge with other world fields.\ntype: city|town|port|village|castle|ruins|dungeon|cave|shrine. near: originating place (default hero location); direction: north/northeast/etc.; days: approximate travel days; terrain: forest, thicket, mountains, hills, swamp, desert, snow, near river/sea/lake/road, wilderness; kingdom: actual kingdom name or K-number.\nhidden:true means a secret GM plan invisible to the hero/map; reveal later with hidden:false. New names/descriptions use the game language.")]
    public string AddPlace(
        string name, string type, string description,
        [Description("Secret GM plan; not visible to the hero.")] string? dmNotes = null,
        string? near = null, string? direction = null, double? days = null, string? terrain = null,
        string? kingdom = null, bool hidden = false)
    {
        Log($"add_place → {name} ({type}) near:{near} dir:{direction} days:{days} terrain:{terrain} kingdom:{kingdom} hidden:{hidden}");
        try
        {
            var (all, geo, link) = LoadWorld();
            if (all == null || geo == null) return "No world is loaded.";
            if (all.Places.Any(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
                return $"Place «{name}» already exists: {WorldAtlas.PlaceInfo(all, WorldAtlas.FindPlace(all, name)!, true)}";
            type = type.Trim().ToLowerInvariant();
            if (type == WorldPlaceTypes.Capital) return "Capitals are already on the map; choose another type.";
            var nearPlace = near is { Length: > 0 } nn ? WorldAtlas.FindPlace(all, nn)
                : link?.Place is { } here ? WorldAtlas.FindPlace(all, here)
                : link is { X: int hx, Y: int hy } ? new WorldPlace { Name = "герой", X = hx, Y = hy } : null;
            int k = -1;
            if (kingdom is { Length: > 0 } kg)
                k = kg.Trim().StartsWith('K') && int.TryParse(kg.Trim()[1..], out int ki) ? ki
                    : all.Kingdoms.FindIndex(x => x.Name.Contains(kg.Trim(), StringComparison.OrdinalIgnoreCase));
            var used = geo.Chronicle.Places.Select(p => p.Slot).Where(s => s >= 0).ToHashSet();
            var spot = WorldAtlas.FindSpot(geo, new WorldAtlas.SpotRequest(type, nearPlace, direction, days ?? (nearPlace != null ? 2 : null), terrain, k),
                all.Places.Select(p => (p.X, p.Y)).ToList(), used);
            if (spot is not { } s) return "No suitable site found; relax the constraints.";

            var patch = new { world = new { places = new[] { new { name = name.Trim(), type, x = s.x, y = s.y, slot = s.slot, hidden, description, dmNotes } } } };
            var probe = new WorldPlace { Name = name, Type = type, X = s.x, Y = s.y, Kingdom = geo.OwnerAt(s.x, s.y) };
            var sb = new StringBuilder();
            sb.AppendLine($"{name}: {WorldPlaceTypes.Label(type)}, {WorldAtlas.KingdomName(all, probe)}, {WorldAtlas.Compass(all, s.x, s.y)}; surroundings: {WorldAtlas.Surroundings(all, s.x, s.y, named: true)}");
            if (nearPlace != null && WorldAtlas.Route(all, nearPlace, probe) is { } r)
                sb.AppendLine($"From «{nearPlace.Name}»: {WorldAtlas.Direction(s.x - nearPlace.X, s.y - nearPlace.Y)}, {r.Summary}");
            sb.AppendLine("Patch for the response:");
            sb.Append(JsonSerializer.Serialize(patch, _writeOptions));
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Log($"add_place error: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }

    // Game world with hidden places; geo - geometry, link - game layer (no - the general world as is).
    private (WorldMap? all, WorldMap? geo, GameWorldLink? link) LoadWorld()
    {
        string worlds = Path.Combine(Path.GetDirectoryName(config.WorldStatePath) ?? ".", "Worlds");
        GameWorldLink? link = null;
        try { link = JsonSerializer.Deserialize<WorldState>(File.ReadAllText(config.WorldStatePath, Encoding.UTF8), _readOptions)?.World; }
        catch { /*no state file - active world*/ }
        string? id = link?.Id;
        if (string.IsNullOrEmpty(id) && File.Exists(Path.Combine(worlds, "active.txt")))
            id = File.ReadAllText(Path.Combine(worlds, "active.txt")).Trim();
        if (string.IsNullOrEmpty(id) || WorldLibrary.LoadFrom(Path.Combine(worlds, id, "world.json")) is not { } geo) return (null, null, link);
        return (link != null ? GameWorld.Compose(geo, link, includeHidden: true) : geo, geo, link);
    }

    private void Log(string message)
    {
        if (config.LogPath is null) return;
        lock (_logLock)
            File.AppendAllText(config.LogPath, $"  MCP {message}\n", new UTF8Encoding(false));
    }
}
