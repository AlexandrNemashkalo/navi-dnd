using System.Reflection;
using System.Text.Json;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class RiverBuildingRenderingTests
{
    [Fact]
    public void BankHouseWithUpperFloorKeepsRiverEntryAndStairsAccessible()
    {
        // CreatePlanned intentionally selects a random layout. Rejects must be atomic; export one
        // accepted river-in-yard scene, while fixed-seed geometry coverage lives in RiverBuildingTests.
        WorldState? accepted=null;var errors=new List<string>();
        for(int attempt=0;attempt<40;attempt++)
        {
            var state=new WorldState {Hero=new(){Name="Explorer",Symbol="HRO",Position=[1,1]}};
            string before=JsonSerializer.Serialize(state);
            var (start,error)=LocationGrower.CreatePlanned(state,[new(0,0,"Southern riverbank","forest"),
                new(1,0,"Southern woodland","forest"),new(1,1,"Eastern riverbank","forest"),
                new(0,1,"Bank house","building",Building:"house"),new(0,1,"Upper rooms","building",Z:1,Building:"house")],
                site:new("forest",true,3,[]));
            if(error!=null){Assert.Null(start);Assert.Equal(before,JsonSerializer.Serialize(state));errors.Add(error);continue;}
            var house=state.Map.Chunks.Single(c=>c.Theme=="building"&&c.Z==0);
            if(house.Terrain!.Any(row=>row.Contains('W'))){accepted=state;break;}
        }
        Assert.NotNull(accepted);
        var ws=accepted;Assert.NotNull(ws.Hero.Position);Assert.False(ws.Map.TerrainAt(ws.Hero.Position[0],ws.Hero.Position[1])!.BlocksMove);
        var ground=ws.Map.Chunks.Single(c=>c.Theme=="building"&&c.Z==0);
        var upper=ws.Map.Chunks.Single(c=>c.Theme=="building"&&c.Z==1);
        Assert.NotEmpty(ws.Map.Stairs!);
        var reached=Reach(ws);
        while(true)
        {
            var doors=ws.Map.Doors.Where(d=>d.IsDoor==true&&d.IsDoorOpen!=true&&d.Hidden!=true&&d.IsWindow!=true
                &&(reached.Contains((d.From[0],d.From[1]))||reached.Contains((d.To[0],d.To[1])))).ToList();
            if(doors.Count==0)break;
            foreach(var door in doors)door.IsDoorOpen=true;
            reached=Reach(ws);
        }
        foreach(var stair in ws.Map.Stairs!)
        {
            Assert.Contains((stair.A[0],stair.A[1]),reached);Assert.Contains((stair.B[0],stair.B[1]),reached);
            Assert.Equal((stair.B[0],stair.B[1]),ws.Map.StairTarget(stair.A[0],stair.A[1]));
        }
        foreach(var room in ws.Map.Rooms)
            foreach(var cell in room.Positions.Where(p=>ws.Map.TerrainAt(p[0],p[1]) is {BlocksMove:false}))
                Assert.Contains((cell[0],cell[1]),reached);
        foreach(var c in ws.Map.Chunks.Where(c=>c.Z==0))foreach(var exit in c.Exits)
        {var p=exit.AnchorCell(c);Assert.Contains((p.col,p.row),reached);}
        // Meanders can cross the east OR south seam of the bank house: follow the whole channel.
        var water=ws.Map.Chunks.Where(c=>c.Z==0).SelectMany(c=>Enumerable.Range(1,MapChunk.Cols)
            .SelectMany(x=>Enumerable.Range(1,MapChunk.Rows).Select(y=>(c:c.OriginCol+x,r:c.OriginRow+y))))
            .Where(p=>ws.Map.GroundAt(p.c,p.r)?.Code is 'W' or '~').ToHashSet();
        var wet=new HashSet<(int c,int r)>{water.First()};var wetQueue=new Queue<(int c,int r)>(wet);
        while(wetQueue.TryDequeue(out var p))foreach(var n in new[]{(p.c-1,p.r),(p.c+1,p.r),(p.c,p.r-1),(p.c,p.r+1)})
            if(water.Contains(n)&&wet.Add(n))wetQueue.Enqueue(n);
        Assert.Equal(water.Count,wet.Count);
        Assert.Contains(wet,p=>ground.Contains(p.c,p.r));
        Assert.Contains(wet,p=>!ground.Contains(p.c,p.r));
        var dir=ArtifactDirectory();File.WriteAllText(Path.Combine(dir,"accepted-state.json"),JsonSerializer.Serialize(ws));
        File.WriteAllLines(Path.Combine(dir,"rejected-attempts.txt"),errors);
        foreach(var c in ws.Map.Chunks.OrderBy(c=>c.Z).ThenBy(c=>c.Y).ThenBy(c=>c.X))Export(ws,c,Path.Combine(dir,"render"));
    }

    private static HashSet<(int c,int r)> Reach(WorldState ws)
    {
        var start=(ws.Hero.Position![0],ws.Hero.Position[1]);var seen=new HashSet<(int c,int r)>{start};var queue=new Queue<(int c,int r)>(seen);
        while(queue.TryDequeue(out var p))
        {
            for(int dc=-1;dc<=1;dc++)for(int dr=-1;dr<=1;dr++)
                if((dc!=0||dr!=0)&&MovementCalculator.CanMoveTo(ws,p.c,p.r,p.c+dc,p.r+dr,-1)&&seen.Add((p.c+dc,p.r+dr)))queue.Enqueue((p.c+dc,p.r+dr));
            if(ws.Map.StairTarget(p.c,p.r) is {} target&&ws.Map.TerrainAt(target.col,target.row) is {BlocksMove:false}&&seen.Add(target))queue.Enqueue(target);
        }
        return seen;
    }
    private static string ArtifactDirectory()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!Directory.Exists(Path.Combine(root.FullName,"scripts")))root=root.Parent;
        Assert.NotNull(root);string path=Path.Combine(root.FullName,"artifacts","river-building-integration");Directory.CreateDirectory(path);return path;
    }
    private static void Export(WorldState state,MapChunk chunk,string dir)
    {
        Directory.CreateDirectory(dir);var storage=new Storage();storage.WorldState.Map=state.Map;
        var position=chunk.Z==0?state.Hero.Position:state.Map.Stairs!.SelectMany(s=>new[]{s.A,s.B}).First(p=>chunk.Contains(p[0],p[1]));
        storage.WorldState.Hero=new(){Name="Explorer",Symbol="HRO",Position=position};
        var display=new DisplayConfig{CameraCol=chunk.OriginCol+1,CameraRow=chunk.OriginRow+1};var config=new AppConfig{RevealMap=true,SaveExploredCells=false};
        var provider=new MapObjectsProvider(storage.WorldState,display,storage,config);var map=new MapDisplay(storage.WorldState,display,new LegendDisplay(storage,display,config));
        var actions=(List<Action>)typeof(MapDisplay).GetMethod("BuildLocationRows",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(map,[provider])!;
        var rows=new List<string>();var old=Console.Out;
        try{foreach(var action in actions){using var writer=new StringWriter();Console.SetOut(writer);action();rows.Add(writer.ToString());}}
        finally{Console.SetOut(old);}
        File.WriteAllText(Path.Combine(dir,$"floor{chunk.Z}-x{chunk.X}-y{chunk.Y}-{chunk.Theme}.json"),JsonSerializer.Serialize(rows));
    }
}
