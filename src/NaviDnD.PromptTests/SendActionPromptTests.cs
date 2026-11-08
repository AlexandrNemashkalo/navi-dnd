using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// REAL calls Claude - paid and slow (seconds per test), so the project is NOT included in the overall run of
// the solution. Run consciously and RARELY - only when you change a specific systemPrompt.md and want to
// check/update the mock for the change, not as part of normal work: dotnet test NaviDnD.PromptTests To update
// the canonical mock for NaviDnD.UiTests from the same run: NAVIDND_RECORD_PROMPTS=1 dotnet test
// NaviDnD.PromptTests --filter OnStepTrap
public class SendActionPromptTests
{
    private readonly ITestOutputHelper _output;

    public SendActionPromptTests(ITestOutputHelper output) => _output = output;

    // The same scenario as NaviDnD.UiTests.TriggersScenarioTests.OnStepTrap_MovingOntoIt_FiresAiTrigger - the
    // hero steps on the “Trap” (triggers.onStep, once:true).
    [Fact]
    public async Task OnStepTrap_DealsDamageAndRemovesTrap()
    {
        var storage = PromptScenario.LoadUiTestFixture("trap_game.json");
        var trap = storage.WorldState.Map.Objects![0]; // "Trap" is the only object in the fixture
        int initialHp = ParseHp(storage.WorldState.Hero!.Hp);

        var ctx = new AiContextBuilder(storage.WorldState);
        string userMessage = ctx.TriggerState([(trap, "onStep")], [], []);

        string response = await PromptScenario.CallRealAiAsync("Dnd5e", "SendAction", storage, userMessage);
        _output.WriteLine("Ответ модели:\n" + response);

        var result = PromptScenario.ApplyAndParse(storage, response);

        Assert.True(result.HistoryTexts.Count > 0, "Ответ не содержит ни одной записи history.");
        Assert.Contains(result.HistoryTexts, t => t.Contains('[') && t.Contains(']'));

        int finalHp = ParseHp(storage.WorldState.Hero!.Hp);
        Assert.True(finalHp < initialHp, $"HP героя должно было уменьшиться от ловушки: было {initialHp}, стало {finalHp}.");

        Assert.DoesNotContain(storage.WorldState.Map.Objects ?? [], o => o.Name == "Капкан" && o.Deleted != true);

        PromptScenario.RecordAsMock(response, mockResponsesFolder: "Triggers", action: "SendAction");
    }

    // Same scenario as NaviDnD.UiTests.TriggersScenarioTests.HiddenObjectOnVisible... - the hero approaches
    // within visibility distance of a hidden object (triggers.onVisible, once:true).
    [Fact]
    public async Task HiddenObjectOnVisible_RevealsTheObject()
    {
        var storage = PromptScenario.LoadUiTestFixture("hidden_object_game.json");
        var hiddenObject = storage.WorldState.Map.Objects![0]; // "Secret Symbol"

        var ctx = new AiContextBuilder(storage.WorldState);
        string userMessage = ctx.TriggerState([(hiddenObject, "onVisible")], [], []);

        string response = await PromptScenario.CallRealAiAsync("Dnd5e", "SendAction", storage, userMessage);
        _output.WriteLine("Ответ модели:\n" + response);

        var result = PromptScenario.ApplyAndParse(storage, response);

        Assert.True(result.HistoryTexts.Count > 0, "Ответ не содержит ни одной записи history — обнаружение скрытого объекта должно быть описано.");

        // The object is either marked visible (hidden:false)), or removed from the list entirely - both options
        // are valid according to the prompt ("hidden: remove - {id,hidden:false}"), as long as the object no
        // longer remains Hidden:true (otherwise the trigger would not actually work from the player's point of
        // view).
        var stillHidden = storage.WorldState.Map.Objects?.Any(o => o.Name == "Тайный символ" && o.Hidden == true) ?? false;
        Assert.False(stillHidden, "Скрытый объект должен был раскрыться (hidden:false) или быть удалён — но остался Hidden:true.");

        PromptScenario.RecordAsMock(response, mockResponsesFolder: "HiddenTrigger", action: "SendAction");
    }

    private static int ParseHp(string hp) => int.Parse(hp.Split('/')[0]);
}
