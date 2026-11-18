using NaviDnD.Data;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class ExplorationVisibilityTests
{
    [Theory]
    [InlineData('o', false)]
    [InlineData('g', true)]
    [InlineData('s', true)]
    public void LargeBoulderHidesSpaceBehindItButLooseStoneDoesNot(char terrain, bool seesBehind)
    {
        var storage = new Storage();
        storage.WorldState.Map.Chunks = [new() { Theme="plains",
            Terrain=Enumerable.Repeat(new string('.',20),15).ToList() }];
        storage.WorldState.Map.Chunks[0].Terrain[2] = ".."+terrain+new string('.',17);
        Assert.True(MovementCalculator.HasLineOfSight(storage.WorldState,1,3,3,3));
        Assert.Equal(seesBehind,MovementCalculator.HasLineOfSight(storage.WorldState,1,3,5,3));
        Assert.Equal(seesBehind,MovementCalculator.HasLineOfSight(storage.WorldState,5,3,1,3));
    }

    private static Room Room(int left, int right) => new()
    {
        Positions = Enumerable.Range(left, right - left + 1)
            .SelectMany(x => Enumerable.Range(1, 5).Select(y => new List<int> { x, y })).ToList()
    };

    private static Storage Scene(bool open)
    {
        var storage = new Storage();
        var world = storage.WorldState;
        world.Hero = new() { Position = [1, 2], VisionFt = 60 };
        world.Map.Cols = 9;
        world.Map.Rows = 5;
        world.Map.Rooms = [Room(1, 3), Room(4, 6), Room(7, 9)];
        world.Map.Doors = [new() { From = [3, 2], To = [4, 2], IsDoor = true, IsDoorOpen = open }];
        return storage;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenObjectRequiresSightThroughTheInterveningDoor(bool open)
    {
        var storage = Scene(open);
        var cache = new CellEntity
        {
            Name = "Cache", Symbol = "CAC", Position = [5, 2], Hidden = true,
            Triggers = new() { OnVisible = new() { Effect = "Notice marks on the chest" } }
        };
        storage.WorldState.Map.Objects = [cache];
        var handler = new MovementHandler(storage.WorldState, storage, new DisplayConfig());
        bool? requested = handler.TryMove("MoveEast", out _, out var triggers, out _, out _);
        Assert.Equal(new[] { 2, 2 }, storage.WorldState.Hero!.Position);
        Assert.Equal(open, requested);
        Assert.Equal(open ? 1 : 0, triggers.Count);
        if (open) Assert.Equal((cache, "onVisible"), triggers.Single());
        Assert.True(cache.Hidden); // The trigger still asks the GM to resolve perception; it does not reveal the cache.
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SecretDoorRequiresOneVisibleSide(bool open, bool reverse)
    {
        var storage = Scene(open);
        var secret = new Door
        {
            From = reverse ? [7, 2] : [6, 2], To = reverse ? [6, 2] : [7, 2], Hidden = true,
            Triggers = new() { OnVisible = new() { Effect = "Notice the concealed seam" } }
        };
        storage.WorldState.Map.Doors!.Add(secret);
        var handler = new MovementHandler(storage.WorldState, storage, new DisplayConfig());
        bool? requested = handler.TryMove("MoveEast", out _, out _, out _, out var doors);
        Assert.Equal(open, requested);
        Assert.Equal(open ? 1 : 0, doors.Count);
        if (open) Assert.Equal((secret, "onVisible"), doors.Single());
    }
}
