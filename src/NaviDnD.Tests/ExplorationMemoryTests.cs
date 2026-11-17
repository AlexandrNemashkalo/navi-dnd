using System.Text.Json;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class ExplorationMemoryTests
{
    [Fact]
    public void RealSaveReloadRestoresKnowledgeOfEarlierLocations()
    {
        string directory = Path.Combine(Path.GetTempPath(), "navidnd-exploration-" + Guid.NewGuid().ToString("N"));
        string save = Path.Combine(directory, "game1.json");
        try
        {
            var storage = new Storage();
            Enter(storage, "Mill");
            storage.SelectExplorationLocation();
            storage.ExploredCells.Add((4, 5));
            storage.SeenObjects = ["Chest@4,5"];
            Enter(storage, "Cave");
            storage.SelectExplorationLocation();
            storage.ExploredCells.Add((8, 2));
            typeof(Storage).GetMethod("SaveTo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(storage, [save]);

            var loaded = new Storage();
            loaded.LoadFrom(save);
            Assert.Equal([(8, 2)], loaded.ExploredCells);
            Enter(loaded, "Mill");
            loaded.SelectExplorationLocation();
            Assert.Equal([(4, 5)], loaded.ExploredCells);
            Assert.Equal(["Chest@4,5"], loaded.SeenObjects);
            loaded.SaveExploredCells = false;
            loaded.LoadFrom(save);
            Assert.Empty(loaded.ExploredCells);
            Enter(loaded, "Mill");
            loaded.SelectExplorationLocation();
            Assert.Empty(loaded.ExploredCells);
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

    private static void Enter(Storage storage, string name, int seed = 42)
    {
        storage.WorldState.World = new() { Id = "test-world", Place = name };
        storage.WorldState.Map = new() { Chunks = [new() { Seed = seed, Theme = "forest", Populated = true }] };
    }

    [Fact]
    public void ReturnRestoresOnlyThisHeroesKnowledgeOfThisLocation()
    {
        var storage = new Storage();
        Enter(storage, "Mill");
        storage.SelectExplorationLocation();
        storage.ExploredCells.Add((4, 5));
        storage.SeenObjects = ["Chest@4,5"];
        storage.VisibleCells.Add((4, 5));

        GameWorld.ClearMap(storage.WorldState.Map);
        storage.SelectExplorationLocation();
        Assert.Empty(storage.ExploredCells);
        Assert.Empty(storage.VisibleCells);
        Enter(storage, "Cave");
        storage.SelectExplorationLocation();
        Assert.Empty(storage.ExploredCells);
        storage.ExploredCells.Add((9, 3));
        storage.SeenObjects = ["Altar@9,3"];

        Enter(storage, "Mill");
        storage.SelectExplorationLocation();
        Assert.Equal([(4, 5)], storage.ExploredCells);
        Assert.Equal(["Chest@4,5"], storage.SeenObjects);
        Assert.False(storage.IsRemembered(new CellEntity { Name = "New clue", Position = [4, 5] }));

        var anotherHero = new Storage();
        Enter(anotherHero, "Mill");
        anotherHero.SelectExplorationLocation();
        Assert.Empty(anotherHero.ExploredCells);
    }

    [Fact]
    public void GrowingMapKeepsKnowledgeButRegeneratedGeometryDoesNot()
    {
        var storage = new Storage();
        Enter(storage, "Mill");
        storage.SelectExplorationLocation();
        storage.ExploredCells.Add((4, 5));
        storage.WorldState.Map.Chunks!.Add(new() { X = 1, Seed = 99 });
        storage.SelectExplorationLocation();
        Assert.Contains((4, 5), storage.ExploredCells);
        Enter(storage, "Mill", 43);
        storage.SelectExplorationLocation();
        Assert.Empty(storage.ExploredCells);
    }

    [Fact]
    public void SavingAndLoadingKeepsBothVisitedLocationsAndSeenObjects()
    {
        var source = new Storage();
        var memory = new ExplorationMemory();
        Enter(source, "Mill");
        memory.Select(source);
        source.ExploredCells.Add((4, 5));
        source.SeenObjects = ["Chest@4,5"];
        Enter(source, "Cave");
        memory.Select(source);
        source.ExploredCells.Add((9, 3));
        source.SeenObjects = [];
        string json = memory.Serialize(source);

        var target = new Storage();
        Enter(target, "Cave");
        var loaded = new ExplorationMemory();
        loaded.Load(target, json);
        Assert.Equal([(9, 3)], target.ExploredCells);
        Assert.Empty(target.SeenObjects!);
        Enter(target, "Mill");
        loaded.Select(target);
        Assert.Equal([(4, 5)], target.ExploredCells);
        Assert.Equal(["Chest@4,5"], target.SeenObjects);
        loaded.Load(target, null);
        Assert.Empty(target.ExploredCells);
    }

    [Fact]
    public void NewAdventureDoesNotInheritExplorationFromThePreviousHero()
    {
        var storage = new Storage();
        Enter(storage, "Mill");
        storage.SelectExplorationLocation();
        storage.ExploredCells.Add((4, 5));
        Enter(storage, "Cave");
        storage.SelectExplorationLocation();
        storage.ResetWorldState();
        Enter(storage, "Mill");
        storage.SelectExplorationLocation();
        Assert.Empty(storage.ExploredCells);
    }

    [Fact]
    public void RuntimeRequiresMigratedExplorationData()
    {
        var storage = new Storage();
        Enter(storage, "Mill");
        Assert.Throws<JsonException>(() => new ExplorationMemory().Load(storage, "[[4,5]]"));
    }
}
