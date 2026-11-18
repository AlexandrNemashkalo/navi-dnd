using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class MapOutdoorTerrainTests
{
    [Fact]
    public void ThroughTrailsDoNotEndAtAnEmptyPlanningHub()
    {
        for (int seed = 1; seed <= 100; seed++)
        {
            var chunk = new MapChunk { Theme = "forest", Seed = seed };
            ChunkExit[] exits = [new() { Side = ChunkSide.Left, Offset = 7 }, new() { Side = ChunkSide.Right, Offset = 7 }];
            var result = OutdoorGenerator.Generate(chunk, exits, new Dictionary<ChunkSide, char>(), seed);
            var anchors = exits.Select(e => e.AnchorCell(chunk)).Select(p => (p.col - 1, p.row - 1)).ToHashSet();
            var trail = result.Terrain.SelectMany((row, r) => row.Select((code, c) => (code, c, r)))
                .Where(cell => cell.code == '=').Select(cell => (cell.c, cell.r)).ToHashSet();
            foreach (var cell in trail.Where(cell => !anchors.Contains(cell)))
                Assert.True(new[] { (cell.c - 1, cell.r), (cell.c + 1, cell.r), (cell.c, cell.r - 1), (cell.c, cell.r + 1) }.Count(trail.Contains) >= 2,
                    $"Seed {seed}: purposeless trail end at {cell}");
        }
    }

    public static IEnumerable<object[]> Biomes() =>
        new[] { "forest", "plains", "hills", "mountain", "swamp", "desert", "snow", "taiga" }
            .Select(theme => new object[] { theme });

    [Fact]
    public void CornerExitsCanStepIntoTheInterior()
    {
        ChunkExit[] exits = [new() { Side = ChunkSide.Left, Offset = 1 },
            new() { Side = ChunkSide.Right, Offset = MapChunk.Rows }];
        var chunk = new MapChunk { Theme = "forest", Seed = 12 };
        var result = OutdoorGenerator.Generate(chunk, exits,
            Enum.GetValues<ChunkSide>().ToDictionary(side => side, _ => 'F'), 12);
        foreach (var (c, r) in new[] { (0, 0), (1, 0), (MapChunk.Cols - 1, MapChunk.Rows - 1), (MapChunk.Cols - 2, MapChunk.Rows - 1) })
            Assert.False(TerrainCatalog.Get(result.Terrain[r][c])!.BlocksMove);
        var reached = new HashSet<(int c, int r)> { (0, 0) };
        var queue = new Queue<(int c, int r)>([(0, 0)]);
        while (queue.TryDequeue(out var cell))
            foreach (var next in new (int c, int r)[] { (cell.c - 1, cell.r), (cell.c + 1, cell.r), (cell.c, cell.r - 1), (cell.c, cell.r + 1) })
                if (next.c >= 0 && next.c < MapChunk.Cols && next.r >= 0 && next.r < MapChunk.Rows
                    && !TerrainCatalog.Get(result.Terrain[next.r][next.c])!.BlocksMove && reached.Add(next)) queue.Enqueue(next);
        Assert.Contains((MapChunk.Cols - 1, MapChunk.Rows - 1), reached);
    }

    [Theory, MemberData(nameof(Biomes))]
    public void AllWalkableGroundAndExitsRemainConnected(string theme)
    {
        for (int seed = 1; seed <= 100; seed++)
        {
            var chunk = new MapChunk { X = seed % 5, Y = seed % 6, Theme = theme, Seed = seed };
            var bands = Enum.GetValues<ChunkSide>().ToDictionary(side => side, _ => '^');
            var result = OutdoorGenerator.Generate(chunk, MapSampleTests.Exits, bands, seed,
                seed % 2 == 0 ? new(seed % 4 == 0, seed % 4 == 0 ? chunk.OriginRow + 8 : chunk.OriginCol + 10, 4, seed) : null);
            Assert.Equal(MapChunk.Rows, result.Terrain.Count);
            Assert.All(result.Terrain, row => Assert.Equal(MapChunk.Cols, row.Length));
            var walkable = result.Terrain.SelectMany((row, r) => row.Select((code, c) => (code, c, r)))
                .Where(cell => cell.code != 'W' && TerrainCatalog.Get(cell.code) is { BlocksMove: false })
                .Select(cell => (cell.c, cell.r)).ToHashSet();
            Assert.NotEmpty(walkable);
            var start = walkable.First();
            var reached = new HashSet<(int c, int r)> { start };
            var queue = new Queue<(int c, int r)>([start]);
            while (queue.TryDequeue(out var cell))
                foreach (var next in new[] { (cell.c - 1, cell.r), (cell.c + 1, cell.r), (cell.c, cell.r - 1), (cell.c, cell.r + 1) })
                    if (walkable.Contains(next) && reached.Add(next)) queue.Enqueue(next);
            Assert.True(walkable.SetEquals(reached), $"{theme} seed {seed}: isolated walkable cells");
            foreach (var exit in MapSampleTests.Exits)
            {
                var (c, r) = exit.AnchorCell(chunk);
                Assert.Contains((c - chunk.OriginCol - 1, r - chunk.OriginRow - 1), reached);
            }
            var exits = MapSampleTests.Exits.Select(exit => exit.AnchorCell(chunk))
                .Select(cell => (cell.col - chunk.OriginCol - 1, cell.row - chunk.OriginRow - 1)).ToHashSet();
            foreach (var cell in walkable.Where(cell => cell.c == 0 || cell.r == 0 || cell.c == MapChunk.Cols - 1 || cell.r == MapChunk.Rows - 1))
                Assert.Contains(cell, exits);
        }
    }

    [Theory, MemberData(nameof(Biomes))]
    public void TerrainIsRepeatableAndSeedsProduceDistinctPlaces(string theme)
    {
        var maps = new HashSet<string>();
        for (int seed = 1; seed <= 20; seed++)
        {
            var chunk = new MapChunk { Theme = theme, Seed = seed };
            var a = OutdoorGenerator.Generate(chunk, MapSampleTests.Exits, new Dictionary<ChunkSide, char>(), seed);
            var b = OutdoorGenerator.Generate(chunk, MapSampleTests.Exits, new Dictionary<ChunkSide, char>(), seed);
            Assert.Equal(a.Terrain, b.Terrain);
            maps.Add(string.Join("\n", a.Terrain));
            if (theme is "snow" or "taiga") Assert.DoesNotContain(a.Terrain, row => row.Contains('='));
        }
        Assert.Equal(20, maps.Count);
    }
}
