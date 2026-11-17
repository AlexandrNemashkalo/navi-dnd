using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class TerrainPopulationPromptTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(2, "forest")]
    [InlineData(18, "forest")]
    [InlineData(2, "village")]
    [InlineData(18, "village")]
    [InlineData(2, "building")]
    [InlineData(18, "building")]
    public async Task FishingTraceUsesRealBankInsteadOfImaginedWater(int waterColumn, string theme)
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var ws = storage.WorldState;
        var row = new string('.', 20).ToCharArray();
        row[waterColumn - 1] = '~';
        var chunk = new MapChunk { X = 1, Y = 0, Theme = theme,
            Purpose = "A quiet fishing spot: place one abandoned fishing basket on dry ground directly beside the existing water. Its contents show ordinary local fishing, not a quest or danger. No creatures, extra objects, or new terrain.",
            Terrain = Enumerable.Repeat(new string(row), 15).ToList() };
        ws.World = new() { Place = "Fishing bank" };
        ws.Map = new() { Cols = 40, Chunks = [new() { Populated = true }, chunk],
            Rooms = [], Objects = [], Entities = [], Doors = [], Furniture = [] };
        ws.Narrative = new() { World = "A peaceful rural district where villagers fish for their meals." };
        ws.Hero!.Position = [3, 3];
        ws.History = [];
        ws.Combat = null;
        var context = new AiContextBuilder(ws);
        string request = "lang:en\nspeechEnabled:false\n" + context.PopulationState(chunk)
            + "\n" + context.LocationPlan(chunk) + "\n" + context.ChunkState(chunk);
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "PopulateChunk", storage, request);
        output.WriteLine($"Water column: {chunk.OriginCol + waterColumn}\n{reply.RawResponse}");
        string patch = ChunkPopulationPatch.Prepare(ws, new(ws.Map, chunk, reply.RawResponse))!;
        Assert.NotNull(patch);
        PromptScenario.ApplyAndParse(storage, patch);
        var basket = Assert.Single(ws.Map.Objects!);
        Assert.Contains("basket", basket.Name!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Math.Abs(basket.Position![0] - (chunk.OriginCol + waterColumn)));
        Assert.Equal('.', chunk.Terrain[basket.Position[1] - 1][basket.Position[0] - chunk.OriginCol - 1]);
        Assert.Empty(ws.Map.Entities!);
        Assert.Empty(ws.Narrative.CurrentArc);
        Assert.Empty(ws.History);
        Assert.Equal(new[] { 3, 3 }, ws.Hero.Position);
    }
}
