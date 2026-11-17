using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class VillagePassageTests
{
    [Theory]
    [InlineData("road", false)]
    [InlineData("square", false)]
    [InlineData("palisade", false)]
    [InlineData("river", false)]
    [InlineData("river", true)]
    [InlineData("palisade", true)]
    public void AtMostOneHouseHasAnAccessibleDryThroughRouteAfterGardensAndFurniture(string layout, bool river)
    {
        int passages = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            var chunk = new MapChunk { X = 1, Y = 1, Theme = "village", Seed = seed };
            ChunkExit[] exits = [new() { Side = ChunkSide.Left, Offset = 7 }, new() { Side = ChunkSide.Right, Offset = 7 }];
            var result = BuildingGenerator.GenerateVillage(chunk, exits,
                Enum.GetValues<ChunkSide>().ToDictionary(side => side, _ => 'T'), layout,
                river ? new(true, chunk.OriginRow + 8, 3, seed) : null, seed);
            chunk.Terrain = result.Terrain;
            var world = new WorldState { Map = new() { Cols = 100, Rows = 100, Chunks = [chunk],
                Rooms = result.Rooms, Doors = result.Doors, Furniture = result.Furniture } };
            foreach (var door in result.Doors.Where(d => d.IsWindow != true)) door.IsDoorOpen = true;
            var allRooms = result.Rooms.SelectMany(room => room.Positions).Select(p => (c:p[0],r:p[1])).ToHashSet();
            int localPassages = 0;
            foreach (var house in Components(allRooms))
            {
                var doors = result.Doors.Where(d => d.IsWindow != true
                    && house.Contains((d.From[0],d.From[1])) != house.Contains((d.To[0],d.To[1]))).ToList();
                Assert.InRange(doors.Count, 1, 2);
                if (doors.Count != 2) continue;
                passages++; localPassages++;
                var front = doors[0]; var back = doors[1];
                var start = (c:front.From[0],r:front.From[1]);
                var target = (c:back.From[0],r:back.From[1]);
                Assert.Contains(start, house);
                Assert.Contains(target, house);
                Assert.Equal(front.To[0] - front.From[0], back.From[0] - back.To[0]);
                Assert.Equal(front.To[1] - front.From[1], back.From[1] - back.To[1]);
                var inside = Flood([start], p => house.Contains(p) && world.Map.TerrainAt(p.c,p.r) is { BlocksMove:false },
                    (a,b) => MovementCalculator.CanMoveTo(world,a.c,a.r,b.c,b.r,-1));
                Assert.Contains(target, inside); // Reach the other threshold through the building, not by walking around it.
                Assert.Null(world.Map.FurnitureAt(target.c,target.r));
                var yard = (c:back.To[0],r:back.To[1]);
                Assert.True(MovementCalculator.CanMoveTo(world,target.c,target.r,yard.c,yard.r,-1));
                Assert.False(world.Map.TerrainAt(yard.c,yard.r)!.BlocksMove);
                Assert.DoesNotContain(world.Map.TerrainAt(yard.c,yard.r)!.Code, new[] { 'W','~' });
                Assert.DoesNotContain(result.Doors.Where(d => d.IsWindow == true), d => d.From.SequenceEqual(back.From));
                bool DryYard((int c,int r) p) => chunk.Contains(p.c,p.r) && !allRooms.Contains(p)
                    && world.Map.TerrainAt(p.c,p.r) is { BlocksMove:false, Code:not ('W' or '~') };
                var streets = Enumerable.Range(1,MapChunk.Cols).SelectMany(c => Enumerable.Range(1,MapChunk.Rows)
                    .Select(r => (c:c+chunk.OriginCol,r:r+chunk.OriginRow))).Where(p => world.Map.GroundAt(p.c,p.r)?.Code == '=' && DryYard(p));
                Assert.Contains(yard, Flood(streets,DryYard,(_,_) => true));
            }
            Assert.InRange(localPassages,0,1);
        }
        Assert.True(passages > 0, $"No through-house found for {layout}, river={river}");
    }

    private static IEnumerable<HashSet<(int c,int r)>> Components(HashSet<(int c,int r)> cells)
    {
        var remaining = cells.ToHashSet();
        while (remaining.Count > 0)
        {
            var group = Flood([remaining.First()],remaining.Contains,(_,_) => true);
            remaining.ExceptWith(group);
            yield return group;
        }
    }

    private static HashSet<(int c,int r)> Flood(IEnumerable<(int c,int r)> starts,
        Func<(int c,int r),bool> free, Func<(int c,int r),(int c,int r),bool> step)
    {
        var seen = starts.Where(free).ToHashSet();
        var queue = new Queue<(int c,int r)>(seen);
        while (queue.TryDequeue(out var p))
            foreach (var n in new (int c,int r)[] { (p.c-1,p.r),(p.c+1,p.r),(p.c,p.r-1),(p.c,p.r+1) })
                if (free(n) && step(p,n) && seen.Add(n)) queue.Enqueue(n);
        return seen;
    }
}
