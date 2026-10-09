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
    [Description("""
        Генерирует карту и записывает её в worldState (cols, rows, rooms, doors).
        Возвращает JSON сгенерированной карты, чтобы ИИ мог расставить entities и objects по
        комнатам, ориентируясь на их координаты и форму.

        Схема MapGenRequest:
          type: "Cave" | "Dungeon" | "Building" | "OpenArea"  (по умолчанию: "Dungeon")
          dungeon: {          ← обязателен только для type "Dungeon"
            rooms: [
              { name: string,   ← ОБЯЗАТЕЛЬНО по-русски
                shape: "Rectangular"|"Circular"|"Irregular"|"LShaped",
                size: "Small"|"Medium"|"Large"|"Huge", zone?: string }
            ]
            passages: [
              { from: string, to: string,
                type: "Opening"|"Corridor",
                width?: int (1–2), shape?: "Straight"|"LShaped"|"Winding",
                hasDoor?: bool }
            ]
            transitions: [
              { edgePoint: [col, row], room: string,
                width?: int, shape?: string, hasDoor?: bool }
            ]
          }

        Формы комнат:
          "Rectangular" — обычный прямоугольник
          "Circular"    — эллипс, вписанный в габаритный прямоугольник
          "Irregular"   — прямоугольник с органично неровными краями
          "LShaped"     — Г-образная комната: один угол срезан (35–60% каждой стороны), края
                          слегка неровные; хороша для караулок, боковых часовен и т.п.

        Значения zone: "north", "south", "west", "east", "center",
                       "north_west", "north_east", "south_west", "south_east"

        transitions.edgePoint — координата за пределами сетки, задаёт сторону входа:
          Запад [0, row]   Восток [21, row]
          Север [col, 0]   Юг [col, 16]

        Рекомендации: 3–6 комнат; хотя бы один transition для выхода с карты.

        Карта генерируется в сетке cols×rows (по умолчанию 20×15, к ней же относятся edgePoint выше),
        а затем ставится в центр поля локации 100×100. Все координаты в ОТВЕТЕ (комнаты, двери) — уже
        координаты поля: расставляй героя, существа и объекты именно по ним.
        """)]
    public string GenerateMap(
        [Description("MapGenRequest в виде JSON-строки")]
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

        // Генератор рисует карту от (1,1) — ставим её в центр поля локации 100×100 (MapConfig.CenterInField),
        // чтобы вокруг было место во все стороны. Ответ ниже — уже в координатах поля.
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
    [Description("""
        План локации по сюжету: из каких блоков 20×15 она состоит и что в каждом. Движок сразу строит
        геометрию всех блоков (комнаты, двери, переходы между соседними блоками) и ставит героя у входа
        снаружи в первый блок. Возвращает стартовый блок (комнаты с id и клетками, двери) — его наполни в
        ответе; остальные блоки наполнятся отдельно, когда герой к ним подойдёт.

        planJson: {"blocks": [{"x": 0, "y": 0, "z": 0, "terrain": "forest", "purpose": "..."}, ...], "startInside": false, "worldX": 12, "worldY": 34}
          worldX/worldY — клетка карты мира места (из патча add_place), если герой ещё не там (начало игры); иначе не нужны.
          startInside — true, если по сюжету игра начинается внутри стартового блока (в таверне, в доме): герой
          встанет в первой комнате за входом; иначе — снаружи у входа.
          blocks[0] — стартовый (вход снаружи); x — на восток, y — на север, соседние блоки отличаются на 1
          по x или y; все блоки связаны сторонами в одну фигуру; 1–8 блоков, фигура не больше 5×6.
          terrain — dungeon (рукотворное: склеп, руины, крепость, катакомбы — комнаты, коридоры, двери,
          плиточный пол; по умолчанию) | cave (природная пещера: неровные залы, лазы, сталагмиты, без дверей)
          | building (здание: фигурный корпус, комнаты с общими стенами, двери, окна, мебель; на z=0 — во дворе;
            "building": tavern | house | shop | temple | manor | barracks | tower — планировка: у tavern/shop/
            temple/manor/barracks на z=0 большой зал со стойкой/прилавком/алтарём и подсобки, выше — коридор с комнатами)
          | village (поселение: 3–5 домов, улицы, площадь с колодцем, огороды; таверна/храм — отдельным building рядом)
          | forest | plains | hills | mountain | swamp | desert | snow | taiga (открытая местность: рельеф без комнат; вид
            местности и реку движок берёт с карты мира — клетки героя, указывай любой открытый). Местность рядом с dungeon-блоком
          отделена скалой с проходом (вход в пещеру/подземелье из леса или с горы).
          z — этаж (по умолчанию 0 — земля): верхние этажи здания — блоки с теми же x,y и z=1,2… (terrain building,
          под каждым — building); подвал/подземелье под блоком — z=-1… (dungeon/cave/building). Этажи одной
          клетки x,y связаны лестницей; blocks[0] — на z=0; соседи по x/y на одном z связаны проходом.
          purpose — что в блоке по сюжету (кто/что там, роль в истории), одна фраза.
        """)]
    public string PlanLocation(
        [Description("План локации в виде JSON-строки")]
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
            // Местность — по клетке карты мира, где герой (лес/горы/пустыня, река, берег).
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
            // Посреди игры (SendAction) игра держит состояние в памяти — метка: перечитать карту из сохранения.
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
