using System.Text.RegularExpressions;
using NaviDnD.Data;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class WorldExitRenderingTests
{
    [Fact]
    public void CrownBesideExitKeepsItsEndpointAndForestBackground()
    {
        var storage = new Storage();
        var rows = Enumerable.Repeat(new string('.', MapChunk.Cols), MapChunk.Rows).ToList();
        rows[0] = "TT=TT...............";
        storage.WorldState.Map = new() { Chunks = [new() { Theme = "plains", Terrain = rows }],
            Doors = [new() { From = [3,1], To = [3,0], IsWorldExit = true, IsEntry = true, IsDoor = true }] };
        storage.WorldState.Hero = new() { Position = [3,1] };
        var provider = new MapObjectsProvider(storage.WorldState, new DisplayConfig(), storage, new AppConfig { RevealMap = true });
        foreach (double x in new[] { 2.5, 3.5 })
        {
            var previous = Console.Out;
            using var output = new StringWriter();
            try { Console.SetOut(output); provider.WriteCorner(x, .5, CornerType.BottomMiddle); }
            finally { Console.SetOut(previous); }
            string ansi = output.ToString();
            Assert.Equal(x < 3 ? "▙" : "▟", Regex.Replace(ansi, "\u001b\\[[0-9;]*m", ""));
            var background = Regex.Match(ansi, "\u001b\\[48;2;(\\d+);(\\d+);(\\d+)m");
            Assert.True(background.Success);
            Assert.Equal(TerrainCatalog.Get('=')!.Bg[1],int.Parse(background.Groups[2].Value));
        }
    }

    [Theory]
    [InlineData(1, 5, 0, 5, "←")]
    [InlineData(20, 5, 21, 5, "→")]
    [InlineData(5, 1, 5, 0, " ↓ ")]
    [InlineData(5, 15, 5, 16, " ↑ ")]
    public void ExitPointsOutwardOnGroundBackgroundInEitherEndpointOrder(int x, int y, int ox, int oy, string arrow)
    {
        foreach (bool reverse in new[] { false, true })
        {
            var storage = new Storage();
            var door = new Door { From = reverse ? [ox, oy] : [x, y], To = reverse ? [x, y] : [ox, oy],
                IsWorldExit = true, IsEntry = true, IsDoor = true, IsDoorOpen = false, Color = [160, 90, 220] };
            storage.WorldState.Map = new() { Chunks = [new() { Theme = "plains",
                Terrain = Enumerable.Repeat(new string('=', MapChunk.Cols), MapChunk.Rows).ToList() }], Doors = [door] };
            storage.WorldState.Hero = new() { Position = [x, y] };
            var provider = new MapObjectsProvider(storage.WorldState, new DisplayConfig(), storage, new AppConfig { RevealMap = true });
            var previous = Console.Out;
            using var output = new StringWriter();
            try { Console.SetOut(output); provider.WriteLine((x + ox) / 2d, (y + oy) / 2d, x == ox); }
            finally { Console.SetOut(previous); }
            string ansi = output.ToString();
            Assert.Equal(arrow, Regex.Replace(ansi, "\u001b\\[[0-9;]*m", ""));
            Assert.DoesNotContain("38;2;160;90;220", ansi);
            var backgrounds = Regex.Matches(ansi, "\u001b\\[48;2;(\\d+);(\\d+);(\\d+)m");
            Assert.NotEmpty(backgrounds);
            Assert.True(int.Parse(backgrounds[0].Groups[1].Value) > 30);
            Assert.False(door.IsDoorOpen); // Rendering must not change travel or door state.
        }
    }
}
