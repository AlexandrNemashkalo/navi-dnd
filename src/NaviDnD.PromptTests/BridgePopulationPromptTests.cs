using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class BridgePopulationPromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task VillagerCanWatchRiverFromExistingDeckWithoutReplacingBridgeOrWater()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var ws = storage.WorldState;
        var chunk = new MapChunk { X = 1, Theme = "village", Seed = 2,
            Exits = [new() { Side = ChunkSide.Left, Offset = 7 }, new() { Side = ChunkSide.Right, Offset = 7 },
                new() { Side = ChunkSide.Top, Offset = 10 }, new() { Side = ChunkSide.Bottom, Offset = 10 }] };
        var generated = BuildingGenerator.GenerateVillage(chunk, chunk.Exits,
            Enum.GetValues<ChunkSide>().ToDictionary(s => s, _ => 'T'), "river", new(true,8,3,2),2);
        chunk.Terrain = generated.Terrain;
        var bridge = Assert.Single(generated.Furniture, f => f.Kind == "bridge");
        chunk.Purpose = "A peaceful riverside village. Place exactly one ordinary villager standing on the existing wooden bridge deck, looking at the river; no other creatures or loose objects. Keep the existing bridge and river intact. This is everyday life, without a quest, danger or combat.";
        ws.Map = new() { Cols = 40, Rows = 15, Chunks = [new() { Populated = true },chunk],
            Rooms = generated.Rooms, Doors = generated.Doors, Furniture = generated.Furniture, Entities = [], Objects = [] };
        ws.Hero!.Position = [3,3];
        ws.World = null;
        ws.Narrative = new();
        ws.History = [];
        ws.Combat = null;
        string Geometry() => JsonSerializer.Serialize(new { chunk.Terrain, ws.Map.Doors,
            furniture = ws.Map.Furniture!.Select(f => new { f.Kind, f.Positions, f.Deleted }) });
        string before = Geometry();
        var context = new AiContextBuilder(ws);
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "PopulateChunk", storage,
            "lang:en\nspeechEnabled:false\n" + context.PopulationState(chunk) + "\n" + context.LocationPlan(chunk) + "\n" + context.ChunkState(chunk));
        output.WriteLine(reply.RawResponse);
        var patch = ChunkPopulationPatch.Prepare(ws, new(ws.Map,chunk,reply.RawResponse));
        Assert.NotNull(patch);
        PromptScenario.ApplyAndParse(storage,patch);
        Assert.Equal(before,Geometry());
        var villager = Assert.Single(ws.Map.Entities!, e => e.Deleted != true);
        Assert.Contains(bridge.Positions,p => p.SequenceEqual(villager.Position));
        Assert.Equal('J',ws.Map.TerrainAt(villager.Position[0],villager.Position[1])!.Code);
        Assert.Contains(ws.Map.GroundAt(villager.Position[0],villager.Position[1])!.Code,new[] { 'W','~' });
        Assert.Empty(ws.Map.Objects!);
        Assert.Empty(ws.History);
        Assert.Empty(ws.Narrative.CurrentArc);
        Assert.False(ws.Combat?.Active == true);
    }
}
