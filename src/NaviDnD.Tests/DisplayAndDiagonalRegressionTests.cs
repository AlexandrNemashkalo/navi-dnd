using System.Reflection;
using System.Text.Json;
using NaviDnD;
using NaviDnD.Data;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class DisplayAndDiagonalRegressionTests
{
    [Theory]
    [InlineData("F4")]
    [InlineData("F1")]
    public async Task StreamedPatchRedrawsCurrentTabRatherThanPaintingMapOverIt(string tab)
    {
        var storage = new Storage();
        int currentDraws = 0, mapDraws = 0;
        var display = new DisplayConfig
        {
            ActiveTabKey = tab,
            RedrawCurrentContent = () => currentDraws++,
            OnMapRedraw = () => mapDraws++
        };
        var dialog = new DialogDisplay(storage.WorldState, display, storage);
        var entry = new RequestHistoryEntry
        {
            Patch = JsonSerializer.SerializeToElement(new { hero = new { name = "Updated" } })
        };
        var method = typeof(DialogDisplay).GetMethod("StreamHistoryEntries", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(dialog, new object[] { new[] { entry } })!;
        Assert.Equal("Updated", storage.WorldState.Hero.Name);
        Assert.Equal(1, currentDraws);
        Assert.Equal(0, mapDraws);
        Assert.Equal(tab, display.ActiveTabKey);
    }

    [Theory]
    [InlineData("MoveNorthEast", 3, 3)]
    [InlineData("MoveNorthWest", 1, 3)]
    [InlineData("MoveSouthEast", 3, 1)]
    [InlineData("MoveSouthWest", 1, 1)]
    public void HeroCanSqueezeDiagonallyBetweenTrees(string direction, int col, int row)
    {
        var storage = new Storage();
        var ws = storage.WorldState;
        ws.Hero = new Hero { Symbol = "HRO", Position = [2, 2], SpeedMax = 30, SpeedLeft = 30 };
        // Terrain rows run south to north. All four cardinal neighbours are trees.
        ws.Map.Chunks = [new MapChunk { X = 0, Y = 0, Theme = "forest", Terrain = [".T.", "T.T", ".T."] }];
        var handler = new MovementHandler(ws, storage, new DisplayConfig());
        handler.TryMove(direction, out _, out _, out _, out _);
        Assert.Equal(new[] { col, row }, ws.Hero.Position);
        Assert.Equal(25, ws.Hero.SpeedLeft);
    }

    [Fact]
    public void DiagonalCannotEnterTreeOrCrossRoomWall()
    {
        var storage = new Storage();
        var ws = storage.WorldState;
        ws.Hero = new Hero { Symbol = "HRO", Position = [2, 2], SpeedMax = 30, SpeedLeft = 30 };
        ws.Map.Chunks = [new MapChunk { X = 0, Y = 0, Theme = "forest", Terrain = ["...", "...", "..T"] }];
        var handler = new MovementHandler(ws, storage, new DisplayConfig());
        handler.TryMove("MoveNorthEast", out _, out _, out _, out _);
        Assert.Equal(new[] { 2, 2 }, ws.Hero.Position);
        ws.Map.Chunks = [];
        ws.Map.Rooms = [new Room { Positions = [[2, 2]] }, new Room { Positions = [[3, 3]] }];
        handler.TryMove("MoveNorthEast", out _, out _, out _, out _);
        Assert.Equal(new[] { 2, 2 }, ws.Hero.Position);
    }
}
