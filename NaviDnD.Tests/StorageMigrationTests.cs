using System.Text.Json.Nodes;
using NaviDnD.Data.Models;
using NaviDnD.Migrations;

namespace NaviDnD.Tests;

// Перевод файлов Storage к текущему формату (StorageMigration): старые файлы во временной папке → проверка полей,
// резервной копии, повторного запуска и загрузки переведённого сохранения игрой.
public class StorageMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "navidnd-migration-" + Guid.NewGuid().ToString("N"));
    private string Storage => Path.Combine(_root, "Storage");

    public StorageMigrationTests() => Directory.CreateDirectory(Storage);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private const string OldSave = """
        {"map":{"rooms":[{"name":"Комната 1","positions":[[1,1]]},{"name":"Коридор","positions":[[2,1]]},{"name":"Лаз"}]},
         "hero":{"name":"Кадрим","stats":[{"name":"Класс Доспеха","value":"19"},{"name":"Раса","value":"Высший эльф"},
           {"name":"Класс","value":"Воин 3 ур"},{"name":"Темное зрение","value":"60 фт"},{"name":"Сила","value":"+2 (15)"}],
           "resources":[{"name":"Второе дыхание","value":"1/1","category":"Способности"},{"name":"Ячейки","value":"2/2","category":"Заклинания"}]},
         "time":{"totalRounds":2,"partOfDay":"Вечер","day":3},
         "history":[{"author":"DM","text":"Привет"}]}
        """;

    private void Write(string relative, string text)
    {
        string path = Path.Combine(Storage, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private JsonObject Read(string relative) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Storage, relative)))!;

    [Fact]
    public void SaveGetsKeysPassagesLanguageAndVersion()
    {
        Write("game1.json", OldSave);
        var result = StorageMigration.Run(Storage);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(1, result.Migrated);

        var save = Read("game1.json");
        Assert.Equal(StorageFormat.Current, (int)save["formatVersion"]!);
        Assert.Equal("ru", (string?)save["language"]);
        Assert.Equal(PartsOfDay.Evening, (string?)save["time"]!["partOfDay"]);
        var stats = save["hero"]!["stats"]!.AsArray().ToDictionary(s => (string)s!["name"]!, s => (string?)s!["key"]);
        Assert.Equal(StatKeys.ArmorClass, stats["Класс Доспеха"]);
        Assert.Equal(StatKeys.Race, stats["Раса"]);
        Assert.Equal(StatKeys.Class, stats["Класс"]);
        Assert.Equal(StatKeys.Darkvision, stats["Темное зрение"]);
        Assert.Null(stats["Сила"]);
        var categories = save["hero"]!["resources"]!.AsArray().Select(r => (string?)r!["category"]).ToList();
        Assert.Equal([ResourceCategories.Abilities, ResourceCategories.Spells], categories);
        var rooms = save["map"]!["rooms"]!.AsArray().Select(r => (bool?)r!["passage"]).ToList();
        Assert.Equal([null, true, true], rooms);
        // Остальное — без изменений.
        Assert.Equal("Привет", (string?)save["history"]![0]!["text"]);
    }

    [Fact]
    public void MigratedSaveLoadsWithKeys()
    {
        Write("game1.json", OldSave);
        StorageMigration.Run(Storage);
        var storage = new Storage();
        storage.LoadFrom(Path.Combine(Storage, "game1.json"));
        var hero = storage.WorldState.Hero!;
        Assert.Equal("Высший эльф", hero.Stat(StatKeys.Race)?.Value);
        Assert.Equal("19", hero.Stat(StatKeys.ArmorClass)?.Value);
        Assert.Equal(60, hero.DarkvisionFt);
        Assert.Equal(PartsOfDay.Evening, storage.WorldState.Time.PartOfDay);
        Assert.Equal(L.Russian, storage.WorldState.Language);
        Assert.Equal(StorageFormat.Current, storage.WorldState.FormatVersion);
        Assert.True(storage.WorldState.Map.Rooms!.Single(r => r.Name == "Коридор").Passage);
    }

    [Fact]
    public void LevelAndXpLeaveStats()
    {
        Write("game1.json", """
            {"formatVersion":1,"hero":{"name":"Лира","stats":[{"key":"class","name":"Класс","value":"Волшебник 3 ур"},
              {"name":"Опыт","value":"1200/2700"},{"name":"Сила","value":"-1 (8)"}]}}
            """);
        Assert.True(StorageMigration.Run(Storage).Ok);
        var hero = Read("game1.json")["hero"]!;
        Assert.Equal(3, (int)hero["level"]!);
        Assert.Equal(1200, (int)hero["xp"]!);
        Assert.Equal(["Волшебник", "-1 (8)"], hero["stats"]!.AsArray().Select(s => (string)s!["value"]!).ToList());

        var storage = new Storage();
        storage.LoadFrom(Path.Combine(Storage, "game1.json"));
        Assert.Equal(3, storage.WorldState.Hero!.Level);
        Assert.Equal(1200, storage.WorldState.Hero.Xp);
    }

    [Fact]
    public void NotesArrayBecomesVersionedObject()
    {
        Write("game2.notes.json", """[{"Text":"Ключ у мельника","Day":2,"PartOfDay":"Ночь","Created":"2026-01-01T00:00:00","ByMaster":false}]""");
        Assert.True(StorageMigration.Run(Storage).Ok);
        var notes = Read("game2.notes.json");
        Assert.Equal(StorageFormat.Current, (int)notes["formatVersion"]!);
        var note = notes["notes"]![0]!;
        Assert.Equal("Ключ у мельника", (string?)note["text"]);
        Assert.Equal(PartsOfDay.Night, (string?)note["partOfDay"]);
        Assert.Equal(2, (int)note["day"]!);
    }

    [Fact]
    public void HeroCardMigratesEmbeddedHero()
    {
        var hero = JsonNode.Parse(OldSave)!["hero"]!.ToJsonString();
        var card = new JsonObject { ["Name"] = "Кадрим", ["Race"] = "Высший эльф", ["HeroJson"] = hero };
        Write(Path.Combine("Heroes", "кадрим.json"), card.ToJsonString());
        Assert.True(StorageMigration.Run(Storage).Ok);
        var migrated = Read(Path.Combine("Heroes", "кадрим.json"));
        Assert.Equal(StorageFormat.Current, (int)migrated["FormatVersion"]!);
        var inner = JsonNode.Parse((string)migrated["HeroJson"]!)!;
        Assert.Contains(inner["stats"]!.AsArray(), s => (string?)s!["key"] == StatKeys.Race);
        Assert.Equal(3, (int)inner["level"]!);
    }

    [Fact]
    public void WorldAndItsLocationsMigrateTogether()
    {
        Write(Path.Combine("Worlds", "world-1", "world.json"),
            """{"Id":"world-1","Name":"Мир","Features":[{"Name":"о. Тилдан","Biome":"I"},{"Name":"о. Туманный","Biome":"I"},{"Name":"Тёмный лес","Biome":"F"}]}""");
        Write(Path.Combine("Worlds", "world-1", "locations", "харцбах.json"), """{"rooms":[{"name":"Галерея"},{"name":"Холл"}]}""");
        Assert.True(StorageMigration.Run(Storage).Ok);
        var world = Read(Path.Combine("Worlds", "world-1", "world.json"));
        Assert.Equal(StorageFormat.Current, (int)world["FormatVersion"]!);
        Assert.Equal("ru", (string?)world["Language"]);
        Assert.Equal(["Остров Тилдан", "Туманный остров", "Тёмный лес"], world["Features"]!.AsArray().Select(f => (string)f!["Name"]!).ToList());
        var location = Read(Path.Combine("Worlds", "world-1", "locations", "харцбах.json"));
        Assert.Equal([true, null], location["rooms"]!.AsArray().Select(r => (bool?)r!["passage"]).ToList());
        var loaded = WorldLibrary.LoadFrom(Path.Combine(Storage, "Worlds", "world-1", "world.json"));
        Assert.Equal(L.Russian, loaded?.Language);
    }

    [Fact]
    public void BackupHoldsOriginalsAndSecondRunChangesNothing()
    {
        Write("game1.json", OldSave);
        var first = StorageMigration.Run(Storage);
        Assert.NotNull(first.BackupDir);
        Assert.Equal(OldSave, File.ReadAllText(Path.Combine(first.BackupDir!, "game1.json")));

        string migrated = File.ReadAllText(Path.Combine(Storage, "game1.json"));
        var second = StorageMigration.Run(Storage);
        Assert.Equal(0, second.Migrated);
        Assert.Null(second.BackupDir);
        Assert.Equal(migrated, File.ReadAllText(Path.Combine(Storage, "game1.json")));
        Assert.False(StorageMigration.Needed(Storage));
    }

    [Fact]
    public void CurrentFormatFilesAndOtherFilesAreNotTouched()
    {
        Write("game1.json", $$$"""{"formatVersion":{{{StorageFormat.Current}}},"time":{"partOfDay":"Утро"}}""");
        Write("game1.explored.json", "[[1,2]]");
        Write("settings.json", """{"Language":"en"}""");
        var result = StorageMigration.Run(Storage);
        Assert.Equal(0, result.Migrated);
        Assert.Equal("Утро", (string?)Read("game1.json")["time"]!["partOfDay"]);   // уже текущий формат — не трогаем
        Assert.Equal("[[1,2]]", File.ReadAllText(Path.Combine(Storage, "game1.explored.json")));
        Assert.False(Directory.Exists(Path.Combine(Storage, "Backup")));
    }

    [Fact]
    public void BrokenFileIsSkippedAndOthersStillMigrate()
    {
        Write("game1.json", OldSave);
        Write("game2.json", "{ не json");
        Write(Path.Combine("Heroes", "битый.json"), """{"Name":"x","HeroJson":"{ сломано"}""");
        var result = StorageMigration.Run(Storage);
        // Не JSON — не наш файл, пропускается; карточка с битым героем — ошибка, файл остаётся как был.
        Assert.Equal(1, result.Migrated);
        Assert.Single(result.Errors);
        Assert.Equal("{ не json", File.ReadAllText(Path.Combine(Storage, "game2.json")));
        Assert.Equal("""{"Name":"x","HeroJson":"{ сломано"}""", File.ReadAllText(Path.Combine(Storage, "Heroes", "битый.json")));
        Assert.Equal(StorageFormat.Current, (int)Read("game1.json")["formatVersion"]!);
        Assert.Empty(Directory.GetFiles(Storage, "*.migrating", SearchOption.AllDirectories));
    }

    [Fact]
    public void CommandReportsErrorsForTheGame()
    {
        Write("game1.json", OldSave);
        Write(Path.Combine("Heroes", "битый.json"), """{"Name":"x","HeroJson":"{ сломано"}""");
        Assert.Equal(1, MigrateCommand.Run(_root));
        Assert.True(File.Exists(Path.Combine(Storage, "update-error.txt")));
        Assert.Contains("migrations.log", Directory.GetFiles(Path.Combine(_root, "logs")).Select(Path.GetFileName));
    }
}
