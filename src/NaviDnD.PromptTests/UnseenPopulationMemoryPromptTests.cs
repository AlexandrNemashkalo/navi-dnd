using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class UnseenPopulationMemoryPromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task UndiscoveredNpcMotiveSurvivesPopulationAndReloadWithoutEnteringTheJournal()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        var chunk = new MapChunk { X = 1, Y = 0, Theme = "plains",
            Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList(),
            Purpose = "A quiet supply yard. Add one neutral clerk named Iven, busy checking deliveries. Unknown to the hero, Iven is concealing a missing shipment whose exact ledger reference is ORCHID-73 to protect his younger brother from dismissal. This motive and reference are established facts for a later conversation, not a quest already offered or accepted. No other inhabitants, danger or objects are needed." };
        state.World = new() { Place = "Supply yard" };
        state.Map = new() { Cols = 40, Rows = 15, Chunks = [new() { Populated = true }, chunk],
            Rooms = [], Doors = [], Furniture = [], Objects = [], Entities = [] };
        state.Narrative = new() { World = "A peaceful district with ordinary work, family obligations and trade." };
        state.Hero!.Position = [3, 3];
        state.History = [];
        state.Combat = null;
        var context = new AiContextBuilder(state);
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "PopulateChunk", storage,
            "lang:en\nspeechEnabled:false\n" + context.PopulationState(chunk) + "\n" + context.LocationPlan(chunk) + "\n" + context.ChunkState(chunk));
        output.WriteLine(reply.RawResponse);
        using var response = JsonDocument.Parse(reply.RawResponse);
        Assert.False(response.RootElement.TryGetProperty("narrative", out _), "Background narrative patches are discarded; motives must be stored with the map.");
        Assert.Equal("map", Assert.Single(response.RootElement.EnumerateObject()).Name);
        string patch = ChunkPopulationPatch.Prepare(state, new(state.Map, chunk, reply.RawResponse))!;
        Assert.NotNull(patch);
        PromptScenario.ApplyAndParse(storage, patch);
        var restored = new Storage();
        restored.ApplySavedState(JsonSerializer.Serialize(state));
        var after = restored.WorldState;
        var clerk = Assert.Single(after.Map.Entities!);
        Assert.Equal("Iven", clerk.Name);
        string memory = string.Join(" ", clerk.AiInfo ?? []);
        Assert.Contains("ORCHID-73", memory);
        Assert.Contains("brother", memory, StringComparison.OrdinalIgnoreCase);
        Assert.True(chunk.Contains(clerk.Position[0], clerk.Position[1]));
        Assert.Empty(after.Narrative!.Npcs);
        Assert.Empty(after.Narrative.PlotThreads);
        Assert.Empty(after.Narrative.CurrentArc);
        Assert.Empty(after.History);
        Assert.Equal(new[] { 3, 3 }, after.Hero!.Position);
        Assert.Null(after.Combat);

        // A saved secret is GM knowledge, not something a stranger learns by greeting its owner.
        after.Hero.Position = [clerk.Position[0] == chunk.OriginCol + 1 ? clerk.Position[0] + 1 : clerk.Position[0] - 1, clerk.Position[1]];
        var visible = Enumerable.Range(chunk.OriginCol + 1, MapChunk.Cols)
            .SelectMany(c => Enumerable.Range(1, MapChunk.Rows).Select(r => (c, r))).ToHashSet();
        var greeting = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", restored,
            "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(after, visible)
                .MinimalState("I greet the clerk: Hello, I am passing through. What are you working on today?"));
        output.WriteLine(greeting.ToolTrace);
        output.WriteLine(greeting.RawResponse);
        Assert.False(greeting.RollDiceTriggered);
        var conversation = PromptScenario.ApplyAndParse(restored, greeting.RawResponse, retainHistory: true);
        Assert.NotEmpty(conversation.HistoryTexts);
        var publicKnowledge = JsonSerializer.Serialize(new
        {
            history = conversation.HistoryTexts,
            after.Narrative.CurrentArc,
            people = after.Narrative.Npcs.Where(n => n.Met == true).Select(n => new { n.Name, n.Notes, n.Memory }),
            stories = after.Narrative.PlotThreads.Where(t => t.Hidden != true).Select(t => new { t.Name, t.Description, t.Steps })
        });
        Assert.DoesNotContain("ORCHID-73", publicKnowledge);
        Assert.Empty(after.Narrative.CurrentArc);
        Assert.DoesNotContain(after.Narrative.PlotThreads, t => t.Status == PlotThreadStatuses.Active && t.Deleted != true);
        Assert.Contains("ORCHID-73", string.Join(" ", Assert.Single(after.Map.Entities!).AiInfo ?? []));
        Assert.False(after.Combat?.Active == true);
    }
}
