using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class WaterCrossingTests
{
    [Theory]
    [InlineData('W', 10)]
    [InlineData('~', 10)]
    [InlineData('J', 5)]
    public void WaterIsTraversableButBridgeCostsNormalMovement(char terrain, int cost)
    {
        var rows = Enumerable.Repeat(new string('.', 20), 15).ToList();
        rows[6] = rows[6].Remove(2, 1).Insert(2, terrain.ToString());
        var state = new WorldState { Hero = new() { Position = [2, 7] },
            Map = new() { Chunks = [new() { Theme = "plains", Terrain = rows }] } };
        Assert.True(MovementCalculator.CanMoveTo(state, 2, 7, 3, 7, -1));
        Assert.Equal(cost, MovementCalculator.GetStepCost(state, 3, 7));
        var reachable = MovementCalculator.Dijkstra(state, 2, 7, cost, -1);
        Assert.True(reachable.ContainsKey((3, 7, false)) || reachable.ContainsKey((3, 7, true)));
    }

    [Fact]
    public void AtlasRoadAcrossRiverProducesAnActualBridge()
    {
        for (int i = 0; i < 8; i++)
        {
            var state = new WorldState { Hero = new() };
            var (start, error) = LocationGrower.CreatePlanned(state, [new(0, 0, null, "plains")],
                site: new("plains", true, 2, [], RoadHorizontal: false,
                    RoadSides: [ChunkSide.Top, ChunkSide.Bottom]), includeSurroundings: true);
            Assert.Null(error);
            Assert.NotNull(start);
            var bridges = state.Map.Furniture!.Where(f => f.Kind == "bridge").ToList();
            Assert.NotEmpty(bridges);
            foreach (var p in bridges.SelectMany(f => f.Positions))
            {
                Assert.Equal('J', state.Map.TerrainAt(p[0], p[1])!.Code);
                Assert.Contains(state.Map.GroundAt(p[0], p[1])!.Code, new[] { 'W', '~' });
            }
        }
    }
}
