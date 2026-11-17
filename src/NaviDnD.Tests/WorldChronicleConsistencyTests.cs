using System.Reflection;
using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.Clients;

namespace NaviDnD.Tests;

public class WorldChronicleConsistencyTests
{
    [Fact]
    public void AddingExistingSecretPlaceRevealsItWithoutDuplicatingOrOverwritingHistory()
    {
        using var fixture = new TestWorld();
        fixture.World.Chronicle.Places = [new() { Name = "Old Mill", Type = "ruins", X = 2, Y = 1,
            Description = "Repaired by villagers", DmNotes = "The keeper shelters a fugitive" }];
        WorldLibrary.Save(fixture.World);
        var storage = new Storage();
        storage.WorldState.World = new() { Id = fixture.World.Id, X = 0, Y = 0 };
        var otherHero = new WorldState { World = new() { Id = fixture.World.Id, X = 0, Y = 0 } };
        string path = Path.Combine(Path.GetDirectoryName(Storage.SavePath)!, fixture.World.Id + ".json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(storage.WorldState));
            var tools = new NaviDnD.McpServer.WorldQueryTools(new NaviDnD.McpServer.McpServerConfig(path, LogPath: null));
            string result = tools.AddPlace("old mill", "village", "An incompatible new story");
            Assert.Contains("Patch for the response:", result);
            string patch = result.Split("Patch for the response:")[1].Trim();
            storage.ApplyUpdateWorldState(patch);
            var place = Assert.Single(GameWorld.ForDisplay(storage.WorldState).Places);
            Assert.Equal("Old Mill", place.Name);
            Assert.Equal("ruins", place.Type);
            Assert.Equal("Repaired by villagers", place.Description);
            Assert.Null(place.DmNotes);
            Assert.Single(fixture.World.Chronicle.Places);
            Assert.Equal("The keeper shelters a fugitive", fixture.World.Chronicle.Places[0].DmNotes);
            Assert.Empty(GameWorld.ForDisplay(otherHero).Places);
            Assert.DoesNotContain("Patch for the response:", tools.AddPlace("Old Mill", "ruins", "Plan", hidden: true));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void JournalNotifiesAboutChangedKnownPlaceButNotSecretPlansOrTravelPosition()
    {
        using var fixture = new TestWorld();
        fixture.World.Chronicle.Places = [
            new() { Name = "Mill", Type = "ruins", X = 2, Y = 1, Description = "Abandoned" },
            new() { Name = "Secret tower", Type = "ruins", X = 3, Y = 1 }];
        var state = new WorldState { World = new() { Id = fixture.World.Id, X = 0, Y = 0, Known = ["Mill"] } };
        string before = Display.JournalDisplay.Stamp(state);
        using var secret = JsonDocument.Parse("""{"places":[{"name":"Mill","dmNotes":"Keeper hides a fugitive"},{"name":"Secret tower","description":"New secret occupant","hidden":true}]}""");
        GameWorld.Apply(state, secret.RootElement);
        Assert.Equal(before, Display.JournalDisplay.Stamp(state));
        state.World.X = 1;
        state.World.Version++;
        Assert.Equal(before, Display.JournalDisplay.Stamp(state));
        using var repair = JsonDocument.Parse("""{"places":[{"name":"Mill","description":"Repaired by villagers"}]}""");
        GameWorld.Apply(state, repair.RootElement);
        Assert.NotEqual(before, Display.JournalDisplay.Stamp(state));
        Assert.Equal("Repaired by villagers", Assert.Single(GameWorld.ForDisplay(state).Places).Description);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StalePlaceNameRecallsOnlyKnownUnambiguousLocation(bool known)
    {
        using var fixture = new TestWorld();
        fixture.World.Chronicle.Places.Add(new() { Name = "New name", Type = "ruins", X = 2, Y = 1, DmNotes = "local-record-marker" });
        var state = new WorldState { World = new() { Id = fixture.World.Id, Place = "Old name", X = 2, Y = 1, Known = known ? ["New name"] : [] } };
        var builder = new AiContextBuilder(state);
        Assert.Equal(known, builder.MinimalState("Wait").Contains("local-record-marker"));
        fixture.World.Chronicle.Places.Add(new() { Name = "Another place", Type = "ruins", X = 2, Y = 1 });
        state.World.Known.Add("Another place");
        state.World.Version++;
        if (known) Assert.DoesNotContain("local-record-marker", builder.MinimalState("Wait"));
    }

    [Fact]
    public void OrdinaryTurnsAndPopulationRememberWorldRulesWithoutRevealingOtherPlaces()
    {
        using var fixture = new TestWorld();
        fixture.World.Chronicle.Description = "People honor guest rights";
        fixture.World.Chronicle.DmNotes = "Magic consumes memories";
        fixture.World.Chronicle.Places =
        [
            new() { Name = "Mill", Type = "ruins", X = 2, Y = 1, Description = "Rebuilt after the flood", DmNotes = "The keeper shelters a fugitive" },
            new() { Name = "Secret tower", Type = "ruins", X = 3, Y = 1, DmNotes = "unrelated distant secret" },
        ];
        var state = new WorldState { World = new() { Id = fixture.World.Id, Place = "Mill", X = 2, Y = 1, Known = ["Mill"] } };
        var builder = new AiContextBuilder(state);
        foreach (var context in new[] { builder.MinimalState("Look around"), builder.PopulationState() })
        {
            Assert.Contains("World memory (GM only", context);
            Assert.Contains("Magic consumes memories", context);
            Assert.Contains("Rebuilt after the flood", context);
            Assert.Contains("The keeper shelters a fugitive", context);
            Assert.DoesNotContain("unrelated distant secret", context);
        }
        Assert.Equal(["Mill"], state.World.Known);
        Assert.Single(GameWorld.ForDisplay(state).Places);
        Assert.Null(GameWorld.ForDisplay(state).DmNotes);
        fixture.World.Chronicle.DmNotes = new string('x', 20000);
        using var rename = JsonDocument.Parse("""{"name":"Changed"}""");
        GameWorld.ApplyChronicle(fixture.World, rename.RootElement);
        var longContext = builder.MinimalState("Wait");
        Assert.Contains("[truncated; world_query world]", longContext);
        Assert.Equal(20000, fixture.World.Chronicle.DmNotes.Length); // Only the model excerpt is shortened.
        int start = longContext.IndexOf("## World memory", StringComparison.Ordinal);
        int end = longContext.IndexOf("## Player action", start, StringComparison.Ordinal);
        Assert.InRange(end - start, 1, 2050);
    }

    [Fact]
    public void ChronicleEditsRefreshBothHeroAndMasterViews()
    {
        var world = new WorldMap { Name = "Old world" };
        var link = new GameWorldLink();
        Assert.Equal("Old world", GameWorld.Compose(world, link, false).Name);
        Assert.Equal("Old world", GameWorld.Compose(world, link, true).Name);
        using var patch = JsonDocument.Parse("""{"name":"Living world","description":"The bridge is rebuilt","dmNotes":"The toll keeper owes a favor"}""");

        GameWorld.ApplyChronicle(world, patch.RootElement);

        var visible = GameWorld.Compose(world, link, false);
        var master = GameWorld.Compose(world, link, true);
        Assert.Equal("Living world", visible.Name);
        Assert.Equal("The bridge is rebuilt", visible.Description);
        Assert.Null(visible.DmNotes);
        Assert.Equal("The toll keeper owes a favor", master.DmNotes);
    }

    [Fact]
    public void OnePatchCanCreateAHiddenPlaceAndMoveTheHeroThereAfterCachedQuery()
    {
        using var fixture = new TestWorld();
        var state = new WorldState { World = new() { Id = fixture.World.Id, X = 0, Y = 0 } };
        Assert.Empty(GameWorld.ForMaster(state)!.Places);
        using var patch = JsonDocument.Parse("""{"places":[{"name":"Old mill","type":"ruins","x":2,"y":1,"hidden":true}],"place":"Old mill"}""");

        GameWorld.Apply(state, patch.RootElement);

        Assert.Equal("Old mill", state.World.Place);
        Assert.Equal((2, 1), GameWorld.HeroTile(state));
        Assert.Contains("Old mill", state.World.Visited);
        Assert.Single(GameWorld.ForDisplay(state).Places, p => p.Name == "Old mill");
    }

    [Fact]
    public void SharedChronicleChangesReachAnotherHeroWithoutRevealingSecrets()
    {
        using var fixture = new TestWorld();
        fixture.World.Chronicle.Places.Add(new() { Name = "Mill", Type = "ruins", X = 2, Y = 1, Description = "Abandoned" });
        var observer = new WorldState { World = new() { Id = fixture.World.Id, Known = ["Mill"] } };
        Assert.Equal("Abandoned", Assert.Single(GameWorld.ForDisplay(observer).Places).Description);
        var actor = new WorldState { World = new() { Id = fixture.World.Id } };
        using var patch = JsonDocument.Parse("""{"places":[{"name":"Mill","description":"Restored","dmNotes":"A hidden cellar"}]}""");

        GameWorld.Apply(actor, patch.RootElement);

        var place = Assert.Single(GameWorld.ForDisplay(observer).Places);
        Assert.Equal("Restored", place.Description);
        Assert.Null(place.DmNotes);
        Assert.Equal("A hidden cellar", Assert.Single(GameWorld.ForMaster(observer)!.Places).DmNotes);
    }

    private sealed class TestWorld : IDisposable
    {
        private readonly Dictionary<string, WorldMap> worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary)
            .GetField("_loaded", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        public WorldMap World { get; } = new()
        {
            Id = "chronicle-test-" + Guid.NewGuid().ToString("N"), Width = 4, Height = 2,
            Biomes = ["....", "...."], Roads = ["2222", "2222"], Rivers = ["0000", "0000"],
            Owners = ["....", "...."], Heights = ["1111", "1111"],
        };

        public TestWorld() => worlds.Add(World.Id, World);

        public void Dispose()
        {
            worlds.Remove(World.Id);
            var directory = Path.Combine(WorldLibrary.Dir, World.Id);
            if (Directory.Exists(directory))
            {
                File.Delete(Path.Combine(directory, "world.json"));
                // Windows may briefly report a nonempty directory immediately after the file deletion.
                // Do not recursively remove unexpected files or hide a persistent cleanup failure.
                for (int attempt = 0; ; attempt++)
                {
                    try { Directory.Delete(directory); break; }
                    catch (IOException) when (attempt < 3) { Thread.Sleep(30); }
                }
            }
        }
    }
}
