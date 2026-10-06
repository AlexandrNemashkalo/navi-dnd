using System.ComponentModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using ModelContextProtocol.Server;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.McpServer;

// Карта мира для мастера: world_query — сведения о месте, что рядом, путь; add_place — новое место на карте
// (клетку подбирает код). Мир игры = геометрия (Storage/Worlds/<world.id>/world.json рядом с файлом состояния)
// + слой игры (world в файле состояния: концепция, места, тропы). Инструменты ничего не пишут сами —
// add_place возвращает патч, который мастер кладёт в ответ (как advance_round).
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
    [Description("""
        Карта мира: места игры (известные герою и скрытые замыслы мастера), королевства, дороги, расстояния.
        query — одна из форм:
          "place:Имя" — что за место, чьё, где, описание, окружение (+ тайна мастера);
          "near:Имя" — места в ~6 днях пути: направление и время в пути;
          "route:Откуда;Куда" — путь пешком: сколько дней, по дороге ли, через какие королевства;
          "overview" — весь мир: концепция, королевства, места.
        Клетка мира ≈ четверть дня пути по дороге. Имя можно неточно (часть имени).
        """)]
    public string WorldQuery(string query)
    {
        Log($"world_query → \"{query}\"");
        try
        {
            var (world, _, _) = LoadWorld();
            if (world == null) return "Мира нет.";
            string q = query.Trim();
            int colon = q.IndexOf(':');
            string mode = (colon >= 0 ? q[..colon] : q).Trim().ToLowerInvariant();
            string arg = colon >= 0 ? q[(colon + 1)..].Trim() : "";
            switch (mode)
            {
                case "place" or "место":
                    return WorldAtlas.FindPlace(world, arg) is { } p ? WorldAtlas.PlaceInfo(world, p, withDmNotes: true) : $"Места «{arg}» нет.";
                case "near" or "рядом":
                    return WorldAtlas.FindPlace(world, arg) is { } n ? WorldAtlas.Near(world, n) : $"Места «{arg}» нет.";
                case "route" or "путь":
                    var parts = arg.Split(';', 2);
                    if (parts.Length < 2) return "Формат: route:Откуда;Куда";
                    var a = WorldAtlas.FindPlace(world, parts[0]);
                    var b = WorldAtlas.FindPlace(world, parts[1]);
                    if (a == null || b == null) return $"Места «{(a == null ? parts[0] : parts[1]).Trim()}» нет.";
                    return WorldAtlas.Route(world, a, b) is { } r ? $"{a.Name} → {b.Name}: {r.Summary}" : $"Пешком из «{a.Name}» в «{b.Name}» не дойти (вода).";
                default:
                    return WorldAtlas.Overview(world, withDmNotes: true);
            }
        }
        catch (Exception ex)
        {
            Log($"world_query error: {ex.Message}");
            return $"Ошибка: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("""
        Добавляет место на карту мира: город/деревню, о которых узнал герой, или место, куда его отправили
        (руины, логово, святилище…). Клетку подбирает код по рельефу (поселение — у воды и дорог, логово — в
        глуши); к новой деревне/логову сама проложится тропа. Возвращает патч — положи его в ответ как есть
        (корневой "world"; можно слить с другими полями world).
        type: city | town | port | village | castle | ruins | dungeon | cave | shrine.
        near — от какого места (по умолчанию — где герой); direction — север/северо-восток/…; days — примерно
        сколько дней пути; terrain — слова: лес, чаща, горы, холмы, болото, пустыня, снег, у реки, у моря,
        у озера, у дороги, в глуши; kingdom — имя королевства или K-номер.
        hidden=true — замысел мастера, герой о месте не знает (на карте не видно; откроешь патчем hidden:false).
        """)]
    public string AddPlace(
        string name, string type, string description,
        [Description("Тайна/план мастера, герою не видна")] string? dmNotes = null,
        string? near = null, string? direction = null, double? days = null, string? terrain = null,
        string? kingdom = null, bool hidden = false)
    {
        Log($"add_place → {name} ({type}) near:{near} dir:{direction} days:{days} terrain:{terrain} kingdom:{kingdom} hidden:{hidden}");
        try
        {
            var (all, geo, link) = LoadWorld();
            if (all == null || geo == null) return "Мира нет.";
            if (all.Places.Any(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
                return $"Место «{name}» уже есть: {WorldAtlas.PlaceInfo(all, WorldAtlas.FindPlace(all, name)!, true)}";
            type = type.Trim().ToLowerInvariant();
            if (type == WorldPlaceTypes.Capital) return "Столицы уже на карте — выбери другой тип.";
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
            if (spot is not { } s) return "Подходящего места не нашлось — смягчи условия.";

            var patch = new { world = new { places = new[] { new { name = name.Trim(), type, x = s.x, y = s.y, slot = s.slot, hidden, description, dmNotes } } } };
            var probe = new WorldPlace { Name = name, Type = type, X = s.x, Y = s.y, Kingdom = geo.OwnerAt(s.x, s.y) };
            var sb = new StringBuilder();
            sb.AppendLine($"{name}: {WorldPlaceTypes.Label(type)}, {WorldAtlas.KingdomName(all, probe)}, {WorldAtlas.Compass(all, s.x, s.y)}; окружение: {WorldAtlas.Surroundings(all, s.x, s.y, named: true)}");
            if (nearPlace != null && WorldAtlas.Route(all, nearPlace, probe) is { } r)
                sb.AppendLine($"От «{nearPlace.Name}»: {WorldAtlas.Direction(s.x - nearPlace.X, s.y - nearPlace.Y)}, {r.Summary}");
            sb.AppendLine("Патч для ответа:");
            sb.Append(JsonSerializer.Serialize(patch, _writeOptions));
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Log($"add_place error: {ex.Message}");
            return $"Ошибка: {ex.Message}";
        }
    }

    // Мир игры со скрытыми местами; geo — геометрия, link — слой игры (нет — общий мир как есть).
    private (WorldMap? all, WorldMap? geo, GameWorldLink? link) LoadWorld()
    {
        string worlds = Path.Combine(Path.GetDirectoryName(config.WorldStatePath) ?? ".", "Worlds");
        GameWorldLink? link = null;
        try { link = JsonSerializer.Deserialize<WorldState>(File.ReadAllText(config.WorldStatePath, Encoding.UTF8), _readOptions)?.World; }
        catch { /* нет файла состояния — активный мир */ }
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
