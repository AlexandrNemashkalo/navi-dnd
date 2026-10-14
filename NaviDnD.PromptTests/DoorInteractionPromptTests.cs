using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// REAL calls Claude - see class SendActionPromptTests for general rules (paid, slow, run rarely and
// deliberately). Checks the fix for a real bug from the production log: the AI described “I come up and open
// the door,” although the hero did not move and was two cells from the door - the engine did not check this in
// any way, the prompt relied on the fact that the AI itself would not come up with a non-existent approach. Now
// § MAP AND OBJECTS teaches you to call open_door(id)/close_door(id) - the tool itself measures the distance
// (Chebyshev ≤1 to from/to) and either returns a patch or the reason for the failure.
public class DoorInteractionPromptTests
{
    private readonly ITestOutputHelper _output;

    public DoorInteractionPromptTests(ITestOutputHelper output) => _output = output;

    // The hero is at [1,1], the door between [3,3]/[4,3] is far away (Chebyshev 2/3). open_door should refuse,
    // and the AI should not resolve the discovery itself on top of the tool’s failure.
    [Fact]
    public async Task OpenDoor_WhenHeroIsFar_DoesNotFabricateSuccess()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var doorBefore = storage.WorldState.Map.Doors!.Single();
        Assert.False(doorBefore.IsDoorOpen, "Предусловие: дверь изначально закрыта.");

        var ctx = new AiContextBuilder(storage.WorldState);
        string message = ctx.MinimalState("подхожу и открываю дверь");
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, message);
        _output.WriteLine("Ответ модели (дверь далеко):\n" + result.RawResponse);

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Отказ/уточнение должно быть описано в history.");

        var doorAfter = storage.WorldState.Map.Doors!.Single();
        Assert.NotEqual(true, doorAfter.IsDoorOpen);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "DoorTooFar", action: "SendAction");
    }

    // The hero is standing close to the door (id:0, from:[3,3]) - open_door must return the patch, the AI
    // must apply it.
    [Fact]
    public async Task OpenDoor_WhenHeroIsAdjacent_OpensIt()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        storage.ApplyUpdateWorldState("""{"hero":{"position":[3,3]}}""");

        var ctx = new AiContextBuilder(storage.WorldState);
        string message = ctx.MinimalState("открываю дверь передо мной");
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, message);
        _output.WriteLine("Ответ модели (дверь рядом):\n" + result.RawResponse);

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Открытие двери должно быть описано в history.");

        var doorAfter = storage.WorldState.Map.Doors!.Single();
        Assert.True(doorAfter.IsDoorOpen, "Дверь рядом с героем должна быть открыта.");

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "DoorAdjacent", action: "SendAction");
    }
}
