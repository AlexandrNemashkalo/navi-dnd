using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class CoastalLocationTests
{
    public static IEnumerable<object[]> Coasts() =>
        from theme in new[] { "village", "building", "forest" }
        from side in Enum.GetValues<ChunkSide>()
        from layout in theme == "village" ? new string?[] { null, "square", "road", "river", "palisade" } : [null]
        select new object[] { theme, side, layout! };

    [Theory, MemberData(nameof(Coasts))]
    public void CoastalLocationPreservesWaterAndArrivesFromAvailableLand(string theme, ChunkSide coast, string? layout)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var state = new WorldState { Hero = new() { Name = "Explorer" } };
            var (chunk, error) = LocationGrower.CreatePlanned(state,
                [new(0, 0, "Coastal destination", theme, Layout: layout)], site: new("forest", null, 0, [coast]));
            Assert.Null(error);
            Assert.NotNull(chunk);
            Assert.NotEqual(coast, Assert.Single(chunk.Exits, e => e.External).Side);
            var edge = coast switch
            {
                ChunkSide.Left => chunk.Terrain.Select(row => row[0]),
                ChunkSide.Right => chunk.Terrain.Select(row => row[^1]),
                ChunkSide.Bottom => chunk.Terrain[0].AsEnumerable(),
                _ => chunk.Terrain[^1].AsEnumerable(),
            };
            // Natural boundary strips can overlap at the corners; the coastline itself stays water.
            Assert.All(edge.Skip(3).SkipLast(3), code => Assert.Equal('W', code));
            Assert.All(edge, code => Assert.True(TerrainCatalog.Get(code)!.BlocksMove));
            Assert.True(state.Map.TerrainAt(state.Hero.Position[0], state.Hero.Position[1]) is { BlocksMove: false });
        }
    }
}
