using System.Text.Json;
using System.Text.Json.Nodes;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;
using NaviDnD.Migrations;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

[Collection(IconPackCollection.Name)]
public class NpcGenderPortraitTests
{
    [Fact]
    public void UnknownMapNpcUsesAnUnclassifiedFaceUntilAnExplicitFactArrives()
    {
        var previous = ArtPictures.Current;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e");
            var storage = new Storage();
            storage.ApplyUpdateWorldState("""{"map":{"entities":[{"name":"New NPC","image":"dcss/monster/human"}]}}""");
            string first = storage.WorldState.Map.Entities![0].Image!;
            Assert.Contains(first, art.PortraitsForGroup("human"));
            Assert.False(art.PortraitConflictsWithGender(first, NpcGenders.Male));
            Assert.False(art.PortraitConflictsWithGender(first, NpcGenders.Female));
            storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"gender":"male"}]}}""");
            string corrected = storage.WorldState.Map.Entities[0].Image!;
            Assert.NotEqual(first, corrected);
            Assert.True(art.PortraitConflictsWithGender(corrected, NpcGenders.Female));
        }
        finally { ArtPictures.Current = previous; }
    }

    [Fact]
    public void MapOnlyNpcGenderSurvivesPatchContextAndReloadAndCorrectsAnEarlierFace()
    {
        var previous = ArtPictures.Current;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e");
            string wrong = art.ChooseNpcPortrait("Old face", NpcGenders.Female, null, new HashSet<string>())!;
            var storage = new Storage();
            storage.ApplySavedState(JsonSerializer.Serialize(new
            {
                map = new { entities = new[] { new { name = "Тихон", gender = "male", image = wrong } } }
            }));
            var entity = Assert.Single(storage.WorldState.Map.Entities!);
            Assert.Equal(NpcGenders.Male, entity.Gender);
            Assert.NotEqual(wrong, entity.Image);
            Assert.True(art.PortraitConflictsWithGender(entity.Image, NpcGenders.Female));
            using var context = JsonDocument.Parse(new AiContextBuilder(storage.WorldState).Serialize(["map.entities"]));
            Assert.Equal("male", context.RootElement.GetProperty("map").GetProperty("entities")[0].GetProperty("gender").GetString());
            string queryPath = Path.Combine(Path.GetTempPath(), "navidnd-npc-gender-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(queryPath, JsonSerializer.Serialize(storage.WorldState));
                using var query = JsonDocument.Parse(new WorldStateTools(new McpServerConfig(queryPath, LogPath: null)).GetWorldState(["map.entities:#0"]));
                Assert.Equal("male", query.RootElement.GetProperty("map").GetProperty("entities")[0].GetProperty("gender").GetString());
            }
            finally { File.Delete(queryPath); }
            string face = entity.Image!;
            storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"aiInfo":"Another fact"}]}}""");
            Assert.Equal(face, storage.WorldState.Map.Entities![0].Image);
            var restored = new Storage();
            restored.ApplySavedState(JsonSerializer.Serialize(storage.WorldState, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            Assert.Equal(face, restored.WorldState.Map.Entities![0].Image);
            Assert.Equal(NpcGenders.Male, restored.WorldState.Map.Entities[0].Gender);
        }
        finally { ArtPictures.Current = previous; }
    }

    [Fact]
    public void NewlyKnownJournalGenderCorrectsTheMapAndThenKeepsTheSameIdentity()
    {
        var previous = ArtPictures.Current;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e");
            string wrong = art.ChooseNpcPortrait("Unknown", NpcGenders.Male, null, new HashSet<string>())!;
            var storage = new Storage();
            storage.ApplySavedState(JsonSerializer.Serialize(new
            {
                map = new { entities = new[] { new { name = "Дарья", image = wrong } } },
                narrative = new { npcs = new[] { new { name = "Дарья", portrait = wrong } } }
            }));
            Assert.Equal(wrong, storage.WorldState.Narrative!.Npcs[0].Portrait);
            storage.ApplyUpdateWorldState("""{"narrative":{"npcs":[{"id":0,"gender":"female"}]}}""");
            string corrected = storage.WorldState.Narrative.Npcs[0].Portrait!;
            Assert.NotEqual(wrong, corrected);
            Assert.True(art.PortraitConflictsWithGender(corrected, NpcGenders.Male));
            Assert.Equal(corrected, storage.WorldState.Map.Entities![0].Image);
            storage.ApplyUpdateWorldState("""{"narrative":{"npcs":[{"id":0,"notes":"Later meeting"}]}}""");
            Assert.Equal(corrected, storage.WorldState.Narrative.Npcs[0].Portrait);
        }
        finally { ArtPictures.Current = previous; }
    }

    [Fact]
    public void CorrectedCatalogLabelRepairsDaryasOldFaceButCustomFilesRemainUntouched()
    {
        var art = ArtPictures.Load("Dnd5e");
        const string mistaken = "art32/38503e1de750d500f6cb0b6d9d20df85a81043ca553cf0124daacc04282ccd42";
        Assert.True(art.PortraitConflictsWithGender(mistaken, NpcGenders.Female));
        Assert.NotEqual(mistaken, art.ChooseNpcPortrait("Дарья", NpcGenders.Female, mistaken, new HashSet<string>()));
        Assert.Equal(mistaken, art.ChooseNpcPortrait("Unknown", null, mistaken, new HashSet<string>()));
        Assert.Equal("portraits/custom.png", art.ChooseNpcPortrait("Дарья", NpcGenders.Female, "portraits/custom.png", new HashSet<string>()));
    }

    [Theory]
    [InlineData("map", "entities", "gender", "narrative", "npcs")]
    [InlineData("Map", "Entities", "Gender", "Narrative", "Npcs")]
    public void MigrationBackfillsOnlyExplicitJournalFactsAndPreservesOriginalFiles(string mapKey, string entitiesKey, string genderKey, string narrativeKey, string npcsKey)
    {
        string root = Path.Combine(Path.GetTempPath(), "navidnd-gender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var save = new JsonObject
            {
                ["formatVersion"] = 8,
                [mapKey] = new JsonObject { [entitiesKey] = new JsonArray(
                    new JsonObject { ["name"] = "Дарья", ["image"] = "portraits/custom.png" },
                    new JsonObject { ["name"] = "Тихон", ["aiInfo"] = new JsonArray("Мужчина, повар") },
                    new JsonObject { ["name"] = "Known", [genderKey] = "female" }) },
                [narrativeKey] = new JsonObject { [npcsKey] = new JsonArray(
                    new JsonObject { ["name"] = "Дарья", [genderKey] = "female" },
                    new JsonObject { ["name"] = "Known", [genderKey] = "male" }) }
            };
            string path = Path.Combine(root, "game1.json"), original = save.ToJsonString();
            File.WriteAllText(path, original);
            var result = StorageMigration.Run(root);
            Assert.True(result.Ok, string.Join('\n', result.Errors));
            Assert.Equal(original, File.ReadAllText(Path.Combine(result.BackupDir!, "game1.json")));
            var migrated = JsonNode.Parse(File.ReadAllText(path))!;
            var entities = migrated[mapKey]![entitiesKey]!.AsArray();
            Assert.Equal("female", (string?)entities[0]![genderKey]);
            Assert.Equal("portraits/custom.png", (string?)entities[0]!["image"]);
            Assert.Null(entities[1]![genderKey]);
            Assert.Equal("female", (string?)entities[2]![genderKey]);
            Assert.Equal(0, StorageMigration.Run(root).Migrated);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ArchivedLocationMigrationKeepsUnknownGenderAndAllExistingSceneData()
    {
        string root = Path.Combine(Path.GetTempPath(), "navidnd-archived-gender-" + Guid.NewGuid().ToString("N"));
        string world = Path.Combine(root, "Worlds", "test-world"), locations = Path.Combine(world, "locations");
        Directory.CreateDirectory(locations);
        try
        {
            File.WriteAllText(Path.Combine(world, "world.json"), """{"formatVersion":8,"Id":"test-world"}""");
            const string original = """{"entities":[{"name":"Тихон","image":"portraits/custom.png","aiInfo":["Мужчина, повар"]}],"snapshotTime":{"day":2},"custom":"keep"}""";
            string path = Path.Combine(locations, "inn.json");
            File.WriteAllText(path, original);
            var result = StorageMigration.Run(root);
            Assert.True(result.Ok, string.Join('\n', result.Errors));
            Assert.Equal(original, File.ReadAllText(Path.Combine(result.BackupDir!, "Worlds", "test-world", "locations", "inn.json")));
            var expected = JsonNode.Parse(original)!;
            expected["entities"]![0]!["gender"] = null;
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(path))));
        }
        finally { Directory.Delete(root, true); }
    }
}
