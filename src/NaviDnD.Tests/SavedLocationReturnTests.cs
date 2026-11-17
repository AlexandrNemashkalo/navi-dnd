using System.Reflection;
using System.Text.Json;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class SavedLocationReturnTests
{
    [Theory]
    [InlineData("""{"chunks":[null]}""")]
    [InlineData("""{"chunks":[{"exits":null}]}""")]
    [InlineData("""{"rooms":[null]}""")]
    [InlineData("""{"rooms":[{"positions":[null]}]}""")]
    [InlineData("""{"doors":[null]}""")]
    public void IncompleteArchiveIsRejectedBeforeChangingTheCurrentState(string snapshot)
    {
        var storage = new Storage();
        storage.WorldState.Map = Clearing();
        storage.WorldState.Hero = new() { Position = [9, 8], Hp = "7/10" };
        string before = JsonSerializer.Serialize(storage.WorldState);
        var archive = System.Text.Json.Nodes.JsonNode.Parse(snapshot)!.AsObject();
        archive["snapshotTime"] = null;
        Assert.False(LocationRestoration.TryRestore(storage, archive.ToJsonString()));
        Assert.Equal(before, JsonSerializer.Serialize(storage.WorldState));
    }

    private sealed class EncounterRandom : Random { public override double NextDouble() => 0; }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemporarilyLockedArchiveIsNotTreatedAsAnUnvisitedPlace(bool encounter)
    {
        string id = "locked-archive-" + Guid.NewGuid().ToString("N");
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var world = new WorldMap { Id = id, Width = 2, Height = 1, Biomes = [".."], Owners = [".."], Roads = ["11"], Rivers = ["00"],
            Chronicle = new() { Places = [new() { Name = "Clearing", Type = "village", X = 1, Y = 0, Location = "clearing.json" }] } };
        string directory = WorldLibrary.LocationsDir(id);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "clearing.json");
        string original = LocationRestoration.SerializeSnapshot(Clearing(), new GameTime());
        File.WriteAllText(path, original);
        worlds.Add(id, world);
        try
        {
            var storage = new Storage();
            storage.WorldState.World = new() { Id = id, X = 0, Y = 0, Known = ["Clearing"] };
            storage.WorldState.Hero = new() { Position = [1, 1] };
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.True(GameWorld.HasSavedLocation(world, "Clearing"));
                Assert.Null(GameWorld.SavedLocationJson(world, "Clearing"));
                var plan = TravelService.PlanTo(storage.WorldState, (1, 0));
                Assert.NotNull(plan);
                string report = TravelService.Go(storage.WorldState, plan, TravelService.Pace.Normal,
                    encounter ? new EncounterRandom() : new Random(1));
                Assert.Equal("Clearing", storage.WorldState.World.Place);
                Assert.DoesNotContain("call plan_location", report);
                Assert.Equal(encounter, report.Contains("ENCOUNTER"));
                if (encounter) Assert.DoesNotContain("take one long_rest", report);
                Assert.False(GameWorld.HasLocation(storage.WorldState));
                Assert.True(LocationRestoration.CanRetryAtTarget(storage.WorldState, null));
                Assert.True(LocationRestoration.CanRetryAtTarget(storage.WorldState, (1, 0)));
                Assert.False(LocationRestoration.CanRetryAtTarget(storage.WorldState, (0, 0)));
                string beforeRetry = JsonSerializer.Serialize(storage.WorldState);
                Assert.False(LocationRestoration.TryRestoreCurrentPlace(storage));
                Assert.Equal(beforeRetry, JsonSerializer.Serialize(storage.WorldState));
            }
            Assert.Equal(original, File.ReadAllText(path));
            string timeBefore = JsonSerializer.Serialize(storage.WorldState.Time);
            string inventoryBefore = JsonSerializer.Serialize(storage.WorldState.Hero!.Inventory);
            storage.WorldState.Combat = new() { Active = true };
            Assert.False(LocationRestoration.CanRetryAtTarget(storage.WorldState, null));
            Assert.False(LocationRestoration.TryRestoreCurrentPlace(storage));
            Assert.False(GameWorld.HasLocation(storage.WorldState));
            storage.WorldState.Combat = null;
            Assert.True(LocationRestoration.TryRestoreCurrentPlace(storage));
            Assert.False(LocationRestoration.CanRetryAtTarget(storage.WorldState, null));
            Assert.Equal(timeBefore, JsonSerializer.Serialize(storage.WorldState.Time));
            Assert.Equal(inventoryBefore, JsonSerializer.Serialize(storage.WorldState.Hero.Inventory));
            Assert.Contains(storage.WorldState.Map.Entities!, e => e.Name == "Resident" && e.Hp == "7/10");
            storage.WorldState.Map.Entities![0].Hp = "6/10";
            Assert.False(LocationRestoration.TryRestoreCurrentPlace(storage));
            Assert.Equal("6/10", storage.WorldState.Map.Entities[0].Hp);
        }
        finally
        {
            worlds.Remove(id);
            File.Delete(path);
            Directory.Delete(directory);
            Directory.Delete(Path.GetDirectoryName(directory)!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingNamedArchiveTargetNeverDiscardsTheCurrentMap(bool renamedCapital)
    {
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        string id = "missing-place-" + Guid.NewGuid().ToString("N");
        var world = new WorldMap { Id = id, Kingdoms = [new() { Capital = 0 }],
            Places = [new() { Name = "Generator capital", Type = "capital" }],
            Chronicle = new() { Places = [new() { Name = "Old capital", Type = "capital", Slot = 0 }] } };
        worlds[id] = world;
        var state = new WorldState { World = new() { Id = id, Place = "Old capital", Known = ["Old capital"], Visited = ["Old capital"] },
            Hero = new() { Position = [8, 8] }, Map = Clearing() };
        try
        {
            if (renamedCapital)
            {
                using var patch = JsonDocument.Parse("""{"kingdoms":[{"id":0,"capital":"New capital"}]}""");
                GameWorld.ApplyChronicle(world, patch.RootElement);
                // An old, already-damaged world may have lost its historical name before aliases existed.
                world.Chronicle.Places[0].FormerNames.Clear();
            }
            else world.Chronicle.Places.Clear();
            string before = JsonSerializer.Serialize(state);
            Assert.False(GameWorld.LeaveLocation(state));
            Assert.Equal(before, JsonSerializer.Serialize(state));
            Assert.True(GameWorld.HasLocation(state));
        }
        finally { worlds.Remove(id); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedLocationOrChronicleSaveKeepsTheSceneAndCancelsTravel(bool blockChronicle)
    {
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        string id = "save-failure-" + Guid.NewGuid().ToString("N");
        string directory = Path.Combine(WorldLibrary.Dir, id);
        string locations = WorldLibrary.LocationsDir(id);
        string chronicle = Path.Combine(directory, "world.json");
        var world = new WorldMap { Id = id, Width = 2, Height = 1, Biomes = [".."], Owners = [".."], Roads = ["00"], Rivers = ["00"],
            Chronicle = new() { Places = [new() { Name = "Clearing", Type = "ruins", Location = "clearing.json" }] } };
        worlds.Add(id, world);
        Directory.CreateDirectory(directory);
        if (blockChronicle) Directory.CreateDirectory(chronicle);
        else File.WriteAllText(locations, "A file blocks the location directory");
        var ws = new WorldState { World = new() { Id = id, Place = "Clearing", X = 0, Y = 0 },
            Hero = new() { Position = [8, 8], Hp = "12/12", Inventory = [new() { Name = "Ration", Quantity = 3 }] },
            Map = Clearing(), ScheduledEvents = [new() { Name = "Pending scene event", FireAtRound = 30 }] };
        string before = JsonSerializer.Serialize(ws);
        try
        {
            Assert.False(GameWorld.LeaveLocation(ws));
            Assert.Equal(before, JsonSerializer.Serialize(ws));
            string report = TravelService.Go(ws, new((0, 0), (1, 0), null,
                new(1, 0, [(0, 0), (1, 0)], "Test route")), TravelService.Pace.Normal, new Random(1));
            Assert.StartsWith("Travel cancelled:", report);
            Assert.Equal(before, JsonSerializer.Serialize(ws));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
            if (blockChronicle) Directory.Delete(chronicle);
            else File.Delete(locations);
            Assert.True(GameWorld.LeaveLocation(ws));
            Assert.False(GameWorld.HasLocation(ws));
            Assert.Contains("Greeter", GameWorld.SavedLocationJson(world, "Clearing"));
            Assert.True(File.Exists(chronicle));
        }
        finally
        {
            worlds.Remove(id);
            if (Directory.Exists(locations))
            {
                foreach (string file in Directory.GetFiles(locations)) File.Delete(file);
                Directory.Delete(locations);
            }
            else if (File.Exists(locations)) File.Delete(locations);
            if (Directory.Exists(chronicle)) Directory.Delete(chronicle);
            else File.Delete(chronicle);
            Directory.Delete(directory);
        }
    }

    private static MapConfig Clearing() => new()
    {
        Cols = 20, Rows = 15,
        Chunks = [new() { Theme = "forest", Populated = true, Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList(),
            Exits = [new() { Side = ChunkSide.Left, Offset = 8, External = true }] }],
        Entities = [new() { Name = "Resident", Symbol = "RES", Position = [1, 1], Hp = "7/10" },
            new() { Name = "Greeter", Symbol = "GRT", Position = [1, 8], Hp = "10/10" }],
    };

    [Fact]
    public void ReturningDoesNotMoveResidentsAndDoesNotOverlapTheEntranceNpc()
    {
        var storage = new Storage();
        storage.WorldState.Hero = new() { Symbol = "HRO", Position = [1, 1], Hp = "12/12" };
        string snapshot = LocationRestoration.SerializeSnapshot(Clearing(), new GameTime());
        Assert.True(LocationRestoration.TryRestore(storage, snapshot));
        Assert.Equal(new[] { 1, 1 }, storage.WorldState.Map.Entities![0].Position);
        Assert.Equal(new[] { 1, 8 }, storage.WorldState.Map.Entities[1].Position);
        Assert.DoesNotContain(storage.WorldState.Map.Entities, e => e.Position.SequenceEqual(storage.WorldState.Hero.Position));
        Assert.Equal("7/10", storage.WorldState.Map.Entities[0].Hp);
        Assert.True(LocationRestoration.TryRestore(storage, snapshot));
        Assert.Equal(2, storage.WorldState.Map.Entities.Count);
    }

    [Fact]
    public void AreaOnlyLocationWithoutExitMetadataCanStillBeRevisited()
    {
        var storage = new Storage();
        storage.WorldState.Hero = new() { Position = [1, 1] };
        var map = new MapConfig { Area = [new() { Name = "Camp", Positions = [[7, 6], [8, 6]] }] };
        Assert.True(LocationRestoration.TryRestore(storage, LocationRestoration.SerializeSnapshot(map, new GameTime())));
        Assert.Equal(new[] { 7, 6 }, storage.WorldState.Hero.Position);
        Assert.Equal("Camp", Assert.Single(storage.WorldState.Map.Area!).Name);
    }

    [Fact]
    public void ImpassableEntranceDoesNotFallBackToAnUnrelatedInteriorCell()
    {
        var storage = new Storage();
        storage.WorldState.Hero = new() { Position = [1, 1] };
        var map = Clearing();
        map.Chunks![0].Terrain = Enumerable.Repeat(new string('^', 20), 15).ToList();
        map.Chunks[0].Terrain![5] = "^^^^^.^^^^^^^^^^^^^^";
        Assert.False(LocationRestoration.TryRestore(storage, LocationRestoration.SerializeSnapshot(map, new GameTime())));
        Assert.False(GameWorld.HasLocation(storage.WorldState));
    }

    [Fact]
    public void BlockedEntranceDoesNotTeleportAcrossAClosedDoorOrMutateCurrentScene()
    {
        var map = Clearing();
        map.Chunks![0].Terrain = Enumerable.Repeat(new string(' ', 20), 15).ToList();
        map.Rooms = [new() { Positions = [[1, 8]] }, new() { Positions = [[2, 8]] }];
        map.Doors = [new() { From = [1, 8], To = [2, 8], IsDoor = true, IsDoorOpen = false }];
        var storage = new Storage();
        storage.WorldState.Hero = new() { Position = [4, 4] };
        storage.WorldState.Map.Objects = [new() { Name = "Previous scene", Position = [3, 3] }];
        Assert.False(LocationRestoration.TryRestore(storage, LocationRestoration.SerializeSnapshot(map, new GameTime())));
        Assert.Equal("Previous scene", Assert.Single(storage.WorldState.Map.Objects!).Name);
        Assert.Equal(new[] { 4, 4 }, storage.WorldState.Hero.Position);
    }

    [Fact]
    public void LeaveReturnAndLeaveAgainPreserveResolvedObjectsDoorsAndTriggers()
    {
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        string id = "return-test-" + Guid.NewGuid().ToString("N");
        var world = new WorldMap { Id = id, Chronicle = new() { Places = [new() { Name = "Clearing", Type = "ruins" }] } };
        worlds.Add(id, world);
        var storage = new Storage();
        var ws = storage.WorldState;
        ws.World = new() { Id = id, Place = "Clearing" };
        ws.Hero = new() { Position = [8, 8], Hp = "12/12" };
        ws.Map = Clearing();
        ws.Map.Entities!.Add(new() { Name = "Defeated bandit", Symbol = "BAN", Position = [7, 7], Hp = "0/8", Deleted = true });
        ws.Map.Objects = [new() { Name = "Searched body", Symbol = "BOD", Position = [7, 7], AiInfo = ["The key was taken"] },
            new() { Name = "Opened cache", Symbol = "CAC", Position = [4, 4], Triggers = new() { OnVisible = new() { Once = true, Effect = "Reveal cache" } } }];
        ws.Map.Doors = [new() { From = [2, 2], To = [3, 2], IsDoor = false, Hidden = false, AiInfo = ["Broken by the hero"] }];
        storage.ApplyUpdateWorldState("""{"map":{"objects":[{"id":1,"triggers":{"onVisible":null}}]}}""");
        try
        {
            for (int visit = 0; visit < 2; visit++)
            {
                GameWorld.LeaveLocation(ws);
                Assert.False(GameWorld.HasLocation(ws));
                string saved = Assert.IsType<string>(GameWorld.SavedLocationJson(world, "Clearing"));
                Assert.True(LocationRestoration.TryRestore(storage, saved));
                Assert.True(ws.Map.Entities![2].Deleted);
                Assert.Equal("0/8", ws.Map.Entities[2].Hp);
                Assert.Equal(2, ws.Map.Objects!.Count);
                Assert.Contains("The key was taken", ws.Map.Objects[0].AiInfo!);
                Assert.Null(ws.Map.Objects[1].Triggers?.OnVisible);
                Assert.False(ws.Map.Doors![0].IsDoor);
                Assert.Contains("Broken by the hero", ws.Map.Doors[0].AiInfo!);
            }
        }
        finally
        {
            worlds.Remove(id);
            string directory = Path.Combine(WorldLibrary.Dir, id);
            if (world.Chronicle.Places[0].Location is { } file)
                File.Delete(Path.Combine(WorldLibrary.LocationsDir(id), file));
            if (Directory.Exists(WorldLibrary.LocationsDir(id))) Directory.Delete(WorldLibrary.LocationsDir(id));
            if (Directory.Exists(directory)) { File.Delete(Path.Combine(directory, "world.json")); Directory.Delete(directory); }
        }
    }
}
