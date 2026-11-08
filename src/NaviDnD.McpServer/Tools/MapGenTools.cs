using System.ComponentModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using ModelContextProtocol.Server;
using NaviDnD;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.MapGen;
using NaviDnD.MapGen.Generators;
using NaviDnD.MapGen.Models;

namespace NaviDnD.McpServer;

[McpServerToolType]
public sealed class MapGenTools(McpServerConfig config)
{
    private static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object _logLock = new();

    [McpServerTool]
    [Description("Generate a map and write cols, rows, rooms, and doors into worldState. Returns map JSON so entities/objects can be placed using actual room shapes and coordinates.\nMapGenRequest: type=\"Cave\"|\"Dungeon\"|\"Building\"|\"OpenArea\" (default Dungeon). For Dungeon, include dungeon:{rooms:[{name,shape,size,zone?}],passages:[{from,to,type,width?,shape?,hasDoor?}],transitions:[{edgePoint:[col,row],room,width?,shape?,hasDoor?}]}.\nRoom names use the game language. Room shape: Rectangular, Circular (inscribed ellipse), Irregular (organic edges), or LShaped (one corner removed, 35–60% of each side, slightly uneven edges). Size: Small|Medium|Large|Huge. Passage type: Opening|Corridor; width 1–2; shape Straight|LShaped|Winding.\nzone: north|south|west|east|center|north_west|north_east|south_west|south_east.\ntransitions.edgePoint is outside the local grid: west [0,row], east [21,row], north [col,0], south [col,16]. Prefer 3–6 rooms and at least one exit transition.\nGeneration uses cols×rows (default 20×15, also the basis of the edgePoint examples), then centers it in the 100×100 location field. All RETURNED room/door coordinates are already field coordinates; use those for hero, creature, and object positions.")]
    public string GenerateMap(
        [Description("MapGenRequest as a JSON string.")]
        string requestJson)
    {
        Log($"→ GenerateMap: {requestJson}");

        MapGenRequest request;
        try
        {
            request = JsonSerializer.Deserialize<MapGenRequest>(requestJson, _readOptions)
                      ?? new MapGenRequest();
        }
        catch (Exception ex)
        {
            string err = $"{{\"error\": \"Invalid requestJson: {ex.Message}\"}}";
            Log($"← (parse error) {err}");
            return err;
        }

        IMapGenerator generator = request.Type switch
        {
            MapType.Dungeon  => new DungeonGenerator(),
            MapType.Cave     => new HardcodedCaveGenerator(),
            MapType.Building => new HardcodedCaveGenerator(),
            MapType.OpenArea => new HardcodedCaveGenerator(),
            _                => new HardcodedCaveGenerator(),
        };

        MapConfig mapConfig;
        try
        {
            mapConfig = generator.Generate(request);
        }
        catch (Exception ex)
        {
            string err = $"{{\"error\": \"Map generation failed: {ex.Message}\"}}";
            Log($"← (gen error) {err}");
            return err;
        }

        // The generator draws a map from (1,1) - put it in the center of the location field 100x100
        // (MapConfig.CenterInField), so that there is space around in all directions. The answer below is
        // already in the field coordinates.
        mapConfig.CenterInField();

        // Apply the new map into worldState and persist it
        try
        {
            var mapUpdate = new { map = mapConfig };
            string mapUpdateJson = JsonSerializer.Serialize(mapUpdate, _writeOptions);

            var storage = LoadStorage();
            storage.ApplyUpdateWorldState(mapUpdateJson);
            SaveStorage(storage);
        }
        catch (Exception ex)
        {
            string err = $"{{\"error\": \"Failed to persist map: {ex.Message}\"}}";
            Log($"← (save error) {err}");
            return err;
        }

        // Return only what the AI needs: room positions + real doors (not passthrough) + entities/objects
        var aiResponse = new
        {
            map   = new { colors = mapConfig.Colors },
            rooms = mapConfig.Rooms?.Select((r, i) => new { id = i, r.Name, r.Color, r.Positions }).ToList(),
            doors = mapConfig.Doors?
                             .Select((d, i) => new { id = i, d.From, d.To, d.IsDoor, d.Color, d.IsDoorOpen, d.Hidden, d.IsEntry }).ToList(),
            //area         = mapConfig.Area,
            //triggerCells = mapConfig.TriggerCells,
        };

        string result = JsonSerializer.Serialize(aiResponse, _writeOptions);
        Log($"← ({result.Length} chars): {result}");
        return result;
    }

    private sealed record LocationPlanRequest(List<LocationGrower.PlannedBlock>? Blocks, string? Theme, bool? StartInside, int? WorldX = null, int? WorldY = null);

    [McpServerTool]
    [Description("Plan a finite story location as 20×15 chunks. The engine immediately builds all geometry (rooms, doors, transitions) and places the hero outside the first chunk's entrance. Returns the starting chunk with room IDs/cells and doors; populate only that chunk in the response. Other chunks are populated separately when approached.\nplanJson:{\"blocks\":[{\"x\":0,\"y\":0,\"z\":0,\"terrain\":\"forest\",\"purpose\":\"...\"}],\"startInside\":false,\"worldX\":12,\"worldY\":34}.\nworldX/worldY: world-map cell of the place (from add_place) if the hero is not there yet, e.g. game start; otherwise omit. startInside:true starts in the first room beyond the entrance instead of outside.\nblocks[0] is the starting chunk on z=0. x increases east, y north; adjacent chunks differ by 1 in x or y. All chunks must form one connected shape, 1–8 chunks within 5×6.\nterrain: dungeon (default; constructed crypt, ruins, fortress, catacombs with rooms/corridors/doors/tiled floors); cave (natural irregular chambers, narrow passages, stalagmites, no doors); building (shaped footprint, shared walls, doors/windows/furniture, courtyard on z=0), with building:tavern|house|shop|temple|manor|barracks|tower; village (3–5 houses, streets, well square, gardens; tavern/temple as an adjacent building); forest|plains|hills|mountain|swamp|desert|snow|taiga (outdoor terrain, no rooms; actual terrain/river comes from the hero's world-map cell, so any outdoor type is acceptable). Outdoors next to a dungeon is separated by a cliff with an entrance.\nBuilding tavern/shop/temple/manor/barracks at z=0 has a large hall with counter/altar and utility rooms; upper floors have corridors and rooms. z=1,2... requires building above building at the same x,y; basement z=-1... may be dungeon/cave/building. Stacked floors connect by stairs; horizontal neighbors on the same z connect by passages.\nmoat:true: ground-floor building on an island behind a moat with one ford. Village layout: road|square|river|palisade. purpose: one sentence defining occupants/content and story role.")]
    public string PlanLocation(
        [Description("Location plan as a JSON string.")]
        string planJson)
    {
        Log($"→ PlanLocation: {planJson}");
        LocationPlanRequest? plan;
        try
        {
            plan = JsonSerializer.Deserialize<LocationPlanRequest>(planJson, _readOptions);
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"Invalid planJson: {ex.Message}\"}}";
        }

        try
        {
            var storage = LoadStorage();
            // Terrain - according to the cell of the world map where the hero is (forest/mountains/desert, river, shore).
            LocationGrower.WorldSite? site = null;
            if (storage.WorldState.World is { } link)
            {
                string worlds = Path.Combine(Path.GetDirectoryName(config.WorldStatePath) ?? ".", "Worlds");
                if (WorldLibrary.LoadFrom(Path.Combine(worlds, link.Id, "world.json")) is { } geo)
                {
                    var all = GameWorld.Compose(geo, link, includeHidden: true);
                    (int x, int y)? tile = plan?.WorldX is int wx && plan.WorldY is int wy ? (wx, wy)
                        : link.X is int hx && link.Y is int hy ? (hx, hy)
                        : link.Place is { } pl && MapGen.Generators.WorldAtlas.FindPlace(all, pl) is { } p ? (p.X, p.Y) : null;
                    if (tile is { } t) site = GameWorld.SiteAt(geo, t.x, t.y);
                }
            }
            var (start, error) = LocationGrower.CreatePlanned(storage.WorldState, plan?.Blocks ?? [], plan?.Theme ?? "dungeon", plan?.StartInside == true, site);
            if (start == null)
            {
                Log($"← (plan error) {error}");
                return $"{{\"error\": \"{error}\"}}";
            }
            SaveStorage(storage);
            // In the middle of the game (SendAction) the game keeps the state in memory - mark: reread the map from the save.
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(config.WorldStatePath) ?? ".", Storage.LocationPlannedFlag), ""); } catch { }

            var ctx = new AiContextBuilder(storage.WorldState);
            string result = ctx.LocationPlan(start) + "\n" + ctx.ChunkState(start);
            Log($"← ({result.Length} chars): {result}");
            return result;
        }
        catch (Exception ex)
        {
            string err = $"{{\"error\": \"Location generation failed: {ex.Message}\"}}";
            Log($"← (gen error) {err}");
            return err;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private Storage LoadStorage()
    {
        string json = RetryOnIo(() =>
        {
            using var stream = new FileStream(config.WorldStatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        });
        var storage = new Storage();
        storage.ApplySavedState(json);
        return storage;
    }

    private void SaveStorage(Storage storage)
    {
        string json = JsonSerializer.Serialize(storage.WorldState, _writeOptions);
        RetryOnIo(() =>
        {
            using var stream = new FileStream(config.WorldStatePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(json);
        });
    }

    private static T RetryOnIo<T>(Func<T> action, int attempts = 5, int delayMs = 30)
    {
        for (int i = 0; i < attempts; i++)
        {
            try { return action(); }
            catch (IOException) when (i < attempts - 1) { Thread.Sleep(delayMs); }
        }
        throw new InvalidOperationException("IO retry limit exceeded");
    }

    private static void RetryOnIo(Action action, int attempts = 5, int delayMs = 30)
    {
        for (int i = 0; i < attempts; i++)
        {
            try { action(); return; }
            catch (IOException) when (i < attempts - 1) { Thread.Sleep(delayMs); }
        }
        throw new InvalidOperationException("IO retry limit exceeded");
    }

    private void Log(string message)
    {
        if (config.LogPath is null) return;
        lock (_logLock)
            File.AppendAllText(config.LogPath, $"  MapGen {message}\n", new UTF8Encoding(false));
    }
}
