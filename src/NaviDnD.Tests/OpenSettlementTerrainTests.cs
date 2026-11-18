using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class OpenSettlementTerrainTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenSettlementEdgesContinueIntoCountrysideWithoutClosingRoutes(bool village)
    {
        int plantsOnEdge = 0;
        for (int seed = 1; seed <= 30; seed++)
        {
            var chunk = new MapChunk { X = 1, Y = 1, Theme = village ? "village" : "building", Style = "house", Seed = seed };
            ChunkExit[] exits = [new() { Side = ChunkSide.Left, Offset = 7, External = true },
                new() { Side = ChunkSide.Right, Offset = 7, External = true }];
            var result = village ? BuildingGenerator.GenerateVillage(chunk, exits, new Dictionary<ChunkSide, char>())
                : BuildingGenerator.Generate(chunk, exits, seed, true, new Dictionary<ChunkSide, char>(), []);
            chunk.Terrain = result.Terrain;
            var edge = result.Terrain.SelectMany((row, r) => row.Select((code, c) => (code, c, r)))
                .Where(p => p.c == 0 || p.r == 0 || p.c == MapChunk.Cols - 1 || p.r == MapChunk.Rows - 1).ToList();
            Assert.True(edge.Count(p => TerrainCatalog.Get(p.code) is { BlocksMove: false }) > edge.Count * 0.75,
                $"Seed {seed}: countryside was sealed by a perimeter");
            plantsOnEdge += edge.Count(p => p.code is 'T' or '"');
            Assert.DoesNotContain(edge, p => p.code == ' ');
            foreach (var door in result.Doors.Where(d => d.IsWindow != true)) door.IsDoorOpen = true;
            var state = new WorldState { Map = new() { Cols = MapConfig.MaxSize, Rows = MapConfig.MaxSize, Chunks = [chunk], Rooms = result.Rooms, Doors = result.Doors, Furniture = result.Furniture } };
            var start = exits[0].AnchorCell(chunk);
            var reached = MovementCalculator.Dijkstra(state, start.col, start.row, 3000, -1);
            foreach (var door in result.Doors.Where(d => d.IsWindow != true))
                foreach (var p in new[] { door.From, door.To }.Where(p => chunk.Contains(p[0], p[1])))
                    Assert.True(reached.ContainsKey((p[0], p[1], false)) || reached.ContainsKey((p[0], p[1], true)),
                        $"Seed {seed}: entrance {p[0]},{p[1]} became unreachable");
        }
        Assert.True(plantsOnEdge >= 10, "Vegetation should continue to open edges, not stop at a rectangular inset");
    }

    [Fact]
    public void UnspecifiedVillagePlanDoesNotAlwaysUseTheSameCentralSquare()
    {
        var centers = new HashSet<(int, int)>();
        for (int seed = 1; seed <= 30; seed++)
        {
            var chunk = new MapChunk { Theme = "village", Seed = seed };
            ChunkExit[] exits = [new() { Side = ChunkSide.Left, Offset = 6 }];
            var result = BuildingGenerator.GenerateVillage(chunk, exits, new Dictionary<ChunkSide, char>());
            var repeat = BuildingGenerator.GenerateVillage(chunk, exits, new Dictionary<ChunkSide, char>());
            Assert.Equal(result.Terrain, repeat.Terrain);
            var well = Assert.Single(result.Terrain.SelectMany((row, r) => row.Select((code, c) => (code, c, r))), p => p.code == 'V');
            centers.Add((well.c, well.r));
            Assert.DoesNotContain(result.Terrain.SelectMany(row => row), code => code is 'W' or '~');
        }
        Assert.True(centers.Count >= 6, $"Only {centers.Count} distinct meeting places");
    }
}
