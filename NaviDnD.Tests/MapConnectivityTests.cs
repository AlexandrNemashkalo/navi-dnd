using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class MapConnectivityTests
{
    public static IEnumerable<object[]> OutdoorCases() =>
        from theme in new[] { "forest", "hills", "swamp" }
        from seed in Enumerable.Range(1, 10)
        from horizontal in new[] { false, true }
        select new object[] { theme, seed, horizontal };

    [Theory, MemberData(nameof(OutdoorCases))]
    public void RiverDoesNotDisconnectExits(string theme, int seed, bool horizontal)
    {
        var chunk = new MapChunk { Theme = theme, Seed = seed };
        var result = OutdoorGenerator.Generate(chunk, MapSampleTests.Exits, new Dictionary<ChunkSide, char>(), seed,
            new(horizontal, horizontal ? 8 : 10, 4, seed));
        var map = new MapConfig { Chunks = [chunk], Doors = result.Doors };
        chunk.Terrain = result.Terrain;
        Check(map, chunk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("square")]
    [InlineData("road")]
    [InlineData("river")]
    [InlineData("palisade")]
    [InlineData("island")]
    public void EveryExitAndDoorIsReachable(string? layout)
    {
        for (int seed = 1; seed <= 10; seed++)
        {
            var chunk = new MapChunk { Theme = layout == "island" ? "building" : "village", Style = "tavern", Seed = seed };
            var result = layout == "island"
                ? BuildingGenerator.Generate(chunk, MapSampleTests.Exits, seed, true, new Dictionary<ChunkSide, char>(), [], moat: true)
                : BuildingGenerator.GenerateVillage(chunk, MapSampleTests.Exits, new Dictionary<ChunkSide, char>(), layout);
            chunk.Terrain = result.Terrain;
            var map = new MapConfig { Chunks = [chunk], Rooms = result.Rooms, Doors = result.Doors, Furniture = result.Furniture };
            Check(map, chunk);
            if (layout == "island")
            {
                Assert.Contains(result.Terrain, row => row.Contains('W'));
                Assert.Equal(1, result.Terrain.Sum(row => row.Count(c => c == '~')));
                Assert.All(result.Rooms.SelectMany(r => r.Positions), p => Assert.NotEqual('W', result.Terrain[p[1] - 1][p[0] - 1]));
            }
        }
    }

    private static void Check(MapConfig map, MapChunk chunk)
    {
        var roomOf = (map.Rooms ?? []).SelectMany((r, i) => r.Positions.Select(p => (p, i)))
            .ToDictionary(x => (x.p[0], x.p[1]), x => x.i);
        var links = new HashSet<((int, int), (int, int))>();
        foreach (var d in map.Doors!.Where(d => d.IsWindow != true))
        {
            var a = (d.From[0], d.From[1]); var b = (d.To[0], d.To[1]);
            links.Add((a, b)); links.Add((b, a));
        }
        bool Free((int c, int r) x) => chunk.Contains(x.c, x.r) && map.TerrainAt(x.c, x.r) is { BlocksMove: false };
        var start = MapSampleTests.Exits[0].AnchorCell(chunk);
        Assert.True(Free(start));
        var seen = new HashSet<(int, int)> { start };
        var queue = new Queue<(int c, int r)>([start]);
        while (queue.Count > 0)
        {
            var x = queue.Dequeue();
            foreach (var n in new[] { (x.c+1,x.r), (x.c-1,x.r), (x.c,x.r+1), (x.c,x.r-1) })
                if (Free(n) && (roomOf.GetValueOrDefault(x, -1) == roomOf.GetValueOrDefault(n, -1) || links.Contains((x,n))) && seen.Add(n))
                    queue.Enqueue(n);
        }
        foreach (var exit in MapSampleTests.Exits) Assert.Contains(exit.AnchorCell(chunk), seen);
        foreach (var d in map.Doors.Where(d => d.IsWindow != true))
        {
            if (chunk.Contains(d.From[0], d.From[1])) Assert.Contains((d.From[0], d.From[1]), seen);
            if (chunk.Contains(d.To[0], d.To[1])) Assert.Contains((d.To[0], d.To[1]), seen);
        }
    }
}
