using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class VillageRoadTests
{
    [Theory]
    [InlineData(ChunkSide.Left, ChunkSide.Right)]
    [InlineData(ChunkSide.Bottom, ChunkSide.Top)]
    [InlineData(ChunkSide.Left, ChunkSide.Top)]
    [InlineData(ChunkSide.Right, ChunkSide.Right)]
    [InlineData(ChunkSide.Left, ChunkSide.Right, "square")]
    [InlineData(ChunkSide.Bottom, ChunkSide.Top, "square")]
    [InlineData(ChunkSide.Left, ChunkSide.Top, "palisade")]
    [InlineData(ChunkSide.Left, ChunkSide.Right, "river")]
    public void RoadsRespectRealEntrancesAndClosedBoundaries(ChunkSide first, ChunkSide second, string layout = "road")
    {
        for (int seed = 1; seed <= 50; seed++)
        {
            var chunk = new MapChunk { X = 1, Y = 1, Theme = "village", Seed = seed };
            var exits = new[] { first, second }.Distinct().Select(side => new ChunkExit
            {
                Side = side, Offset = side is ChunkSide.Left or ChunkSide.Right ? 6 : 9, External = true
            }).ToArray();
            var result = BuildingGenerator.GenerateVillage(chunk, exits,
                Enum.GetValues<ChunkSide>().ToDictionary(side => side, _ => 'T'), layout);
            chunk.Terrain = result.Terrain;
            var world = new WorldState { Map = new() { Cols = 100, Rows = 100, Chunks = [chunk],
                Rooms = result.Rooms, Doors = result.Doors, Furniture = result.Furniture } };
            var anchors = exits.Select(e => e.AnchorCell(chunk)).ToHashSet();
            foreach (var door in result.Doors.Where(d => d.IsWindow != true)) door.IsDoorOpen = true;
            var start = anchors.First();
            var reached = MovementCalculator.Dijkstra(world, start.col, start.row, 3000, -1);
            foreach (var anchor in anchors)
                Assert.True(reached.ContainsKey((anchor.col, anchor.row, false)) || reached.ContainsKey((anchor.col, anchor.row, true)), $"{first}/{second}, seed {seed}: exit unreachable");
            foreach (var door in result.Doors.Where(d => d.IsWindow != true))
                foreach (var cell in new[] { door.From, door.To }.Where(p => chunk.Contains(p[0], p[1])))
                    Assert.True(reached.ContainsKey((cell[0], cell[1], false)) || reached.ContainsKey((cell[0], cell[1], true)), $"{first}/{second}, seed {seed}: door at {cell[0]},{cell[1]} unreachable");
            // Swimming through the river mouth is allowed; every dry opening still needs a planned entrance.
            for (int c = 0; c < MapChunk.Cols; c++)
                for (int r = 0; r < MapChunk.Rows; r++)
                    if ((c == 0 || r == 0 || c == MapChunk.Cols - 1 || r == MapChunk.Rows - 1)
                        && result.Terrain[r][c] != 'W'
                        && TerrainCatalog.Get(result.Terrain[r][c]) is { BlocksMove: false })
                        Assert.Contains((chunk.OriginCol + c + 1, chunk.OriginRow + r + 1), anchors);
            Assert.NotEmpty(result.Rooms);
            Assert.Single(result.Terrain.SelectMany(row => row), code => code == 'V');
        }
    }

    [Theory]
    [InlineData("square")]
    [InlineData("palisade")]
    [InlineData("river")]
    public void VillageMeetingPlacesVaryWithoutChangingOnRegeneration(string layout)
    {
        var wells = new HashSet<(int c, int r)>();
        for (int seed = 1; seed <= 30; seed++)
        {
            var chunk = new MapChunk { Theme = "village", Seed = seed };
            ChunkExit[] exits = [new() { Side = ChunkSide.Left, Offset = 6 }];
            var bands = Enum.GetValues<ChunkSide>().ToDictionary(side => side, _ => 'T');
            var first = BuildingGenerator.GenerateVillage(chunk, exits, bands, layout);
            var repeated = BuildingGenerator.GenerateVillage(chunk, exits, bands, layout);
            Assert.Equal(first.Terrain, repeated.Terrain);
            var well = Assert.Single(first.Terrain.SelectMany((row, r) => row.Select((code, c) => (code, c, r))), cell => cell.code == 'V');
            wells.Add((well.c, well.r));
        }
        Assert.True(wells.Select(p => p.c).Distinct().Count() >= 3);
        Assert.True(wells.Select(p => p.r).Distinct().Count() >= 2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoadSquareFollowsApproachesAndVariesAlongTheStreet(bool horizontal)
    {
        var wells = new HashSet<(int c, int r)>();
        for (int seed = 1; seed <= 30; seed++)
        {
            var chunk = new MapChunk { Theme = "village", Seed = seed };
            ChunkExit[] exits = [new() { Side = horizontal ? ChunkSide.Left : ChunkSide.Bottom, Offset = 6 },
                new() { Side = horizontal ? ChunkSide.Right : ChunkSide.Top, Offset = 6 }];
            var result = BuildingGenerator.GenerateVillage(chunk, exits, new Dictionary<ChunkSide, char>(), "road");
            var well = Assert.Single(result.Terrain.SelectMany((row, r) => row.Select((code, c) => (code, c, r))), cell => cell.code == 'V');
            Assert.Equal(5, horizontal ? well.r : well.c);
            wells.Add((well.c, well.r));
        }
        Assert.True(wells.Count >= 3, "The meeting place should vary along the street between seeds");
    }
}
