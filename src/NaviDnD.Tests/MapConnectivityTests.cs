using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class MapConnectivityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false, "palisade")]
    [InlineData(true, "palisade")]
    [InlineData(false, "square")]
    [InlineData(true, "square")]
    [InlineData(false, "road")]
    [InlineData(true, "road")]
    public void RiverVillagePreservesWorldChannelAndAccessibleDryHouses(bool horizontal, string layout = "river")
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            var chunk = new MapChunk { Theme = "village", Seed = seed };
            var bands = Enum.GetValues<ChunkSide>().ToDictionary(side => side, _ => 'F');
            var river = new OutdoorGenerator.RiverSpec(horizontal, horizontal ? 8 : 10, 3, seed);
            var natural = OutdoorGenerator.Generate(chunk, [], bands, seed, river);
            var result = BuildingGenerator.GenerateVillage(chunk, MapSampleTests.Exits, bands, layout, river, seed);
            chunk.Terrain = result.Terrain;
            var map = new MapConfig { Chunks = [chunk], Rooms = result.Rooms, Doors = result.Doors, Furniture = result.Furniture };
            Assert.NotEmpty(result.Rooms);
            Check(map, chunk);
            foreach (var room in result.Rooms)
                foreach (var cell in room.Positions)
                    Assert.DoesNotContain(natural.Terrain[cell[1] - 1][cell[0] - 1], new[] { 'W', '~' });
            for (int r = 0; r < MapChunk.Rows; r++)
                for (int c = 0; c < MapChunk.Cols; c++)
                    if (natural.Terrain[r][c] is 'W' or '~')
                        Assert.True(result.Terrain[r][c] is 'W' or '~', $"seed {seed}, [{c},{r}]: river replaced by {result.Terrain[r][c]}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VillageOnlyLocationStillUsesWorldRiver(bool horizontal)
    {
        var state = new WorldState { Hero = new() { Name = "Explorer" } };
        var (chunk, error) = LocationGrower.CreatePlanned(state,
            [new(0, 0, "Riverside settlement", "village")],
            site: new("plains", horizontal, 3, []));
        Assert.Null(error);
        Assert.NotNull(chunk);
        Assert.Contains(chunk.Terrain, row => row.Contains('W'));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false, "palisade")]
    [InlineData(true, "palisade")]
    public void WorldRiverContinuesFromVillageIntoNeighboringForest(bool horizontal, string? layout = null)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var state = new WorldState { Hero = new() { Name = "Explorer" } };
            var (village, error) = LocationGrower.CreatePlanned(state,
                [new(0, 0, "Riverside settlement", "village", Layout: layout),
                 new(horizontal ? 1 : 0, horizontal ? 0 : 1, "Riverbank", "forest")],
                site: new("forest", horizontal, 3, []));
            Assert.Null(error);
            Assert.NotNull(village);
            var forest = Assert.Single(state.Map.Chunks, c => c.Theme == "forest");
            bool Water(char code) => code is 'W' or '~';
            var villageEdge = horizontal ? village.Terrain.Select(row => row[^1]).ToArray() : village.Terrain[^1].ToCharArray();
            var forestEdge = horizontal ? forest.Terrain.Select(row => row[0]).ToArray() : forest.Terrain[0].ToCharArray();
            Assert.True(villageEdge.Zip(forestEdge).Any(pair => Water(pair.First) && Water(pair.Second)),
                "The river must share water cells across the village/forest seam.");
        }
    }

    public static IEnumerable<object[]> OutdoorCases() =>
        from theme in new[] { "forest", "hills", "swamp" }
        from seed in Enumerable.Range(1, 10)
        from horizontal in new[] { false, true }
        select new object[] { theme, seed, horizontal };

    [Theory, MemberData(nameof(OutdoorCases))]
    public void RiverDoesNotDisconnectExits(string theme, int seed, bool horizontal)
    {
        var chunk = new MapChunk { Theme = theme, Seed = seed };
        var result = OutdoorGenerator.Generate(chunk, MapSampleTests.Exits,
            Enum.GetValues<ChunkSide>().ToDictionary(side => side, _ => 'F'), seed,
            new(horizontal, horizontal ? 8 : 10, 4, seed));
        var map = new MapConfig { Chunks = [chunk], Doors = result.Doors };
        chunk.Terrain = result.Terrain;
        Check(map, chunk);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RiverReachesClosedEdgesAsDeepWater(bool horizontal)
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            var chunk = new MapChunk { Theme = "forest", Seed = seed };
            var bands = Enum.GetValues<ChunkSide>().ToDictionary(side => side, _ => 'F');
            var result = OutdoorGenerator.Generate(chunk, [], bands, seed,
                new(horizontal, horizontal ? 8 : 10, 4, seed));
            var first = horizontal ? result.Terrain.Select(row => row[0]) : result.Terrain[0];
            var last = horizontal ? result.Terrain.Select(row => row[^1]) : result.Terrain[^1];
            Assert.Contains('W', first);
            Assert.Contains('W', last);
            Assert.All(first.Concat(last), code => Assert.True(TerrainCatalog.Get(code)!.BlocksMove));
        }
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
            if (layout == "river")
            {
                var roomCells = result.Rooms.SelectMany(r => r.Positions).Select(p => (p[0], p[1])).ToHashSet();
                int entrances = result.Doors.Count(d => d.IsWindow != true && d.IsDoor == true
                    && (roomCells.Contains((d.From[0], d.From[1])) != roomCells.Contains((d.To[0], d.To[1]))));
                Assert.True(entrances >= 2, $"River village seed {seed} has only {entrances} houses");
            }
            if (layout == "island")
            {
                Assert.Contains(result.Terrain, row => row.Contains('W'));
                Assert.Equal(1, result.Terrain.Sum(row => row.Count(c => c == '~')));
                Assert.All(result.Rooms.SelectMany(r => r.Positions), p => Assert.NotEqual('W', result.Terrain[p[1] - 1][p[0] - 1]));
            }
        }
    }


    [Theory]
    [InlineData("house")]
    [InlineData("tavern")]
    [InlineData("shop")]
    [InlineData("temple")]
    [InlineData("manor")]
    [InlineData("barracks")]
    [InlineData("tower")]
    public void IslandApproachWorksForEveryBuildingStyle(string style)
    {
        for (int seed = 1; seed <= 30; seed++)
        {
            var chunk = new MapChunk { Theme = "building", Style = style, Seed = seed };
            var result = BuildingGenerator.Generate(chunk, MapSampleTests.Exits, seed, true,
                new Dictionary<ChunkSide, char>(), [], moat: true);
            chunk.Terrain = result.Terrain;
            var map = new MapConfig { Chunks = [chunk], Rooms = result.Rooms, Doors = result.Doors, Furniture = result.Furniture };
            Check(map, chunk);
            Assert.Equal(1, result.Terrain.Sum(row => row.Count(c => c == '~')));
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
