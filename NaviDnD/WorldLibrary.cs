using System.Text;
using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD;

// Библиотека миров: Storage/Worlds/<id>/world.json — геометрия мира и его хроника (лор и места, общие для всех
// игр этого мира), Storage/Worlds/<id>/locations/*.json — сохранённые локации мест. Активный мир
// (Storage/Worlds/active.txt) — для новой игры по умолчанию и для старых сохранений без мира игры.
public static class WorldLibrary
{
    public static string Dir => Path.Combine(AppConfig.ProjectRoot, "Storage", "Worlds");
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

    // Мир по id (мир игры) — читается с диска один раз за запуск. Не создаёт новый.
    public static WorldMap? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_loaded.TryGetValue(id, out var w)) return w;
        w = LoadFrom(WorldPath(id));
        if (w != null) _loaded[id] = w;
        return w;
    }

    // Сделать мир активным (по умолчанию для новой игры).
    public static void SetActive(string id)
    {
        if (Get(id) is not { } w) return;
        _current = w;
        try { File.WriteAllText(ActivePath, id); } catch { }
        Changed?.Invoke();
    }

    // Миры библиотеки для анкеты новой игры: активный и те, где уже играли (есть хроника) — пустые миры
    // (отладка, F12) не показываются. id, название (хроники или генератора), размер, мест в хронике.
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

    // Есть ли в файле мира хроника (без разбора всего JSON): у пустой — ни имени, ни королевств.
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

    // Чтение файла мира (и MCP-сервером — по своему пути); старые миры дополняются (острова, места не на воде).
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
        catch { /* не сохранилось — изменения живут до конца сеанса */ }
        _loaded[world.Id] = world;
        Changed?.Invoke();
    }

    // Мир, сгенерированный заранее (анкета новой игры: превью до выбора), — в библиотеку и активным.
    public static void Register(WorldMap world)
    {
        Save(world);
        try { File.WriteAllText(ActivePath, world.Id); } catch { }
        _current = world;
        Changed?.Invoke();
    }

    // Игры (слоты «Мои игры»), которые играют в этом мире: номер и имя героя. Сохранение без мира игры — в
    // активном мире.
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
                    list.Add((slot, (string?)(root?["hero"]?["name"] ?? root?["Hero"]?["Name"]) ?? L.F("игра {0}", slot)));
            }
            catch { list.Add((slot, L.F("игра {0}", slot))); }   // не прочиталось — лучше не удалять
        }
        return list;
    }

    // Удалить мир с его локациями (проверка, что в нём нет игр, — GamesUsing, до вызова).
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

    // Новый мир: сгенерировать, сохранить, сделать активным.
    public static WorldMap Create(string size, int seed)
    {
        var world = WorldGenerator.Generate(new WorldGenerator.Options(seed, size, Language: L.Language));
        try
        {
            Directory.CreateDirectory(Path.Combine(Dir, world.Id));
            File.WriteAllText(WorldPath(world.Id), JsonSerializer.Serialize(world), Encoding.UTF8);
            File.WriteAllText(ActivePath, world.Id);
        }
        catch { /* не сохранилось — мир живёт до конца сеанса */ }
        _loaded[world.Id] = world;
        _current = world;
        Changed?.Invoke();
        return world;
    }
}
