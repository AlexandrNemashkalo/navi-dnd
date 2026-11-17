using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class BuildingForestBorderTests
{
    [Fact]
    public void ForestLobesAreConnectedAndPreserveWaterApproachesAndBoundary()
    {
        var bands = Enum.GetValues<ChunkSide>().ToDictionary(s => s, _ => 'T');
        HashSet<(int c, int r)> reserved = [(0, 7), (19, 7), (5, 2)];
        var depths = new HashSet<int>();
        for (int seed = 1; seed <= 12; seed++)
        {
            var g = new char[20, 15];
            for (int c = 0; c < 20; c++) for (int r = 0; r < 15; r++)
                g[c, r] = c == 0 || c == 19 || r == 0 || r == 14 ? 'T' : '.';
            g[10, 1] = 'W'; g[10, 2] = '~';
            var before = (char[,])g.Clone(); var repeat = (char[,])g.Clone();
            BuildingGenerator.GrowForestEdge(g, bands, seed, 0, 0, reserved);
            BuildingGenerator.GrowForestEdge(repeat, bands, seed, 0, 0, reserved);
            for (int c = 0; c < 20; c++) for (int r = 0; r < 15; r++)
            {
                Assert.Equal(g[c, r], repeat[c, r]);
                if (c == 0 || c == 19 || r == 0 || r == 14 || before[c, r] is 'W' or '~'
                    || reserved.Any(p => Math.Abs(p.c - c) <= 1 && Math.Abs(p.r - r) <= 1)) Assert.Equal(before[c, r], g[c, r]);
                if (g[c, r] == 'T' && before[c, r] == '.')
                    Assert.True(new[] { (c-1,r), (c+1,r), (c,r-1), (c,r+1) }.Any(p => g[p.Item1,p.Item2] == 'T'));
            }
            for (int c = 3; c < 17; c++)
                depths.Add(Enumerable.Range(0, 4).TakeWhile(r => g[c, r] == 'T').Count());
        }
        Assert.Contains(1, depths); Assert.Contains(2, depths); Assert.Contains(3, depths);
    }

    [Theory]
    [InlineData("house")]
    [InlineData("manor")]
    public void NewForestBorderLeavesBuildingRoomsAndExitsReachable(string style)
    {
        int irregularEdges = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            var chunk = new MapChunk { Theme = "building", Style = style, Seed = seed };
            ChunkExit[] exits = [new() { Side = ChunkSide.Left, Offset = 7, External = true }, new() { Side = ChunkSide.Right, Offset = 7 }];
            var result = BuildingGenerator.Generate(chunk, exits, seed, true, Enum.GetValues<ChunkSide>().ToDictionary(s => s, _ => 'T'), []);
            chunk.Terrain = result.Terrain;
            foreach (var door in result.Doors.Where(d => d.IsWindow != true)) door.IsDoorOpen = true;
            var state = new WorldState { Map = new() { Chunks = [chunk], Rooms = result.Rooms, Doors = result.Doors, Furniture = result.Furniture } };
            var start = exits[0].AnchorCell(chunk);
            var reached = MovementCalculator.Dijkstra(state, start.col, start.row, 3000, -1);
            bool Reach(int c, int r) => reached.ContainsKey((c,r,false)) || reached.ContainsKey((c,r,true));
            foreach (var p in result.Rooms.SelectMany(room => room.Positions).Where(p => state.Map.TerrainAt(p[0], p[1])?.BlocksMove != true))
                Assert.True(Reach(p[0],p[1]), $"{style}/{seed}: {p[0]},{p[1]}");
            var end = exits[1].AnchorCell(chunk); Assert.True(Reach(end.col,end.row));
            var voids = result.Terrain.SelectMany((line, r) => line.Select((code, c) => (code,c,r))).Where(p => p.code == ' ').ToList();
            if (voids.Count > 0) irregularEdges++;
            foreach (var p in voids)
            {
                Assert.True(p.c < 2 || p.r < 2 || p.c >= MapChunk.Cols - 2 || p.r >= MapChunk.Rows - 2);
                Assert.False(MovementCalculator.CanMoveTo(state, start.col, start.row, p.c + 1, p.r + 1, -1));
                Assert.Null(state.Map.RoomAt(p.c + 1,p.r + 1));
            }
        }
        Assert.True(irregularEdges >= 10, $"Only {irregularEdges} irregular forest silhouettes");
    }
}
