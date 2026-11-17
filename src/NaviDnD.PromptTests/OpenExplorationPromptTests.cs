using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class OpenExplorationPromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task UnscriptedWatchPostOffersObservableTracesWithoutChoosingTheHerosGoal()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        var chunk = new MapChunk { X = 1, Theme = "building",
            Purpose = "An abandoned watch post on a disused trade road. A small local mystery emerges from material traces of the ordinary people who once worked here. No predetermined quest or solution has been supplied." };
        state.World = null;
        state.Map = new() { Cols = 40, Rows = 15, Chunks = [new() { Populated = true },chunk],
            Rooms = [new() { Name = "Watch post", Positions = Enumerable.Range(22,7)
                .SelectMany(c => Enumerable.Range(3,5).Select(r => new List<int> {c,r})).ToList() }],
            Doors = [new() { From = [22,4], To = [21,4], IsDoor = true, IsDoorOpen = true }],
            Furniture = [new() { Kind = "table", Name = "Old desk", Positions = [[24,6]] },
                new() { Kind = "bench", Name = "Waiting bench", Positions = [[27,6]] }], Objects = [], Entities = [] };
        state.Narrative = new() { World = "A grounded frontier where human-scale mysteries grow from travel, work and relationships.",
            Style = "Quiet exploration and curiosity; low magic, no forced combat or cosmic conspiracy." };
        state.Hero!.Position = [3,3];
        state.Hero.Inventory = [];
        state.History = [];
        state.Combat = null;
        var context = new AiContextBuilder(state);
        var population = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "PopulateChunk", storage,
            "lang:en\nspeechEnabled:false\n" + context.PopulationState(chunk) + "\n" + context.LocationPlan(chunk) + "\n" + context.ChunkState(chunk));
        output.WriteLine("GENERATED PLACE: " + population.RawResponse);
        string patch = ChunkPopulationPatch.Prepare(state,new(state.Map,chunk,population.RawResponse))!;
        PromptScenario.ApplyAndParse(storage,patch);
        chunk.Populated = true;
        Assert.Empty(state.History);
        var restored = new Storage();
        restored.ApplySavedState(JsonSerializer.Serialize(state));
        var after = restored.WorldState;
        after.Hero!.Position = [22,4];
        var visible = Enumerable.Range(21,9).SelectMany(c => Enumerable.Range(2,7).Select(r => (c,r))).ToHashSet();
        var arrival = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", restored,
            "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(after,visible)
                .MinimalState("I step inside the old watch post and look around quietly. What can I see?"));
        output.WriteLine("FIRST LOOK: " + arrival.RawResponse);
        var narration = PromptScenario.ApplyAndParse(restored,arrival.RawResponse,retainHistory:true);
        Assert.NotEmpty(narration.HistoryTexts);
        Assert.True((after.Map.Objects ?? []).Any(o => o.Deleted != true && o.Hidden != true && o.AiInfo?.Count > 0)
            || (after.Map.Furniture ?? []).Any(f => f.Deleted != true && f.AiInfo?.Count > 0),
            "The place needs a concrete inspectable trace in its persistent map, not only an introductory story.");
        Assert.Empty(after.Narrative!.CurrentArc);
        Assert.DoesNotContain(after.Narrative.PlotThreads,t => t.Deleted != true && t.Status == PlotThreadStatuses.Active);
        Assert.Empty(after.Hero.Inventory);
        Assert.False(after.Combat?.Active == true);

        var targetObject = after.Map.Objects?.FirstOrDefault(o => o.Deleted != true && o.Hidden != true && o.AiInfo?.Count > 0);
        var targetFurniture = targetObject == null ? after.Map.Furniture!.First(f => f.Deleted != true && f.AiInfo?.Count > 0) : null;
        string targetName = targetObject?.Name ?? FurnitureCatalog.DisplayName(targetFurniture!);
        string? targetSymbol = targetObject?.Symbol;
        int furnitureId = targetFurniture == null ? -1 : after.Map.Furniture!.IndexOf(targetFurniture);
        var establishedFacts = (targetObject?.AiInfo ?? targetFurniture!.AiInfo!).ToList();
        after.History = [];
        var resumed = new Storage();
        resumed.ApplySavedState(JsonSerializer.Serialize(after));
        var inspection = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", resumed,
            "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(resumed.WorldState,visible)
                .MinimalState($"I walk up to the {targetName} and examine it carefully, reading any clearly visible writing. I leave it in place and do not damage it."));
        output.WriteLine("INSPECTION: " + inspection.RawResponse);
        var inspected = PromptScenario.ApplyAndParse(resumed,inspection.RawResponse,retainHistory:true);
        Assert.NotEmpty(inspected.HistoryTexts);
        var result = resumed.WorldState;
        var survivingFacts = targetObject != null
            ? Assert.Single(result.Map.Objects!,o => o.Symbol == targetSymbol && o.Deleted != true).AiInfo
            : result.Map.Furniture![furnitureId].AiInfo;
        Assert.All(establishedFacts,fact => Assert.Contains(fact,survivingFacts!));
        Assert.Empty(result.Hero!.Inventory);
        Assert.Empty(result.Narrative!.CurrentArc);
        Assert.DoesNotContain(result.Narrative.PlotThreads,t => t.Deleted != true && t.Status == PlotThreadStatuses.Active);
        Assert.False(result.Combat?.Active == true);
        Assert.Contains(result.Narrative.PlotThreads,t => t.Deleted != true && t.Hidden != true
            && t.Status == PlotThreadStatuses.Revealed && !string.IsNullOrWhiteSpace(t.Description));
        output.WriteLine("KNOWN STORIES: " + JsonSerializer.Serialize(result.Narrative.PlotThreads));
    }
}
