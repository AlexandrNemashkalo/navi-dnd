using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class VillagePlazaTests
{
    [Fact]
    public void FootprintsVaryInShapeWhileKeepingAConnectedRingAroundTheWell()
    {
        var shapes = new HashSet<string>();
        bool rounded = false, asymmetric = false;
        for (int seed = 1; seed <= 30; seed++)
        {
            var cells = BuildingGenerator.VillagePlazaOffsets(seed).ToHashSet();
            Assert.Equal(13, cells.Count);
            for (int u = -1; u <= 1; u++)
                for (int v = -1; v <= 1; v++) Assert.Contains((u,v), cells);
            Assert.DoesNotContain(cells, p => Math.Abs(p.u) > 2 || Math.Abs(p.v) > 2);
            rounded |= cells.Contains((0,2)) && cells.Contains((0,-2));
            asymmetric |= cells.Any(p => !cells.Contains((p.u,-p.v)));
            shapes.Add(string.Join(";", cells.OrderBy(p => p.u).ThenBy(p => p.v)));
            cells.Remove((0,0)); // The well itself blocks movement.
            var reached = new HashSet<(int u,int v)> { (-1,0) };
            var queue = new Queue<(int u,int v)>([(-1,0)]);
            while (queue.TryDequeue(out var p))
                foreach (var next in new[] { (p.u-1,p.v), (p.u+1,p.v), (p.u,p.v-1), (p.u,p.v+1) })
                    if (cells.Contains(next) && reached.Add(next)) queue.Enqueue(next);
            Assert.True(cells.SetEquals(reached));
        }
        Assert.True(rounded && asymmetric, "Include rounded squares and asymmetric street forecourts, not just rotated rectangles.");
        Assert.True(shapes.Count >= 3);
    }

    [Theory]
    [InlineData(9)] // rounded square
    [InlineData(17)] // street widening
    [InlineData(3)] // staggered forecourt
    public void PlannedFootprintActuallyReservesGroundAndSetsHousesBack(int seed)
    {
        var chunk = new MapChunk { X = 1, Y = 1, Theme = "village", Seed = seed };
        ChunkExit[] exits = [new() { Side = ChunkSide.Left, Offset = 7 }, new() { Side = ChunkSide.Right, Offset = 7 }];
        var result = BuildingGenerator.GenerateVillage(chunk, exits,
            Enum.GetValues<ChunkSide>().ToDictionary(side => side, _ => 'T'), "road");
        var well = Assert.Single(result.Terrain.SelectMany((row,r) => row.Select((code,c) => (code,c,r))), p => p.code == 'V');
        var rooms = result.Rooms.SelectMany(room => room.Positions).Select(p => (p[0],p[1])).ToHashSet();
        foreach (var (u,v) in BuildingGenerator.VillagePlazaOffsets(seed))
        {
            int c = well.c + u, r = well.r + v;
            Assert.Equal(u == 0 && v == 0 ? 'V' : '=', result.Terrain[r][c]);
            Assert.DoesNotContain((chunk.OriginCol + c + 1,chunk.OriginRow + r + 1), rooms);
        }
        Assert.NotEmpty(rooms);
    }
}
