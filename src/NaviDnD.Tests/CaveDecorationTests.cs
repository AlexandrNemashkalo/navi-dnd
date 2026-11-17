using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class CaveDecorationTests
{
    [Fact]
    public void NaturalPatchesAreGroupedDeterministicAndLeaveDoorApproachesDry()
    {
        var room = new Room { Positions = (from r in Enumerable.Range(3, 9)
            from c in Enumerable.Range(3, 14) select new List<int> { c, r }).ToList() };
        Door[] doors = [new() { From = [3, 7], To = [2, 7] }];
        var characters = new HashSet<(bool wet, bool scree)>();
        for (int seed = 1; seed <= 30; seed++)
        {
            var chunk = new MapChunk { Theme = "cave", Seed = seed };
            var terrain = DungeonDecorator.Decorate(chunk, [room], doors);
            Assert.Equal(terrain, DungeonDecorator.Decorate(chunk, [room], doors));
            bool hasWet = terrain.Any(row => row.Contains('u'));
            bool hasScree = terrain.Any(row => row.Contains('r'));
            Assert.True(hasWet || hasScree);
            characters.Add((hasWet, hasScree));
            foreach (var p in room.Positions)
            {
                char code = terrain[p[1] - 1][p[0] - 1];
                if (Math.Abs(p[0] - 3) + Math.Abs(p[1] - 7) <= 1)
                    Assert.DoesNotContain(code, new[] { 'm', 'u', 'r', 'S', 'R' });
            }
            // A wet niche remains a contiguous landmark, rather than isolated random puddles.
            var wet = room.Positions.Where(p => terrain[p[1] - 1][p[0] - 1] is 'm' or 'u')
                .Select(p => (c: p[0], r: p[1])).ToHashSet();
            if (!hasWet) continue;
            Assert.True(wet.Count >= 3);
            var seen = new HashSet<(int c, int r)> { wet.First() };
            while (true)
            {
                var next = wet.Where(p => !seen.Contains(p) && seen.Any(q =>
                    Math.Abs(p.c - q.c) + Math.Abs(p.r - q.r) == 1)).ToList();
                if (next.Count == 0) break;
                seen.UnionWith(next);
            }
            Assert.Equal(wet.Count, seen.Count);
        }
        Assert.Equal(3, characters.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NarrowTunnelNeverReceivesSlowOrBlockingDecoration(bool passage)
    {
        var room = new Room { Passage = passage, Positions = Enumerable.Range(1, 20)
            .Select(c => new List<int> { c, 7 }).ToList() };
        for (int seed = 1; seed <= 30; seed++)
        {
            var terrain = DungeonDecorator.Decorate(new MapChunk { Theme = "cave", Seed = seed }, [room], []);
            Assert.All(terrain[6], code => Assert.Contains(code, new[] { 'q', 'b' }));
        }
    }
}
