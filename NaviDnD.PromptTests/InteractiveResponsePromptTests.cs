using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// РЕАЛЬНЫЕ вызовы Claude — см. класс SendActionPromptTests для общих правил (платно, медленно,
// запускать редко и осознанно). Проверяет, что промпт реально ВЫЗЫВАЕТ нужный MCP-инструмент
// (roll_dice / ask_player) для сценариев, где это требуется — без автоответчика
// (PromptScenario.CallRealAiWithAutoAnswersAsync) вызов завис бы на реальный таймаут инструмента (50с).
public class InteractiveResponsePromptTests
{
    private readonly ITestOutputHelper _output;

    public InteractiveResponsePromptTests(ITestOutputHelper output) => _output = output;

    // Атака мечом по видимому противнику в бою — должна вызвать roll_dice (см. промпт: "roll_dice —
    // все d20-броски героя (... атака ...)"). Сценарий — тот же, что и OpportunityAttack (герой и
    // орк смежны, ход героя, полные ресурсы).
    [Fact]
    public async Task AttackAction_TriggersRollDice()
    {
        var storage = PromptScenario.LoadUiTestFixture("opportunity_attack_game.json");
        var actionResource = storage.WorldState.Hero!.Actions!.First(a => a.Name == "Действие");
        Assert.Equal(1, actionResource.Value); // предусловие: Действие ещё не потрачено

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
        Assert.Equal(0, actionAfter.Value); // "Действие" потрачено на атаку

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "DiceRoll", action: "SendAction");
    }

    // Два одинаковых врага рядом с героем + неоднозначная команда "атакую врага" — промпт обязан
    // дать выбрать цель мышью на карте (select_target, см. § MCP), а не спрашивать текстом.
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

        // Ответ после уточнения цели должен быть структурно валиден (не проверяем конкретную цель —
        // модель сама решает и могла добавить дополнительный контекст).
        PromptScenario.ApplyAndParse(storage, result.RawResponse);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "AskPlayer", action: "SendAction");
    }
}
