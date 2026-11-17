using System.Reflection;
using System.Text.Json;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class PlaceRenameExplorationTests
{
    private sealed class TestWorld : IDisposable
    {
        private readonly Dictionary<string, WorldMap> worlds =
            (Dictionary<string, WorldMap>)typeof(WorldLibrary).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        public WorldMap World { get; } = new()
        {
            Id = "rename-fog-" + Guid.NewGuid().ToString("N"), Width = 1, Height = 1,
            Biomes = ["."], Owners = ["a"], Roads = ["0"], Rivers = ["0"],
            Kingdoms = [new() { Name = "Country", Capital = 0 }],
            Places = [new() { Name = "Oldhaven", Type = WorldPlaceTypes.Capital, X = 0, Y = 0 }],
            Chronicle = new() { Places = [new() { Name = "Oldhaven", Type = WorldPlaceTypes.Capital, Slot = 0, X = 0, Y = 0 }] },
        };

        public TestWorld() => worlds.Add(World.Id, World);

        public void Rename(string name)
        {
            using var patch = JsonDocument.Parse(JsonSerializer.Serialize(new { kingdoms = new[] { new { id = 0, capital = name } } }));
            Assert.True(GameWorld.ApplyChronicle(World, patch.RootElement));
        }

        public Storage Hero(string name = "Oldhaven", int seed = 42)
        {
            var storage = new Storage();
            storage.WorldState.World = new() { Id = World.Id, Place = name, Known = [name], Visited = [name] };
            storage.WorldState.Hero = new() { Name = "Explorer", Position = [4, 5] };
            storage.WorldState.Map = new() { Chunks = [new() { Seed = seed, Theme = "forest", Populated = true }] };
            return storage;
        }

        public void Dispose() => worlds.Remove(World.Id);
    }

    [Fact]
    public void LiveRepeatedRenameKeepsFogVisibilityAndCreatureReactions()
    {
        using var fixture = new TestWorld();
        var storage = fixture.Hero();
        storage.SelectExplorationLocation();
        storage.ExploredCells.Add((4, 5));
        storage.SeenObjects = ["Chest@4,5"];
        storage.VisibleCells.Add((4, 5));
        storage.WorldState.Hero!.SpeedMax = 30;
        storage.WorldState.Hero.SpeedLeft = 30;
        storage.ApplyClientMovement(5, isDiagonal: true);
        Assert.True(storage.TryMarkOpportunityAttackOnHero("GUARD", 12));
        Assert.True(storage.UpdateEntitySeesHero("GUARD", true));

        foreach (string name in new[] { "Newhaven", "Bright Haven", "Oldhaven" })
        {
            fixture.Rename(name);
            storage.SelectExplorationLocation();
            Assert.Equal(name, storage.WorldState.World!.Place);
            Assert.Contains((4, 5), storage.ExploredCells);
            Assert.Contains("Chest@4,5", storage.SeenObjects!);
            Assert.Contains((4, 5), storage.VisibleCells);
            Assert.Equal(1, storage.DiagonalUsed);
            Assert.False(storage.TryMarkOpportunityAttackOnHero("GUARD", 12));
            Assert.False(storage.UpdateEntitySeesHero("GUARD", true));
        }
    }

    [Fact]
    public void OldAndCurrentKeysMergeOnlyWithinTheSameWorldAndGeometry()
    {
        using var fixture = new TestWorld();
        fixture.Rename("Newhaven");
        string otherWorld = "unrelated-" + Guid.NewGuid().ToString("N");
        static string Key(string world, string name, int seed) => JsonSerializer.Serialize(new[] { world, name.ToUpperInvariant(), $"seed:{seed}" });
        var document = new ExplorationMemory.Document
        {
            Locations = new()
            {
                [Key(fixture.World.Id, "Oldhaven", 42)] = new() { Cells = [[1, 1]], SeenObjects = ["Old clue@1,1"] },
                [Key(fixture.World.Id, "Newhaven", 42)] = new() { Cells = [[2, 2]], SeenObjects = ["New clue@2,2"] },
                [Key(fixture.World.Id, "Oldhaven", 43)] = new() { Cells = [[3, 3]], SeenObjects = ["Other map@3,3"] },
                [Key(otherWorld, "Oldhaven", 42)] = new() { Cells = [[9, 9]], SeenObjects = ["Other world@9,9"] },
            },
        };
        var storage = fixture.Hero();
        var memory = new ExplorationMemory();
        memory.Load(storage, JsonSerializer.Serialize(document));
        Assert.True(storage.ExploredCells.SetEquals([(1, 1), (2, 2)]));
        Assert.True(storage.SeenObjects!.SetEquals(["Old clue@1,1", "New clue@2,2"]));

        storage.WorldState.Map.Chunks![0].Seed = 43;
        memory.Select(storage);
        Assert.Equal([(3, 3)], storage.ExploredCells);
        Assert.Equal(["Other map@3,3"], storage.SeenObjects);

        storage.WorldState.World = new() { Id = otherWorld, Place = "Oldhaven" };
        storage.WorldState.Map.Chunks[0].Seed = 42;
        memory.Select(storage);
        Assert.Equal([(9, 9)], storage.ExploredCells);
        Assert.Equal(["Other world@9,9"], storage.SeenObjects);

        var anotherHero = fixture.Hero("Newhaven");
        anotherHero.SelectExplorationLocation();
        Assert.Empty(anotherHero.ExploredCells);
        Assert.Null(anotherHero.SeenObjects);
    }

    [Fact]
    public void LoadingAnUnmodifiedOldHeroSaveRestoresExplorationAfterCapitalRename()
    {
        using var fixture = new TestWorld();
        string directory = Path.Combine(Path.GetTempPath(), "navidnd-rename-save-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "hero.json");
        try
        {
            var storage = fixture.Hero();
            storage.SelectExplorationLocation();
            storage.ExploredCells.Add((4, 5));
            storage.SeenObjects = ["Chest@4,5"];
            typeof(Storage).GetMethod("SaveTo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(storage, [path]);
            string oldSave = File.ReadAllText(path);
            string oldExploration = File.ReadAllText(Path.Combine(directory, "hero.explored.json"));

            fixture.Rename("Newhaven");
            fixture.Rename("Bright Haven");
            var loaded = new Storage();
            loaded.LoadFrom(path);
            Assert.Equal("Bright Haven", loaded.WorldState.World!.Place);
            Assert.Equal([(4, 5)], loaded.ExploredCells);
            Assert.Equal(["Chest@4,5"], loaded.SeenObjects);
            Assert.Equal(oldSave, File.ReadAllText(path));
            Assert.Equal(oldExploration, File.ReadAllText(Path.Combine(directory, "hero.explored.json")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }
}
