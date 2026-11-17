using System.Text.Json.Nodes;
using NaviDnD.Data.Models;
using NaviDnD.Migrations;

namespace NaviDnD.Tests;

// Translation of Storage files to the current format (StorageMigration): old files in a temporary folder →
// checking fields, backup, re-launching and loading the translated save game.
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
        // The rest is unchanged.
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
        string explored = $$$"""{"formatVersion":{{{StorageFormat.Current}}},"locations":{}}""";
        Write("game1.explored.json", explored);
        Write("settings.json", """{"Language":"en"}""");
        var result = StorageMigration.Run(Storage);
        Assert.Equal(0, result.Migrated);
        Assert.Equal("Утро", (string?)Read("game1.json")["time"]!["partOfDay"]);   // already the current format - don’t touch it
        Assert.Equal(explored, File.ReadAllText(Path.Combine(Storage, "game1.explored.json")));
        Assert.False(Directory.Exists(Path.Combine(Storage, "Backup")));
    }

    [Theory]
    [InlineData("Chronicle", "Places")]
    [InlineData("chronicle", "places")]
    public void VersionFiveWorldInitializesAliasHistoryWithoutInventingOrReplacingIt(string chronicleKey, string placesKey)
    {
        string relative = Path.Combine("Worlds", "world-6", "world.json");
        var world = new JsonObject
        {
            ["FormatVersion"] = 5,
            [chronicleKey] = new JsonObject
            {
                [placesKey] = new JsonArray
                {
                    new JsonObject { ["Name"] = "New Capital", ["Slot"] = 0, ["Location"] = "capital.json", ["formerNames"] = new JsonArray("Old Capital") },
                    new JsonObject { ["Name"] = "Village", ["Slot"] = -1, ["Description"] = "Repaired by the hero" },
                    new JsonObject { ["Name"] = "Ruins", ["FormerNames"] = null, ["DmNotes"] = "The cache is already empty" },
                },
            },
        };
        string original = world.ToJsonString();
        Write(relative, original);
        string locationRelative = Path.Combine("Worlds", "world-6", "locations", "capital.json");
        const string location = """{"objects":[{"name":"Opened cache","aiInfo":["The key was taken"]}],"entities":[{"name":"Keeper","hp":"7/10"}]}""";
        Write(locationRelative, location);

        var result = StorageMigration.Run(Storage);

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(1, result.Migrated);
        Assert.Equal(original, File.ReadAllText(Path.Combine(result.BackupDir!, relative)));
        Assert.Equal(location, File.ReadAllText(Path.Combine(result.BackupDir!, locationRelative)));
        var migrated = Read(relative);
        Assert.Equal(StorageFormat.Current, (int)migrated["FormatVersion"]!);
        var places = migrated[chronicleKey]![placesKey]!.AsArray();
        Assert.Equal("Old Capital", (string?)Assert.Single(places[0]!["formerNames"]!.AsArray()));
        Assert.Equal("capital.json", (string?)places[0]!["Location"]);
        Assert.Empty(places[1]!["FormerNames"]!.AsArray());
        Assert.Equal("Repaired by the hero", (string?)places[1]!["Description"]);
        Assert.Empty(places[2]!["FormerNames"]!.AsArray());
        Assert.Equal("The cache is already empty", (string?)places[2]!["DmNotes"]);
        var expectedLocation = JsonNode.Parse(location)!.AsObject();
        expectedLocation["snapshotTime"] = null;
        expectedLocation["entities"]![0]!["gender"] = null; // v9 records unknown; no fact is inferred from Keeper.
        Assert.True(JsonNode.DeepEquals(expectedLocation, Read(locationRelative)));
        string after = File.ReadAllText(Path.Combine(Storage, relative));
        Assert.Equal(0, StorageMigration.Run(Storage).Migrated);
        Assert.Equal(after, File.ReadAllText(Path.Combine(Storage, relative)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VersionSixLocationAddsUnknownClockOriginAndPreservesExistingData(bool hasSnapshotTime)
    {
        string worldPath = Path.Combine("Worlds", "clock-world", "world.json");
        string locationPath = Path.Combine("Worlds", "clock-world", "locations", "ruins.json");
        const string world = """{"FormatVersion":6,"Chronicle":{"Places":[{"Name":"Ruins","FormerNames":["Citadel"],"Location":"ruins.json"}]}}""";
        var originalMap = JsonNode.Parse("""{"seed":71,"rooms":[{"name":"Hall","positions":[[1,2]]}],"entities":[{"name":"Keeper","hp":"3/9","effects":[{"name":"Poison","expiresAtRound":33}]}],"objects":[{"name":"Opened cache","aiInfo":["Empty"]}],"customMetadata":{"visited":true}}""")!.AsObject();
        if (hasSnapshotTime)
            originalMap["snapshotTime"] = JsonNode.Parse("""{"totalRounds":17,"partOfDay":"evening","day":4}""");
        string original = originalMap.ToJsonString();
        Write(worldPath, world);
        Write(locationPath, original);

        var result = StorageMigration.Run(Storage);

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(1, result.Migrated);
        Assert.Equal(world, File.ReadAllText(Path.Combine(result.BackupDir!, worldPath)));
        Assert.Equal(original, File.ReadAllText(Path.Combine(result.BackupDir!, locationPath)));
        var expected = originalMap.DeepClone().AsObject();
        if (!hasSnapshotTime) expected["snapshotTime"] = null;
        expected["entities"]![0]!["gender"] = null; // v9 leaves the archived NPC's gender unknown.
        var migrated = Read(locationPath);
        Assert.True(migrated.ContainsKey("snapshotTime"));
        Assert.True(JsonNode.DeepEquals(expected, migrated));
        Assert.Equal(StorageFormat.Current, (int)Read(worldPath)["FormatVersion"]!);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(world)!["Chronicle"], Read(worldPath)["Chronicle"]));
        string after = File.ReadAllText(Path.Combine(Storage, locationPath));

        var second = StorageMigration.Run(Storage);

        Assert.True(second.Ok);
        Assert.Equal(0, second.Migrated);
        Assert.Null(second.BackupDir);
        Assert.Equal(after, File.ReadAllText(Path.Combine(Storage, locationPath)));
        Assert.False(StorageMigration.Needed(Storage));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void VersionedExplorationPreservesEveryLocationEvenWithoutItsSave(int version)
    {
        // This is already a per-location archive, not the legacy single-map cell array.
        // A missing save must not discard earlier discoveries or restrict them to a current map.
        string original = """{"formatVersion":5,"locations":{"[\"world-a\",\"OLD CAPITAL\",\"seed:3\"]":{"cells":[[1,2],[3,4]],"seenObjects":["Cache@3,4"]},"[\"world-a\",\"CAVE\",\"seed:7\"]":{"cells":[[8,9]],"seenObjects":["Door@8,9"]},"[\"world-b\",\"OLD CAPITAL\",\"seed:3\"]":{"cells":[[15,12]],"seenObjects":null}}}""";
        original = original.Replace("\"formatVersion\":5", $"\"formatVersion\":{version}");
        Write("game1.explored.json", original);

        var result = StorageMigration.Run(Storage);

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(1, result.Migrated);
        Assert.Equal(original, File.ReadAllText(Path.Combine(result.BackupDir!, "game1.explored.json")));
        var migrated = Read("game1.explored.json");
        Assert.Equal(StorageFormat.Current, (int)migrated["formatVersion"]!);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original)!["locations"], migrated["locations"]));
        Assert.Equal(3, migrated["locations"]!.AsObject().Count);
        string after = migrated.ToJsonString();
        var second = StorageMigration.Run(Storage);
        Assert.Equal(0, second.Migrated);
        Assert.Null(second.BackupDir);
        Assert.Equal(after, Read("game1.explored.json").ToJsonString());
        Assert.False(StorageMigration.Needed(Storage));
    }

    [Fact]
    public void MalformedVersionFiveExplorationIsBackedUpWithoutBeingReset()
    {
        const string original = """{"formatVersion":5,"locations":[[1,2]]}""";
        Write("game1.explored.json", original);
        var result = StorageMigration.Run(Storage);
        Assert.False(result.Ok);
        Assert.Equal(0, result.Migrated);
        Assert.Single(result.Errors);
        Assert.Equal(original, File.ReadAllText(Path.Combine(Storage, "game1.explored.json")));
        Assert.Equal(original, File.ReadAllText(Path.Combine(result.BackupDir!, "game1.explored.json")));
    }

    [Fact]
    public void ExplorationArrayIsBoundToItsOwnSavedLocationAndBackedUp()
    {
        Write("game1.json", """{"formatVersion":4,"world":{"id":"world-a","place":"Mill","x":2,"y":3},"map":{"rooms":[{"name":"Mill hall","positions":[[1,1],[1,2]]}]}}""");
        Write("game1.explored.json", "[[1,1],[1,2]]");
        var first = StorageMigration.Run(Storage);
        Assert.True(first.Ok, string.Join("\n", first.Errors));
        Assert.Equal(2, first.Migrated);
        Assert.Equal("[[1,1],[1,2]]", File.ReadAllText(Path.Combine(first.BackupDir!, "game1.explored.json")));
        var explored = Read("game1.explored.json");
        Assert.Equal(StorageFormat.Current, (int)explored["formatVersion"]!);
        var location = Assert.Single(explored["locations"]!.AsObject());
        Assert.Equal("[[1,1],[1,2]]", location.Value!["cells"]!.ToJsonString());
        Assert.Empty(location.Value["seenObjects"]!.AsArray());
        var storage = new Storage();
        storage.LoadFrom(Path.Combine(Storage, "game1.json"));
        Assert.Contains((1, 1), storage.ExploredCells);
        Assert.Contains((1, 2), storage.ExploredCells);
        Assert.Equal(0, StorageMigration.Run(Storage).Migrated);
    }

    [Fact]
    public void ExplorationWithoutATacticalLocationDoesNotRevealTheNextLocation()
    {
        Write("game1.json", """{"formatVersion":4,"world":{"id":"world-a","x":2,"y":3},"map":{}}""");
        Write("game1.explored.json", "[[1,2]]");
        var result = StorageMigration.Run(Storage);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Empty(Read("game1.explored.json")["locations"]!.AsObject());
        Assert.Equal("[[1,2]]", File.ReadAllText(Path.Combine(result.BackupDir!, "game1.explored.json")));
    }

    [Fact]
    public void OrphanedExplorationIsArchivedWithoutLeakingIntoANewAdventure()
    {
        Write("newGameState.explored.json", "[[1,2]]");
        var result = StorageMigration.Run(Storage);
        Assert.True(result.Ok);
        Assert.Equal(1, result.Migrated);
        Assert.Empty(Read("newGameState.explored.json")["locations"]!.AsObject());
        Assert.Equal("[[1,2]]", File.ReadAllText(Path.Combine(result.BackupDir!, "newGameState.explored.json")));
        Assert.False(StorageMigration.Needed(Storage));
    }

    [Fact]
    public void ExplorationWithBrokenSaveIsPreservedForRecovery()
    {
        Write("game1.json", "{ broken");
        Write("game1.explored.json", "[[1,2]]");
        var result = StorageMigration.Run(Storage);
        Assert.False(result.Ok);
        Assert.Single(result.Errors);
        Assert.Equal(0, result.Migrated);
        Assert.Equal("[[1,2]]", File.ReadAllText(Path.Combine(Storage, "game1.explored.json")));
        Assert.Equal("[[1,2]]", File.ReadAllText(Path.Combine(result.BackupDir!, "game1.explored.json")));
    }

    [Fact]
    public void BrokenFileIsSkippedAndOthersStillMigrate()
    {
        Write("game1.json", OldSave);
        Write("game2.json", "{ не json");
        Write(Path.Combine("Heroes", "битый.json"), """{"Name":"x","HeroJson":"{ сломано"}""");
        var result = StorageMigration.Run(Storage);
        // Not JSON - not our file, skipped; card with a beaten hero - an error, the file remains as it was.
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

    [Fact]
    public void VersionThreeReservesPortraitAndPreservesAssignedFacesWithBackup()
    {
        Write("game1.json", """{"formatVersion":3,"narrative":{"npcs":[{"name":"A"},{"name":"B","portrait":"portraits/custom.png"}]}}""");
        var result = StorageMigration.Run(Storage);
        Assert.True(result.Ok);
        Assert.NotNull(result.BackupDir);
        var save = Read("game1.json");
        Assert.Equal(StorageFormat.Current, (int)save["formatVersion"]!);
        Assert.True(save["narrative"]!["npcs"]![0]!.AsObject().ContainsKey("portrait"));
        Assert.Equal("portraits/custom.png", (string?)save["narrative"]!["npcs"]![1]!["portrait"]);
        Assert.False(StorageMigration.Needed(Storage));
    }
}
