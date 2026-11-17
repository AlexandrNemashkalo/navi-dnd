using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;
using Cell = NaviDnD.MapGen.Generators.DungeonGenerator.Cell;

namespace NaviDnD.Tests;

public class CaveTunnelTests
{
    [Fact]
    public void BendHidesFarEndUntilExplorerWalksAroundIt()
    {
        var from = new Cell(2,7);
        var to = new Cell(18,7);
        var path = new[] { from }.Concat(DungeonGenerator.FindCavePath(from,to,[],new Random(3))).Append(to).ToList();
        static WorldState Scene(IEnumerable<Cell> cells) => new() { Map = new()
        {
            Rooms = [new() { Name = "Tunnel", Positions = cells.Select(c => new List<int> { c.Col,c.Row }).ToList() }],
            Doors = []
        } };
        var straight = Scene(Enumerable.Range(2,17).Select(c => new Cell(c,7)));
        Assert.True(MovementCalculator.HasLineOfSight(straight,from.Col,from.Row,to.Col,to.Row));
        var cave = Scene(path);
        Assert.False(MovementCalculator.HasLineOfSight(cave,from.Col,from.Row,to.Col,to.Row));
        Assert.False(MovementCalculator.HasLineOfSight(cave,to.Col,to.Row,from.Col,from.Row));
        var reachable = MovementCalculator.Dijkstra(cave,from.Col,from.Row,300,-1);
        Assert.Contains(reachable.Keys,p => p.col == to.Col && p.row == to.Row);
        var nearEnd = path[^2];
        Assert.True(MovementCalculator.HasLineOfSight(cave,nearEnd.Col,nearEnd.Row,to.Col,to.Row));
    }

    [Fact]
    public void LongNaturalTunnelBendsWithoutSelfContactAndPreservesBothApproaches()
    {
        var from=new Cell(2,7); var to=new Cell(18,7);
        for(int seed=1;seed<=30;seed++)
        {
            var path=DungeonGenerator.FindCavePath(from,to,[],new Random(seed));
            Assert.Equal(path,DungeonGenerator.FindCavePath(from,to,[],new Random(seed)));
            Assert.InRange(path.Count-15,2,6);
            Assert.Equal(new Cell(3,7),path[0]);Assert.Equal(new Cell(17,7),path[^1]);
            var all=new[]{from}.Concat(path).Append(to).ToList();
            for(int i=0;i<all.Count;i++)
                for(int j=i+1;j<all.Count;j++)
                {
                    int distance=Math.Abs(all[i].Col-all[j].Col)+Math.Abs(all[i].Row-all[j].Row);
                    if(j==i+1) Assert.Equal(1,distance); else Assert.True(distance>1);
                }
            Assert.All(path,c=>{Assert.InRange(c.Col,2,19);Assert.InRange(c.Row,2,14);});
        }
    }

    [Fact]
    public void ConfinedTunnelRetainsSafeRouteInsteadOfCuttingOtherRooms()
    {
        var blocked=Enumerable.Range(1,20).SelectMany(c=>new[]{new Cell(c,6),new Cell(c,8)}).ToHashSet();
        var path=DungeonGenerator.FindCavePath(new(2,7),new(18,7),blocked,new Random(3));
        Assert.Equal(15,path.Count);Assert.All(path,p=>Assert.Equal(7,p.Row));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(42)]
    public void CaveWitnessKeepsExitCoordinatesAndDeterministicRoomGeometry(int seed)
    {
        var chunk=new MapChunk {Theme="cave",Seed=seed};
        ChunkExit[] exits=[new(){Side=ChunkSide.Left,Offset=7,External=true},new(){Side=ChunkSide.Right,Offset=7}];
        var result=ChunkGenerator.Generate(chunk,exits);
        var repeated=ChunkGenerator.Generate(chunk,exits);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(result),System.Text.Json.JsonSerializer.Serialize(repeated));
        Assert.Contains(result.Doors,d=>d.IsEntry==true && d.From.SequenceEqual(new[]{1,7}));
        Assert.Contains(result.Doors,d=>d.IsEntry==true && d.From.SequenceEqual(new[]{20,7}));
    }
}
