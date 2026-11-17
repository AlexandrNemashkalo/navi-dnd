using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class PairedChunkTraversalTests
{
    public static IEnumerable<object[]> Cases()=>
        from pair in new[]{("forest","forest"),("hills","forest"),("village","forest"),("forest","village")}
        from horizontal in new[]{true,false}
        from seed in new[]{3,17,42}
        select new object[]{pair.Item1,pair.Item2,horizontal,seed};

    [Theory,MemberData(nameof(Cases))]
    public void NeighboringChunksShareRiverAndTraversablePairedRoad(string first,string second,bool horizontal,int seed)
    {
        var a=new MapChunk {X=1,Y=1,Theme=first,Seed=seed};
        var b=new MapChunk {X=horizontal?2:1,Y=horizontal?1:2,Theme=second,Seed=seed+41};
        var outward=horizontal?ChunkSide.Left:ChunkSide.Bottom;var inward=ChunkExit.Opposite(outward);
        int offset=horizontal?7:10;
        a.Exits=[new(){Side=outward,Offset=offset,External=true},new(){Side=inward,Offset=offset}];
        b.Exits=[new(){Side=outward,Offset=offset},new(){Side=inward,Offset=offset,External=true}];
        var river=new OutdoorGenerator.RiverSpec(horizontal,horizontal?a.OriginRow+8:a.OriginCol+10,3,seed);
        var map=new MapConfig{Cols=80,Rows=60,Chunks=[a,b],Rooms=[],Doors=[],Furniture=[]};
        void Generate(MapChunk chunk,ChunkSide openSide)
        {
            var bands=Enum.GetValues<ChunkSide>().Where(s=>s!=openSide).ToDictionary(s=>s,_=>'T');
            if(chunk.Theme=="village")
            {
                var result=BuildingGenerator.GenerateVillage(chunk,chunk.Exits,bands,"river",river,seed);
                chunk.Terrain=result.Terrain;map.Rooms.AddRange(result.Rooms);map.Doors.AddRange(result.Doors);map.Furniture.AddRange(result.Furniture);
            }
            else
            {
                var result=OutdoorGenerator.Generate(chunk,chunk.Exits,bands,seed,river);
                chunk.Terrain=result.Terrain;map.Doors.AddRange(result.Doors);
            }
        }
        Generate(a,inward);Generate(b,outward);
        var state=new WorldState{Map=map};
        var edgeA=Enumerable.Range(1,horizontal?MapChunk.Rows:MapChunk.Cols)
            .Select(i=>new ChunkExit{Side=inward,Offset=i}.AnchorCell(a)).Select(p=>(c:p.col,r:p.row)).ToList();
        var edgeB=Enumerable.Range(1,horizontal?MapChunk.Rows:MapChunk.Cols)
            .Select(i=>new ChunkExit{Side=outward,Offset=i}.AnchorCell(b)).Select(p=>(c:p.col,r:p.row)).ToList();
        bool Water((int c,int r) p)=>map.GroundAt(p.c,p.r)?.Code is 'W' or '~';
        Assert.Contains(edgeA,p=>map.GroundAt(p.c,p.r)?.Code=='W');
        Assert.Contains(edgeB,p=>map.GroundAt(p.c,p.r)?.Code=='W');
        var joins=edgeA.Zip(edgeB).Where(pair=>Water(pair.First)&&Water(pair.Second)).ToList();
        Assert.NotEmpty(joins);
        // Every wet seam cell belongs to one connected channel, including carved fords and water under bridge decks.
        var wet=new HashSet<(int c,int r)>{joins[0].First};var queue=new Queue<(int c,int r)>(wet);
        while(queue.TryDequeue(out var p))foreach(var n in new[]{(p.c-1,p.r),(p.c+1,p.r),(p.c,p.r-1),(p.c,p.r+1)})
            if((a.Contains(n.Item1,n.Item2)||b.Contains(n.Item1,n.Item2))&&Water(n)&&wet.Add(n))queue.Enqueue(n);
        Assert.All(edgeA.Concat(edgeB).Where(Water),p=>Assert.Contains(p,wet));
        var from=a.Exits[0].AnchorCell(a);var seamA=a.Exits[1].AnchorCell(a);var seamB=b.Exits[0].AnchorCell(b);var to=b.Exits[1].AnchorCell(b);
        Assert.True(MovementCalculator.CanMoveTo(state,seamA.col,seamA.row,seamB.col,seamB.row,-1));
        Assert.True(MovementCalculator.CanMoveTo(state,seamB.col,seamB.row,seamA.col,seamA.row,-1));
        // Real movement includes furniture overlays, water costs, room boundaries and final generated terrain.
        foreach(var (start,end) in new[]{(from,to),(to,from)})
        {
            var reached=MovementCalculator.Dijkstra(state,start.col,start.row,2000,-1);
            Assert.True(reached.ContainsKey((end.col,end.row,false))||reached.ContainsKey((end.col,end.row,true)),
                $"{first}/{second}, horizontal={horizontal}, seed={seed}: road endpoints disconnected");
        }
    }
}
