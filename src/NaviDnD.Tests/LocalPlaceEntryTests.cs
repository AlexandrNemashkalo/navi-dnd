using System.Reflection;
using System.Text.Json;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public sealed class LocalPlaceEntryTests : IDisposable
{
    private readonly string id = "local-entry-" + Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, WorldMap> worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary)
        .GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    private readonly WorldMap world;

    public LocalPlaceEntryTests()
    {
        world = new() { Id = id, Width = 3, Height = 1, Biomes = ["..."], Roads = ["000"], Rivers = ["000"], Owners = ["..."],
            Chronicle = new() { Places = [new() { Name = "Wayside shrine", FormerNames = ["Old shrine"], Type = WorldPlaceTypes.Shrine,
                X = 1, Y = 0, Description = "A quiet shrine beside the road." }] } };
        worlds[id] = world;
    }

    private WorldState State() => new()
    {
        Hero = new() { Name = "Explorer", Hp = "4/10", Position = [1, 1],
            Inventory = [new() { Name = "Ration", Quantity = 3 }], Effects = [new() { Name = "Ward", ExpiresAtRound = 40 }] },
        World = new() { Id = id, X = 1, Y = 0, Known = ["Wayside shrine"] },
        Time = new() { Day = 7, PartOfDay = PartsOfDay.Evening, TotalRounds = 23 },
        Narrative = new() { CurrentArc = "Choose my own destination" },
        ScheduledEvents = [new() { Name = "A reminder", FireAtRound = 30, Effect = "Remember the appointment" }],
        PendingTravel = "An earlier journey still needs narration"
    };

    [Fact]
    public void SelectingKnownPlaceOnCurrentTileDoesNotEnterIt()
    {
        var state = State();
        var visibleWorld = GameWorld.ForDisplay(state);
        string before = JsonSerializer.Serialize(state);
        Assert.Equal("Wayside shrine", LocalPlaceEntry.Find(state, (1, 0), visibleWorld)!.Name);
        Assert.Null(TravelService.PlanTo(state, (1, 0)));
        Assert.Equal(before, JsonSerializer.Serialize(state));
        Assert.Empty(state.World!.Visited);
        Assert.Null(state.World.Place);
    }

    [Fact]
    public void ExplicitEntryOnlyMarksThePlaceAndVisitWithoutAnotherJourney()
    {
        var state = State();
        string Preserved() => JsonSerializer.Serialize(new { state.Hero, state.Time, state.Map, state.Narrative,
            state.ScheduledEvents, state.Combat, state.History, state.PendingTravel });
        string before = Preserved();
        int version = state.World!.Version;
        Assert.True(LocalPlaceEntry.TryEnter(state, (1, 0), out string report));
        Assert.Equal("Wayside shrine", state.World.Place);
        Assert.Equal(new[] { "Wayside shrine" }, state.World.Visited);
        Assert.Equal(new[] { "Wayside shrine" }, state.World.Known);
        Assert.Equal((1, 0), GameWorld.HeroTile(state));
        Assert.Equal(version + 1, state.World.Version);
        Assert.Equal(before, Preserved());
        Assert.Contains("## LOCAL ENTRY", report);
        Assert.Contains("No travel time, food, rest", report);
        Assert.Contains("entry does not accept a quest", report);
        Assert.False(LocalPlaceEntry.TryEnter(state, (1, 0), out _));
        Assert.Single(state.World.Visited);
    }

    [Theory]
    [InlineData("no selection")]
    [InlineData("another tile")]
    [InlineData("unknown")]
    [InlineData("combat")]
    [InlineData("active map")]
    [InlineData("already here")]
    [InlineData("ambiguous")]
    public void InvalidEntryPreservesState(string condition)
    {
        var state = State();
        (int, int)? target = (1, 0);
        switch (condition)
        {
            case "no selection": target = null; break;
            case "another tile": target = (2, 0); break;
            case "unknown": state.World!.Known.Clear(); break;
            case "combat": state.Combat = new() { Active = true }; break;
            case "active map": state.Map.Chunks = [new() { Theme = "forest", Seed = 4 }]; break;
            case "already here": state.World!.Place = "Wayside shrine"; break;
            case "ambiguous":
                world.Chronicle.Places.Add(new() { Name = "Other shrine", Type = WorldPlaceTypes.Shrine, X = 1, Y = 0 });
                state.World!.Known.Add("Other shrine");
                break;
        }
        string before = JsonSerializer.Serialize(state);
        Assert.Null(LocalPlaceEntry.Find(state, target));
        Assert.False(LocalPlaceEntry.TryEnter(state, target, out string report));
        Assert.Empty(report);
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void PureSelectionDoesNotReenterTheCurrentPlacesOldName()
    {
        var state = State();
        var visibleWorld = GameWorld.ForDisplay(state);
        state.World!.Place = "Old shrine";
        string before = JsonSerializer.Serialize(state);
        Assert.Null(LocalPlaceEntry.Find(state, (1, 0), visibleWorld));
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void ArchivedPlaceRequiresRestorationAndIsNotRegeneratedByEntry()
    {
        world.Chronicle.Places[0].Location = "unavailable-map.json";
        var state = State();
        Assert.True(LocalPlaceEntry.TryEnter(state, (1, 0), out string report));
        Assert.Contains("archived location", report);
        Assert.Contains("never regenerate or replace", report);
        Assert.False(GameWorld.HasLocation(state));
        Assert.True(LocationRestoration.CanRetryAtTarget(state, (1, 0)));
        Assert.Equal("unavailable-map.json", world.Chronicle.Places[0].Location);
    }

    [Fact]
    public void LocalEntryRestoresAnOccupiedArchiveWithoutResettingItsConsequencesOrChargingTheHero()
    {
        var storage = new Storage { SaveExploredCells = false };
        storage.ApplySavedState(JsonSerializer.Serialize(State()));
        var state = storage.WorldState;
        world.Chronicle.Places[0].Location = "entry.json";
        string directory = WorldLibrary.LocationsDir(id);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "entry.json");
        var map = new MapConfig
        {
            Chunks = [new() { Theme = "plains", Populated = true,
                Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList(),
                Exits = [new() { Side = ChunkSide.Left, Offset = 8, External = true }] }],
            Entities = [new() { Name = "Keeper", Symbol = "KPR", Position = [1,8], Hp = "6/10",
                AiInfo = ["The keeper remembers that the shrine's lost letter was returned."] }],
            Furniture = [new() { Name = "Writing desk", Kind = "table", State = "searched", Positions = [[4,8]],
                AiInfo = ["Its letter was taken and returned to the keeper. The drawer remains empty."] }]
        };
        string archive = LocationRestoration.SerializeSnapshot(map, state.Time);
        File.WriteAllText(path, archive);
        string Resources() => JsonSerializer.Serialize(new { state.Time, state.ScheduledEvents, state.PendingTravel,
            state.Hero!.Hp, state.Hero.Inventory, state.Hero.Effects });
        string before = Resources();
        try
        {
            Assert.True(LocalPlaceEntry.TryEnter(state, (1,0), out _));
            Assert.True(LocationRestoration.TryRestoreCurrentPlace(storage));
            Assert.Equal(before, Resources());
            Assert.Equal("Wayside shrine", state.World!.Place);
            Assert.Single(state.World.Visited);
            var keeper = Assert.Single(state.Map.Entities!);
            Assert.Equal(new[] { 1,8 }, keeper.Position);
            Assert.Equal("6/10", keeper.Hp);
            Assert.False(keeper.Position.SequenceEqual(state.Hero!.Position));
            Assert.Contains("lost letter was returned", Assert.Single(keeper.AiInfo!));
            var desk = Assert.Single(state.Map.Furniture!);
            Assert.Equal("searched", desk.State);
            Assert.Contains("drawer remains empty", Assert.Single(desk.AiInfo!));
            Assert.Empty(state.Map.Objects ?? []);
            Assert.Equal(archive, File.ReadAllText(path));
            keeper.Hp = "5/10";
            Assert.False(LocalPlaceEntry.TryEnter(state, (1,0), out _));
            Assert.False(LocationRestoration.TryRestoreCurrentPlace(storage));
            Assert.Equal("5/10", keeper.Hp);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
            Directory.Delete(Path.GetDirectoryName(directory)!);
        }
    }

    public void Dispose() => worlds.Remove(id);
}
