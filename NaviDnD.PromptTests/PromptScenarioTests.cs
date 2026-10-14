using System.Text.Json;
using NaviDnD.Clients;

namespace NaviDnD.PromptTests;

// No model calls: regression coverage for the real-scenario harness.
public class PromptScenarioTests
{
    [Fact]
    public void SelfCenteredCubeCanChooseHeroCell()
    {
        var storage=new Storage();
        storage.ApplySavedState("""{"hero":{"symbol":"HRO","position":[3,3]}}""");
        var chosen=PromptScenario.ChooseTarget(storage.WorldState,new(){Mode="point",Shape="cube",SizeFt=15,RangeFt=0},(4,3));
        Assert.Equal((3,3),chosen);
    }

    [Fact]
    public void AreaSelectionAnswerIncludesAffectedCreaturesAndCells()
    {
        var storage = new Storage();
        storage.ApplySavedState("""{"hero":{"symbol":"HRO","position":[2,2]},"map":{"entities":[{"name":"Ally","symbol":"ALY","position":[3,2]}]}}""");
        var result = PromptScenario.BuildTargetResult(storage.WorldState, new TargetSelectionRequest { Shape="sphere",SizeFt=5 },(3,2));
        var target = Assert.Single(result.Targets);
        Assert.Contains(target.InArea!,e=>e.Symbol=="HRO");
        Assert.Contains(target.InArea!,e=>e.Symbol=="ALY");
        Assert.NotEmpty(target.AreaCells!);
    }

    [Fact]
    public void StreamedToolHistoryAppliesPatchesInOrder()
    {
        var storage = new Storage();
        storage.ApplySavedState("""{"hero":{"symbol":"HRO","hp":"10/10"}}""");
        PromptScenario.ApplyToolHistory(storage,
        [new() { Text = "Первый удар", Patch = JsonDocument.Parse("""{"hero":{"hp":"7/10"}}""").RootElement },
         new() { Text = "Второй удар", Patch = JsonDocument.Parse("""{"hero":{"hp":"5/10"}}""").RootElement }]);
        Assert.Equal("5/10", storage.WorldState.Hero!.Hp);
    }
    [Fact]
    public void StreamedHistoryIsAvailableToSubsequentWorldStateQueries()
    {
        var storage = new Storage();
        storage.ApplySavedState("""{"hero":{"symbol":"HRO","hp":"10/10"}}""");
        PromptScenario.ApplyToolHistory(storage,
        [new() { Text = "<speak>Орк атакует.</speak> [17 vs КД15]", Author = "DM" }]);
        var message = Assert.Single(storage.WorldState.History);
        Assert.Equal("DM", message.Author);
        Assert.Equal("Орк атакует. [17 vs КД15]", message.Text);
    }

}
