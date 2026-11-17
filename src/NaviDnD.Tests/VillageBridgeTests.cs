using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class VillageBridgeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void SavedBridgeKeepsItsRiverbedAndDeletionPatchRestoresWater()
    {
        var chunk = new MapChunk { Theme = "village", Seed = 2 };
        ChunkExit[] exits = [new() { Side = ChunkSide.Left, Offset = 7 }, new() { Side = ChunkSide.Right, Offset = 7 },
            new() { Side = ChunkSide.Top, Offset = 10 }, new() { Side = ChunkSide.Bottom, Offset = 10 }];
        var generated = BuildingGenerator.GenerateVillage(chunk, exits,
            Enum.GetValues<ChunkSide>().ToDictionary(s => s, _ => 'T'), "river", new(true,8,3,2),2);
        chunk.Terrain = generated.Terrain;
        var original = new WorldState { Map = new() { Chunks = [chunk], Rooms = generated.Rooms,
            Doors = generated.Doors, Furniture = generated.Furniture } };
        var storage = new Storage();
        storage.ApplySavedState(System.Text.Json.JsonSerializer.Serialize(original));
        var map = storage.WorldState.Map;
        int bridgeId = map.Furniture!.FindIndex(f => f.Kind == "bridge");
        Assert.True(bridgeId >= 0);
        var bridge = map.Furniture[bridgeId];
        var deep = bridge.Positions.First(p => map.GroundAt(p[0],p[1])!.Code == 'W');
        Assert.Equal('J',map.TerrainAt(deep[0],deep[1])!.Code);
        Assert.Equal(5,MovementCalculator.GetStepCost(storage.WorldState,deep[0],deep[1]));
        storage.ApplyUpdateWorldState(System.Text.Json.JsonSerializer.Serialize(new
        {
            map = new { furniture = new[] { new { id = bridgeId, deleted = true } } }
        }));
        Assert.Equal('W',map.TerrainAt(deep[0],deep[1])!.Code);
        Assert.True(map.TerrainAt(deep[0],deep[1])!.BlocksMove);
        var reloaded = new Storage();
        reloaded.ApplySavedState(System.Text.Json.JsonSerializer.Serialize(storage.WorldState));
        Assert.Equal('W',reloaded.WorldState.Map.TerrainAt(deep[0],deep[1])!.Code);
        Assert.Equal(generated.Terrain,Assert.Single(reloaded.WorldState.Map.Chunks!).Terrain);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExistingCrossingsHaveTwoDryBanksAndPreserveWaterUnderWalkableDeck(bool horizontal)
    {
        int found = 0;
        for (int seed = 1; seed <= 100; seed++)
        {
            var chunk = new MapChunk { Theme = "village", Seed = seed };
            ChunkExit[] exits = [new() { Side=ChunkSide.Left,Offset=7 },new() { Side=ChunkSide.Right,Offset=7 },
                new() { Side=ChunkSide.Top,Offset=10 },new() { Side=ChunkSide.Bottom,Offset=10 }];
            var result = BuildingGenerator.GenerateVillage(chunk, exits,
                Enum.GetValues<ChunkSide>().ToDictionary(s=>s,_=>'T'), "river", new(horizontal,8,3,seed),seed);
            chunk.Terrain=result.Terrain;
            var ws = new WorldState { Map=new() { Chunks=[chunk],Rooms=result.Rooms,Doors=result.Doors,Furniture=result.Furniture } };
            var bridges=result.Furniture.Where(f=>f.Kind=="bridge").ToList();
            Assert.InRange(bridges.Count,0,1);
            if (bridges.Count==0) continue;
            found++;
            var bridge=bridges[0];
            if(found==1) output.WriteLine($"Bridge witness horizontal={horizontal}, seed={seed}: {string.Join("; ",bridge.Positions.Select(p=>$"[{p[0]},{p[1]}]"))}");
            var cells=bridge.Positions.OrderBy(p=>horizontal?p[1]:p[0]).ToList();
            Assert.Single(cells.Select(p=>horizontal?p[0]:p[1]).Distinct());
            Assert.Contains(cells,p=>ws.Map.GroundAt(p[0],p[1])!.Code=='W');
            foreach(var p in cells)
            {
                Assert.Contains(ws.Map.GroundAt(p[0],p[1])!.Code,new[]{'W','~'});
                Assert.Equal('J',ws.Map.TerrainAt(p[0],p[1])!.Code);
                Assert.False(ws.Map.TerrainAt(p[0],p[1])!.BlocksMove);
            }
            int dc=horizontal?0:1,dr=horizontal?1:0;
            var before=new[]{cells[0][0]-dc,cells[0][1]-dr};
            var after=new[]{cells[^1][0]+dc,cells[^1][1]+dr};
            foreach(var bank in new[]{before,after})
            {
                Assert.False(ws.Map.TerrainAt(bank[0],bank[1])!.BlocksMove);
                Assert.DoesNotContain(ws.Map.GroundAt(bank[0],bank[1])!.Code,new[]{'W','~'});
            }
            var route=new[]{before}.Concat(cells.Select(p=>p.ToArray())).Append(after).ToList();
            for(int i=1;i<route.Count;i++) Assert.True(MovementCalculator.CanMoveTo(ws,route[i-1][0],route[i-1][1],route[i][0],route[i][1],-1));
            bridge.Deleted=true;
            Assert.Contains(cells,p=>ws.Map.TerrainAt(p[0],p[1]) is {Code:'W',BlocksMove:true});
        }
        Assert.True(found>0,$"No bridge found, horizontal={horizontal}");
    }

    [Theory]
    [InlineData("village","Streets and courtyards")]
    [InlineData("forest","Passable cells")]
    public void PopulationCanUseDeepWaterOnlyWhereDeckExists(string theme,string prefix)
    {
        var chunk=new MapChunk {Theme=theme,Terrain=["WWW"]};
        var ws=new WorldState {Hero=new(){Position=[1,2]},Map=new(){Chunks=[chunk],Furniture=[new(){Kind="bridge",Positions=[[2,1]]}]}};
        string context=new AiContextBuilder(ws,[]).ChunkState(chunk);
        string available=Assert.Single(context.Split('\n'),line=>line.StartsWith(prefix,StringComparison.Ordinal));
        Assert.EndsWith("row 1: 2",available.TrimEnd());
        Assert.Contains("walkable",context);
        var patch=ChunkPopulationPatch.Prepare(ws,new(ws.Map,chunk,"""{"map":{"entities":[{"name":"Guard","symbol":"GRD","position":[2,1]}]}}"""))!;
        var storage=new Storage();storage.WorldState.Map=ws.Map;storage.WorldState.Hero=ws.Hero;
        storage.ApplyUpdateWorldState(patch);
        Assert.Equal(new[]{2,1},Assert.Single(storage.WorldState.Map.Entities!).Position);
    }
}
