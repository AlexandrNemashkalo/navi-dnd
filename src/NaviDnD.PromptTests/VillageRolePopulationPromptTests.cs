using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class VillageRolePopulationPromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task EverydayWorkshopAndStorehouseKeepGeneratedGeometryDuringPopulation()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var ws = storage.WorldState;
        var candidates = new[] { 17, 42 }.Select(seed =>
        {
            var chunk = new MapChunk { X = 1, Y = 0, Theme = "village", Seed = seed,
                Exits = [new() { Side = ChunkSide.Left, Offset = 6 }, new() { Side = ChunkSide.Right, Offset = 6 }] };
            var generated = BuildingGenerator.GenerateVillage(chunk, chunk.Exits,
                Enum.GetValues<ChunkSide>().ToDictionary(side => side, _ => 'T'), "road");
            chunk.Terrain = generated.Terrain;
            return (chunk, generated);
        }).ToList();
        static bool HasRole(Room room, string format) =>
            Enumerable.Range(1, 30).Any(number => room.Name == L.WF(format, number));
        var chosen = candidates.FirstOrDefault(c => c.generated.Rooms.Any(r => HasRole(r, "Workshop {0}"))
            && c.generated.Rooms.Any(r => HasRole(r, "Storehouse {0}")));
        Assert.NotNull(chosen.chunk);
        var target = chosen.chunk;
        var plan = chosen.generated;
        int workshopId = plan.Rooms.FindIndex(r => HasRole(r, "Workshop {0}"));
        int storehouseId = plan.Rooms.FindIndex(r => HasRole(r, "Storehouse {0}"));
        target.Purpose = $"A quiet everyday village. Place one artisan doing ordinary repairs in existing workshop room id:{workshopId} and one loose bundle of linen in existing storehouse room id:{storehouseId}; no other new creatures or objects. Give both rooms concrete descriptive aiInfo and keep their established roles. No new quest, danger, combat or mystery; preserve all generated geometry and furniture.";
        ws.Map = new() { Cols = 40, Rows = 15, Chunks = [new() { Populated = true }, target],
            Rooms = plan.Rooms, Doors = plan.Doors, Furniture = plan.Furniture, Objects = [], Entities = [] };
        ws.World = new() { Place = "Quiet village" };
        ws.Narrative = new() { World = "A peaceful rural district with ordinary village work and trade." };
        ws.Hero!.Position = [3, 3];
        ws.Combat = null;
        ws.History = [];

        string Geometry() => JsonSerializer.Serialize(new
        {
            ws.Map.Cols, ws.Map.Rows,
            rooms = ws.Map.Rooms!.Select(r => new { r.Positions, r.Passage, r.Deleted }),
            furniture = ws.Map.Furniture!.Select(f => new { f.Positions, f.Kind, f.Deleted }),
            doors = ws.Map.Doors,
            terrain = ws.Map.Chunks!.Select(c => new { c.X, c.Y, c.Terrain })
        });
        string before = Geometry();
        var context = new AiContextBuilder(ws);
        string request = "lang:en\nspeechEnabled:false\n" + context.PopulationState(target)
            + "\n" + context.LocationPlan(target) + "\n" + context.ChunkState(target);
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "PopulateChunk", storage, request);
        output.WriteLine($"Village seed: {target.Seed}; workshop room: {workshopId}; storehouse room: {storehouseId}\n{reply.RawResponse}");
        string? patch = ChunkPopulationPatch.Prepare(ws, new(ws.Map, target, reply.RawResponse));
        Assert.NotNull(patch);
        PromptScenario.ApplyAndParse(storage, patch);
        foreach (int id in new[] { workshopId, storehouseId })
            Assert.True(ws.Map.Rooms![id].AiInfo?.Any(line => !string.IsNullOrWhiteSpace(line)) == true,
                $"Room {id} should receive concrete everyday activity or supply details.");
        Assert.Equal(before, Geometry());
        Assert.Empty(ws.Narrative.CurrentArc);
        Assert.Null(ws.Combat);
        Assert.Empty(ws.History);
        var inhabitants = ws.Map.Entities!.Cast<CellEntity>().Concat(ws.Map.Objects!).Where(e => e.Deleted != true).ToList();
        var artisan = Assert.Single(ws.Map.Entities!);
        var supplies = Assert.Single(ws.Map.Objects!);
        Assert.Same(ws.Map.Rooms![workshopId], ws.Map.RoomAt(artisan.Position[0], artisan.Position[1]));
        Assert.Same(ws.Map.Rooms[storehouseId], ws.Map.RoomAt(supplies.Position[0], supplies.Position[1]));
        var thresholds = ws.Map.Doors!.Where(d => d.IsWindow != true)
            .SelectMany(d => new[] { (d.From[0],d.From[1]), (d.To[0],d.To[1]) }).ToHashSet();
        var positions = new HashSet<(int, int)>();
        foreach (var item in inhabitants)
        {
            int c = item.Position[0], r = item.Position[1];
            Assert.True(target.Contains(c, r));
            Assert.True(positions.Add((c, r)), "Population overlaps another inhabitant.");
            Assert.DoesNotContain((c,r), thresholds);
            Assert.Null(ws.Map.FurnitureAt(c, r));
            Assert.False(ws.Map.TerrainAt(c, r)?.BlocksMove == true);
            Assert.True(ws.Map.TerrainAt(c, r) != null || ws.Map.RoomAt(c, r) != null);
        }
        output.WriteLine("Workshop details: " + string.Join(" | ", ws.Map.Rooms![workshopId].AiInfo ?? []));
        output.WriteLine("Storehouse details: " + string.Join(" | ", ws.Map.Rooms![storehouseId].AiInfo ?? []));
    }
}
