using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Helpers;

namespace NaviDnD.PromptTests;

// No model calls: regression coverage for the real-scenario harness.
public class PromptScenarioTests
{
    [Fact]
    public void FinalResponseResolvesCatalogReferencesBeforeApplyingNestedPatches()
    {
        var storage = new Storage();
        storage.ApplySavedState("""{"hero":{"symbol":"HRO","hp":"10/10"}}""");
        PromptScenario.ApplyAndParse(storage, """
            {"history":[{"text":"Found a sword","patch":{"hero":{"inventory":[{"name":"Temporary name","ref":"Longsword","quantity":1}]}}}],"map":{"entities":[{"name":"Raven","symbol":"RAV","position":[2,2],"monster":"Raven"}]}}
            """);
        var item = Assert.Single(storage.WorldState.Hero!.Inventory!);
        Assert.Equal(EquipmentDatabase.Find("Longsword")!.LocalizedName, item.Name);
        var creature = Assert.Single(storage.WorldState.Map.Entities!);
        Assert.False(string.IsNullOrWhiteSpace(creature.MonsterKey));
        Assert.False(string.IsNullOrWhiteSpace(creature.Hp));
    }

    [Fact]
    public void RetainedFinalHistoryIsAvailableNextTurnWithoutReapplyingItsPatch()
    {
        var storage = new Storage();
        storage.ApplySavedState("""{"hero":{"symbol":"HRO","hp":"10/10"}}""");
        PromptScenario.ApplyAndParse(storage, """
            {"history":[{"author":"Mira","text":"<speak>I repair the ferry.</speak>","patch":{"narrative":{"npcs":[{"name":"Mira","memory":["Mira repairs the ferry"]}]}}}]}
            """, retainHistory: true);
        var message = Assert.Single(storage.WorldState.History!);
        Assert.Equal("Mira", message.Author);
        Assert.Equal("I repair the ferry.", message.Text);
        Assert.Single(storage.WorldState.Narrative!.Npcs);
        Assert.Contains("I repair the ferry.", new AiContextBuilder(storage.WorldState).MinimalState("Continue"));
    }

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
