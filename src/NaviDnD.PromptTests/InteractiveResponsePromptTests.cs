using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// REAL calls Claude - see class SendActionPromptTests for general rules (paid, slow, run rarely and
// deliberately). Checks that the prompt actually CALLS the required MCP- tool (roll_dice / ask_player) for
// scenarios where this is required - without an answering machine
// (PromptScenario.CallRealAiWithAutoAnswersAsync) the call would hang on the real tool timeout (50s).
public class InteractiveResponsePromptTests
{
    private readonly ITestOutputHelper _output;

    public InteractiveResponsePromptTests(ITestOutputHelper output) => _output = output;

    // Attack with a sword on a visible enemy in battle - should cause roll_dice (see prompt: "roll_dice - all
    // d20- throws of the hero (... attack ...)"). The scenario is the same as OpportunityAttack (hero and orc
    // are adjacent, hero's turn, full resources).
    [Fact]
    public async Task AttackAction_TriggersRollDice()
    {
        var storage = PromptScenario.LoadUiTestFixture("opportunity_attack_game.json");
        var actionResource = storage.WorldState.Hero!.Actions!.First(a => a.Name == "Действие");
        Assert.Equal(1, actionResource.Value); // precondition: Action not yet spent

        var ctx = new AiContextBuilder(storage.WorldState);
        string userMessage = ctx.MinimalState("атакую орка мечом");

        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync(
            "Dnd5e", "SendAction", storage, userMessage, rollValue: 15);
        _output.WriteLine("Ответ модели:\n" + result.RawResponse);

        Assert.True(result.RollDiceTriggered, "Атака по видимому противнику должна была вызвать roll_dice.");

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Результат атаки должен быть описан в history.");
        Assert.Contains(parsed.HistoryTexts, t => t.Contains('[') && t.Contains(']'));

        var actionAfter = storage.WorldState.Hero!.Actions!.First(a => a.Name == "Действие");
        Assert.Equal(0, actionAfter.Value); // "Action" spent on attack

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "DiceRoll", action: "SendAction");
    }

    // Two identical enemies next to the hero + an ambiguous command “attack the enemy” - the prompt must allow
    // you to select a target with the mouse on the map (select_target, see § MCP), and not ask in text.
    [Fact]
    public async Task AmbiguousTarget_TriggersSelectTarget()
    {
        var storage = PromptScenario.LoadLocalFixture("ambiguous_target_game.json");

        var ctx = new AiContextBuilder(storage.WorldState);
        string userMessage = ctx.MinimalState("атакую врага мечом");

        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync(
            "Dnd5e", "SendAction", storage, userMessage, rollValue: 15);
        _output.WriteLine("Ответ модели:\n" + result.RawResponse);

        Assert.True(result.SelectTargetTriggered,
            "Неоднозначная цель (два одинаковых врага рядом) должна была вызвать select_target.");

        // The answer after specifying the goal must be structurally valid (we are not checking a specific goal
        // - the model decides itself and could add additional context).
        PromptScenario.ApplyAndParse(storage, result.RawResponse);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "AskPlayer", action: "SendAction");
    }
}
