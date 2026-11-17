using System.Text.RegularExpressions;
using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class TerrainContextTests
{
    [Theory]
    [InlineData("forest", false, "Passable cells")]
    [InlineData("village", false, "Streets and courtyards")]
    [InlineData("building", true, "- rooms id:0")]
    public void PlacementListsExcludeThresholdsStairsFurnitureAndOccupants(string theme, bool indoors, string prefix)
    {
        var chunk = new MapChunk { Theme = theme, Terrain = [".........."] };
        var state = new WorldState { Hero = new() { Position = [6,1] }, Map = new()
        {
            Chunks = [chunk],
            Rooms = indoors ? [new() { Name = "Room", Positions = Enumerable.Range(1,10).Select(c => new List<int> {c,1}).ToList() }] : [],
            Doors = [new() { From = [2,1], To = [3,1], IsDoor = true }],
            Stairs = [new() { A = [4,1], B = [5,1] }],
            Furniture = [new() { Kind = "bench", Positions = [[7,1]] }],
            Entities = [new() { Position = [8,1] }, new() { Position = [10,1], Deleted = true }],
            Objects = [new() { Position = [9,1] }]
        } };
        string before = System.Text.Json.JsonSerializer.Serialize(state);
        string context = new AiContextBuilder(state, []).ChunkState(chunk);
        string placement = Assert.Single(context.Split('\n'), line => line.StartsWith(prefix, StringComparison.Ordinal));
        Assert.EndsWith("row 1: 1, 10", placement.TrimEnd());
        Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(state));
    }

    [Fact]
    public void FurnitureNamesItsActualRoomWithOriginalIdsAndDistinguishesTheCourtyard()
    {
        var chunk = new MapChunk { X = 1, Y = 0, Theme = "village",
            Terrain = Enumerable.Repeat(new string('.',20),15).ToList() };
        var state = new WorldState { Map = new() { Chunks = [chunk],
            Rooms = [new() { Name = "Other chunk", Positions = [[2,3]] },
                new() { Name = "Removed", Deleted = true, Positions = [[22,3]] },
                new() { Name = "Workshop", Positions = [[22,3],[23,3]] },
                new() { Name = "Storehouse", Positions = [[25,3]] }],
            Furniture = [new() { Kind = "table", Name = "Other table", Positions = [[2,3]] },
                new() { Kind = "table", Name = "Repair table", Positions = [[22,3],[23,3]] },
                new() { Kind = "cabinet", Name = "Linen cabinet", Positions = [[25,3]] },
                new() { Kind = "bench", Name = "Courtyard bench", Positions = [[21,2]] }] } };
        string before = System.Text.Json.JsonSerializer.Serialize(state);
        string text = new AiContextBuilder(state, []).ChunkState(chunk);
        Assert.Contains("id:1 Repair table [22,3]–[23,3] (room id:2)", text);
        Assert.Contains("id:2 Linen cabinet [25,3] (room id:3)", text);
        Assert.Contains("id:3 Courtyard bench [21,2] (outside rooms)", text);
        Assert.DoesNotContain("Other table", text);
        Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(state));
    }

    [Theory]
    [InlineData("village")]
    [InlineData("building")]
    public void CourtyardReportsWaterCoordinatesSeparatelyFromRoomTerrain(string theme)
    {
        var chunk = new MapChunk { X = 2, Y = 1, Theme = theme,
            Terrain = ["WW........", "~~........", ".........s"] };
        var roomCell = new List<int> { chunk.OriginCol + 10, chunk.OriginRow + 3 };
        var ws = new WorldState { Map = new() { Chunks = [chunk],
            Rooms = [new() { Name = "House", Positions = [roomCell] }] } };
        string context = new AiContextBuilder(ws, []).ChunkState(chunk);
        Assert.Contains("Outside rooms. Terrain (cells):", context);
        string references = context.Split("Terrain reference cells")[1];
        Assert.Contains($"[{chunk.OriginCol + 1},{chunk.OriginRow + 1}]", references);
        Assert.Contains($"[{chunk.OriginCol + 1},{chunk.OriginRow + 2}]", references);
        Assert.DoesNotContain($"[{roomCell[0]},{roomCell[1]}]", references);
        Assert.Contains(TerrainCatalog.Get('W')!.Name, references);
        Assert.Contains(TerrainCatalog.Get('~')!.Name, references);
        Assert.DoesNotContain(TerrainCatalog.Get('s')!.Name + ":", references);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, -1)]
    public void TerrainReferencesUseActualCellsAndGlobalChunkCoordinates(int x, int y)
    {
        // Separated water patches: their average is dry ground, not a valid water landmark.
        var chunk = new MapChunk { X = x, Y = y, Theme = "forest",
            Terrain = ["~.........", "..........", ".........~"] };
        var ws = new WorldState { Map = new() { Chunks = [chunk] } };
        string context = new AiContextBuilder(ws, []).ChunkState(chunk);
        string references = context.Split("Terrain reference cells")[1].Split("Passable cells")[0];
        foreach (var line in references.Split('\n').Where(l => l.StartsWith("- ")))
        {
            string kindName = line[2..line.IndexOf(':')];
            var points = Regex.Matches(line, @"\[(-?\d+),(-?\d+)\]");
            Assert.InRange(points.Count, 1, 5);
            foreach (Match point in points)
            {
                int col = int.Parse(point.Groups[1].Value) - chunk.OriginCol - 1;
                int row = int.Parse(point.Groups[2].Value) - chunk.OriginRow - 1;
                Assert.Equal(kindName, TerrainCatalog.Get(chunk.Terrain[row][col])!.Name);
            }
        }
        Assert.Equal(2, references.Split('\n').Count(l => l.StartsWith("- ")));
        Assert.Contains($"[{chunk.OriginCol + 1},{chunk.OriginRow + 1}]", references);
        Assert.Contains($"[{chunk.OriginCol + 10},{chunk.OriginRow + 3}]", references);
    }
}
