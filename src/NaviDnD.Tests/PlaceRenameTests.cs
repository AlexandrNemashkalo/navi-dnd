using System.Reflection;
using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

public class PlaceRenameTests : IDisposable
{
    private readonly string id = "rename-world-" + Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, WorldMap> worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary)
        .GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    private readonly WorldMap world;
    private string StatePath => Path.Combine(Path.GetDirectoryName(Storage.SavePath)!, id + ".json");

    public PlaceRenameTests()
    {
        world = new() { Id = id, Width = 8, Height = 4,
            Biomes = Enumerable.Repeat("........", 4).ToList(), Roads = Enumerable.Repeat("11111111", 4).ToList(),
            Rivers = Enumerable.Repeat("00000000", 4).ToList(), Owners = Enumerable.Repeat("aaaabbbb", 4).ToList(),
            Places = [new() { Name = "Generated North", Type = WorldPlaceTypes.Capital, X = 1, Y = 1 },
                new() { Name = "Generated South", Type = WorldPlaceTypes.Capital, X = 6, Y = 1 }],
            Kingdoms = [new() { Name = "North", Capital = 0 }, new() { Name = "South", Capital = 1 }],
            Chronicle = new() { Name = "Original world", Places = [
                new() { Name = "Old Harbor", Type = WorldPlaceTypes.Capital, Slot = 0, X = 1, Y = 1, Location = "harbor.json", Description = "The repaired port" },
                new() { Name = "South Gate", Type = WorldPlaceTypes.Capital, Slot = 1, X = 6, Y = 1 },
                new() { Name = "Old Tower", FormerNames = ["Ancient Tower"], Type = WorldPlaceTypes.Ruins, X = 4, Y = 2 }] } };
        worlds[id] = world;
    }

    private WorldState Hero() => new() { World = new() { Id = id, Place = "Old Harbor", X = 1, Y = 1,
        Known = ["Old Harbor", "old harbor"], Visited = ["Old Harbor"] }, Hero = new() { Position = [1, 8] } };

    private void Rename(string name)
    {
        using var patch = JsonDocument.Parse(JsonSerializer.Serialize(new { kingdoms = new[] { new { id = 0, capital = name } } }));
        Assert.True(GameWorld.ApplyChronicle(world, patch.RootElement));
    }

    [Fact]
    public void OtherHeroAndCachedViewsResolveHistoricalNamesWithoutRewritingTheSave()
    {
        var first = Hero();
        string secondSave = JsonSerializer.Serialize(Hero());
        GameWorld.ForDisplay(first);
        Rename("New Harbor");
        Assert.Same(world, GameWorld.Geo(first));
        Assert.Equal("New Harbor", first.World!.Place);
        Assert.Equal(new[] { "New Harbor" }, first.World.Known);
        Assert.Equal(new[] { "New Harbor" }, first.World.Visited);
        var restored = new Storage();
        restored.ApplySavedState(secondSave);
        var view = GameWorld.ForDisplay(restored.WorldState);
        Assert.Equal("New Harbor", restored.WorldState.World!.Place);
        Assert.Equal("New Harbor", WorldAtlas.FindPlace(view, "OLD HARBOR")!.Name);
        Assert.Null(WorldAtlas.FindPlace(view, "Old Harbors"));
        int version = restored.WorldState.World.Version;
        GameWorld.ForDisplay(restored.WorldState);
        Assert.Equal(version, restored.WorldState.World.Version);
        Assert.Equal("harbor.json", world.Chronicle.Places[0].Location);
    }

    [Theory]
    [InlineData("South Gate")]
    [InlineData("Ancient Tower")]
    public void ConflictingCapitalNameRejectsTheWholeChroniclePatch(string conflicting)
    {
        string before = JsonSerializer.Serialize(world.Chronicle);
        using var patch = JsonDocument.Parse(JsonSerializer.Serialize(new { name = "Must not change",
            kingdoms = new[] { new { id = 0, name = "Also must not change", capital = conflicting } } }));
        Assert.False(GameWorld.ApplyChronicle(world, patch.RootElement));
        Assert.Equal(before, JsonSerializer.Serialize(world.Chronicle));
    }

    [Fact]
    public void TwoNewCapitalNamesCannotClaimTheSameName()
    {
        string before = JsonSerializer.Serialize(world.Chronicle);
        using var patch = JsonDocument.Parse("""{"name":"Changed","kingdoms":[{"id":0,"capital":"Shared Name"},{"id":1,"capital":"shared name"}]}""");
        GameWorld.Apply(Hero(), patch.RootElement);
        Assert.Equal(before, JsonSerializer.Serialize(world.Chronicle));
    }

    [Fact]
    public void RepeatedRenameAndOldNamePatchPreserveOnePlaceAndItsHistory()
    {
        Rename("New Harbor");
        Rename("Bright Harbor");
        var hero = Hero();
        using var patch = JsonDocument.Parse("""{"places":[{"name":"Old Harbor","description":"A lasting repair","hidden":false,"formerNames":["Injected name"]}]}""");
        GameWorld.Apply(hero, patch.RootElement);
        Assert.Equal(3, world.Chronicle.Places.Count);
        Assert.Equal("A lasting repair", GameWorld.ResolvePlace(world, "New Harbor")!.Description);
        Assert.Null(GameWorld.ResolvePlace(world, "Injected name"));
        Rename("Old Harbor");
        Assert.Equal("Old Harbor", GameWorld.ResolvePlace(world, "Bright Harbor")!.Name);
        Assert.Equal("Old Harbor", GameWorld.ResolvePlace(world, "New Harbor")!.Name);
        Assert.DoesNotContain("Old Harbor", world.Chronicle.Places[0].FormerNames);
    }

    [Fact]
    public void FirstCapitalNamingRemembersItsGeneratorName()
    {
        world.Chronicle.Places.RemoveAt(0);
        Rename("New Harbor");
        Assert.Equal("New Harbor", GameWorld.ResolvePlace(world, "Generated North")!.Name);
    }

    [Fact]
    public void McpQueriesAndAddPlaceAcceptOldNamesWithoutDuplicatingThePlace()
    {
        Rename("New Harbor");
        Assert.True(WorldLibrary.Save(world));
        var state = Hero();
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        var tools = new WorldQueryTools(new(StatePath, LogPath: null));
        Assert.Contains("New Harbor", tools.WorldQuery("place:Old Harbor"));
        Assert.Contains("South Gate", tools.WorldQuery("near:Old Harbor"));
        Assert.Contains("New Harbor", tools.WorldQuery("route:Old Harbor;South Gate"));
        string result = tools.AddPlace("Old Harbor", "village", "Do not replace the port");
        Assert.Contains("already exists", result);
        string patch = result.Split("Patch for the response:\n")[1];
        Assert.Contains("New Harbor", patch);
        using var document = JsonDocument.Parse(patch);
        GameWorld.Apply(state, document.RootElement.GetProperty("world"));
        Assert.Equal(3, world.Chronicle.Places.Count);
        Assert.Equal("The repaired port", world.Chronicle.Places[0].Description);
    }

    [Fact]
    public void RenamedCapitalKeepsItsSavedSceneAcrossDepartureAndReturn()
    {
        var storage = new Storage();
        storage.WorldState.World = Hero().World;
        storage.WorldState.Hero = Hero().Hero;
        storage.WorldState.Map = new() { Chunks = [new() { Seed = 17, Theme = "forest", Populated = true,
            Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList(),
            Exits = [new() { Side = ChunkSide.Left, Offset = 8, External = true }] }],
            Objects = [new() { Name = "Opened cache", Position = [4, 4], AiInfo = ["The key was taken"] }],
            Entities = [new() { Name = "Resident", Position = [7, 7], Hp = "3/10" }] };
        Rename("New Harbor");
        Assert.True(GameWorld.LeaveLocation(storage.WorldState));
        Assert.Equal("New Harbor", storage.WorldState.World!.Place);
        string snapshot = GameWorld.SavedLocationJson(world, "Old Harbor")!;
        Assert.True(LocationRestoration.TryRestore(storage, snapshot));
        Assert.Equal("3/10", Assert.Single(storage.WorldState.Map.Entities!).Hp);
        Assert.Contains("The key was taken", Assert.Single(storage.WorldState.Map.Objects!).AiInfo!);
        Assert.Equal("harbor.json", world.Chronicle.Places[0].Location);
    }

    public void Dispose()
    {
        worlds.Remove(id);
        File.Delete(StatePath);
        string locations = WorldLibrary.LocationsDir(id);
        if (Directory.Exists(locations))
        {
            foreach (string file in Directory.GetFiles(locations)) File.Delete(file);
            Directory.Delete(locations);
        }
        string directory = Path.Combine(WorldLibrary.Dir, id);
        if (Directory.Exists(directory)) { File.Delete(Path.Combine(directory, "world.json")); Directory.Delete(directory); }
    }
}
