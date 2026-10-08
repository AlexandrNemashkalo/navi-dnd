using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace NaviDnD.Migrations;

// Перевод файлов данных Storage к текущему формату (StorageFormat.Current). Запускается при обновлении игры:
// помощник установщика после патчей вызывает «NaviDnD.exe --migrate <папка игры>»; запасной вызов — при старте,
// если файлы всё ещё старые (сборка из исходников, сбой обновления). Код игры читает только текущий формат.
//
// Файлы и версия (поле formatVersion, без регистра):
//   сохранения — game*.json, newGameState.json, worldState.json;  заметки — *.notes.json (старый — голый массив);
//   карточки героев — Heroes/*.json (герой внутри — строкой HeroJson);  миры — Worlds/*/world.json,
//   их локации (locations/*.json) — по версии своего мира.
// Надёжность: до первой записи — копия всех переводимых файлов в Storage/Backup/format-<N>-<время>; не вышла копия —
// ничего не трогаем. Каждый файл — через временный файл и замену (обрыв не оставит половину). Файл, который не
// разобрался, остаётся как был (ошибка — в результат). Повторный запуск ничего не меняет.
public static class StorageMigration
{
    public const int Current = StorageFormat.Current;

    public sealed record Result(int Migrated, List<string> Errors, string? BackupDir)
    {
        public bool Ok => Errors.Count == 0;
    }

    private enum Kind { Save, Notes, Hero, World }

    private sealed record Job(Kind Kind, string Path, int Version, List<string> Locations);

    private static readonly JsonSerializerOptions Write = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = false,
    };

    // Есть ли что переводить (быстро: только чтение версий).
    public static bool Needed(string storageDir) => Collect(storageDir).Any();

    public static Result Run(string storageDir)
    {
        var errors = new List<string>();
        var jobs = Collect(storageDir).ToList();
        if (jobs.Count == 0) return new(0, errors, null);

        string backup;
        try { backup = Backup(storageDir, jobs); }
        catch (Exception e)
        {
            errors.Add("Резервная копия не создана, файлы не изменены: " + e.Message);
            return new(0, errors, null);
        }

        int migrated = 0;
        foreach (var job in jobs)
        {
            try { MigrateJob(job); migrated++; }
            catch (Exception e) { errors.Add($"{System.IO.Path.GetRelativePath(storageDir, job.Path)}: {e.Message}"); }
        }
        return new(migrated, errors, backup);
    }

    // ── Какие файлы ──────────────────────────────────────────────────────────

    private static IEnumerable<Job> Collect(string storageDir)
    {
        if (!Directory.Exists(storageDir)) yield break;
        foreach (string f in Directory.EnumerateFiles(storageDir, "*.json"))
        {
            string name = System.IO.Path.GetFileName(f);
            Kind? kind = name.EndsWith(".notes.json", StringComparison.OrdinalIgnoreCase) ? Kind.Notes
                : name.EndsWith(".explored.json", StringComparison.OrdinalIgnoreCase) ? null
                : name.StartsWith("game", StringComparison.OrdinalIgnoreCase) && char.IsDigit(name[4..].FirstOrDefault())
                  || name is "newGameState.json" or "worldState.json" ? Kind.Save
                : null;
            if (kind is { } k && VersionOf(f) is int v && v < Current) yield return new(k, f, v, []);
        }
        string heroes = System.IO.Path.Combine(storageDir, "Heroes");
        if (Directory.Exists(heroes))
            foreach (string f in Directory.EnumerateFiles(heroes, "*.json"))
                if (VersionOf(f) is int v && v < Current) yield return new(Kind.Hero, f, v, []);
        string worlds = System.IO.Path.Combine(storageDir, "Worlds");
        if (Directory.Exists(worlds))
            foreach (string dir in Directory.EnumerateDirectories(worlds))
            {
                string world = System.IO.Path.Combine(dir, "world.json");
                if (!File.Exists(world) || VersionOf(world) is not int v || v >= Current) continue;
                string locations = System.IO.Path.Combine(dir, "locations");
                var list = Directory.Exists(locations) ? Directory.EnumerateFiles(locations, "*.json").ToList() : [];
                yield return new(Kind.World, world, v, list);
            }
    }

    // Версия файла: поле formatVersion (без регистра), нет поля или массив — 0; не JSON — null (пропуск).
    private static int? VersionOf(string path)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (node is JsonArray) return 0;
            if (node is not JsonObject o) return null;
            return Get(o, "formatVersion") is JsonValue v && v.TryGetValue(out int n) ? n : 0;
        }
        catch { return null; }
    }

    // ── Резервная копия ──────────────────────────────────────────────────────

    private static string Backup(string storageDir, List<Job> jobs)
    {
        string dir = System.IO.Path.Combine(storageDir, "Backup", $"format-{jobs.Min(j => j.Version)}-{DateTime.Now:yyyyMMdd-HHmmss}");
        foreach (string file in jobs.SelectMany(j => j.Locations.Prepend(j.Path)))
        {
            string target = System.IO.Path.Combine(dir, System.IO.Path.GetRelativePath(storageDir, file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        return dir;
    }

    // ── Перевод файла ────────────────────────────────────────────────────────

    private static void MigrateJob(Job job)
    {
        var root = JsonNode.Parse(File.ReadAllText(job.Path, Encoding.UTF8))
                   ?? throw new InvalidDataException("пустой файл");
        // Сначала локации мира (если упадут — мир останется старым и переведётся снова целиком).
        foreach (string location in job.Locations)
        {
            var map = JsonNode.Parse(File.ReadAllText(location, Encoding.UTF8)) as JsonObject
                      ?? throw new InvalidDataException("локация не объект: " + System.IO.Path.GetFileName(location));
            for (int v = job.Version; v < Current; v++) Steps[v].Location(map);
            Save(location, map);
        }
        for (int v = job.Version; v < Current; v++)
        {
            var step = Steps[v];
            root = job.Kind switch
            {
                Kind.Save => step.Save(AsObject(root)),
                Kind.Notes => step.Notes(root),
                Kind.Hero => step.Hero(AsObject(root)),
                _ => step.World(AsObject(root)),
            };
        }
        Set(AsObject(root), "formatVersion", Current, job.Kind is Kind.Hero or Kind.World ? "FormatVersion" : "formatVersion");
        Save(job.Path, root);
    }

    // Запись через временный файл рядом и замену — оригинал не теряется при обрыве.
    private static void Save(string path, JsonNode node)
    {
        string tmp = path + ".migrating";
        File.WriteAllText(tmp, node.ToJsonString(Write), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private static JsonObject AsObject(JsonNode node) => node as JsonObject ?? throw new InvalidDataException("ожидался объект");

    // ── Шаги: Steps[v] — из версии v в v+1 ───────────────────────────────────

    private sealed record Step(
        Func<JsonObject, JsonObject> Save,
        Func<JsonNode, JsonNode> Notes,
        Func<JsonObject, JsonObject> Hero,
        Func<JsonObject, JsonObject> World,
        Action<JsonObject> Location);

    private static readonly Step[] Steps = [Migration1.Step];

    private static class Migration1
    {
        // 0 → 1: ключи вместо русских слов (часть суток, особые статы героя, категории ресурсов), проходы-комнаты
        // отмечены флагом passage, язык игры и мира — русский (до выбора языка все игры были русскими).
        public static readonly Step Step = new(SaveFile, NotesFile, HeroCard, WorldFile, map => Rooms(map));

        private static JsonObject SaveFile(JsonObject save)
        {
            Set(save, "language", L.Russian);
            if (Get(save, "time") is JsonObject time) PartOfDay(time);
            if (Get(save, "hero") is JsonObject hero) Hero(hero);
            if (Get(save, "map") is JsonObject map) Rooms(map);
            return save;
        }

        private static JsonNode NotesFile(JsonNode notes)
        {
            var list = notes as JsonArray ?? Get(AsObject(notes), "notes") as JsonArray ?? [];
            var result = new JsonArray();
            foreach (var item in list.ToList())
            {
                if (item is not JsonObject note) continue;
                list.Remove(note);
                // Прежние заметки писались с именами полей как в C# (Text, PartOfDay) — теперь camelCase.
                var fresh = new JsonObject();
                foreach (var (key, value) in note.ToList())
                {
                    note.Remove(key);
                    fresh[char.ToLowerInvariant(key[0]) + key[1..]] = value;
                }
                PartOfDay(fresh);
                result.Add(fresh);
            }
            return new JsonObject { ["formatVersion"] = Current, ["notes"] = result };
        }

        private static JsonObject HeroCard(JsonObject card)
        {
            if (Get(card, "HeroJson") is JsonValue v && v.TryGetValue(out string? json) && JsonNode.Parse(json) is JsonObject hero)
                Set(card, "HeroJson", Hero(hero).ToJsonString(Write));
            return card;
        }

        private static JsonObject WorldFile(JsonObject world)
        {
            if (Get(world, "Language") is not JsonValue) Set(world, "Language", L.Russian);
            // Острова с сокращёнными именами (были недолго) — полностью: «о. Тилдан» → «Остров Тилдан»,
            // «о. Туманный» → «Туманный остров» (раньше это делала загрузка мира).
            if (Get(world, "Features") is JsonArray features)
                foreach (var f in features.OfType<JsonObject>())
                    if (Get(f, "Biome") is JsonValue b && b.ToString() == "I"
                        && Get(f, "Name") is JsonValue n && n.TryGetValue(out string? name) && name.StartsWith("о. "))
                    {
                        string rest = name[3..];
                        Set(f, "Name", rest.EndsWith("ый") || rest.EndsWith("ий") || rest.EndsWith("ой") ? $"{rest} остров" : $"Остров {rest}");
                    }
            return world;
        }

        // Ключи статов и категорий ресурсов — по тем же правилам, что Storage для ответов мастера.
        private static JsonObject Hero(JsonObject hero)
        {
            if (Get(hero, "stats") is JsonArray stats)
                foreach (var s in stats.OfType<JsonObject>())
                    if (Get(s, "key") is null && StatKeys(s) is { } key) Set(s, "key", key);
            if (Get(hero, "resources") is JsonArray resources)
                foreach (var r in resources.OfType<JsonObject>())
                    if (Get(r, "category") is JsonValue c && c.TryGetValue(out string? category))
                        Set(r, "category", Data.Models.ResourceCategories.Normalize(category));
            return hero;
        }

        private static string? StatKeys(JsonObject stat) =>
            Get(stat, "name") is JsonValue n && n.TryGetValue(out string? name) ? Data.Models.StatKeys.FromName(name) : null;

        private static void PartOfDay(JsonObject holder)
        {
            if (Get(holder, "partOfDay") is JsonValue v && v.TryGetValue(out string? value))
                Set(holder, "partOfDay", Data.Models.PartsOfDay.Normalize(value));
        }

        // Проходы прежних генераторов назывались «Коридор», «Лаз», «Галерея» (до перевода — «Corridor»).
        private static readonly string[] PassageNames = ["Коридор", "Лаз", "Галерея", "Corridor"];

        private static void Rooms(JsonObject map)
        {
            if (Get(map, "rooms") is not JsonArray rooms) return;
            foreach (var room in rooms.OfType<JsonObject>())
                if (Get(room, "name") is JsonValue n && n.TryGetValue(out string? name) && PassageNames.Contains(name))
                    Set(room, "passage", true);
        }
    }

    // ── JSON без учёта регистра имён (сохранения — camelCase, миры и карточки — как в C#) ─────────────

    private static JsonNode? Get(JsonObject o, string name) =>
        o.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    // Значение в существующее поле (с его написанием) или новое поле с именем newName ?? name.
    private static void Set(JsonObject o, string name, JsonNode? value, string? newName = null)
    {
        string? existing = o.Select(p => p.Key).FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
        o[existing ?? newName ?? name] = value;
    }

    private static void Set(JsonObject o, string name, string? value) => Set(o, name, value == null ? null : JsonValue.Create(value));
    private static void Set(JsonObject o, string name, bool value) => Set(o, name, JsonValue.Create(value));
    private static void Set(JsonObject o, string name, int value, string newName) => Set(o, name, JsonValue.Create(value), newName);
}
