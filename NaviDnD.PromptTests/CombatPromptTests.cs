using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// РЕАЛЬНЫЕ вызовы Claude — см. класс SendActionPromptTests для общих правил (платно, медленно,
// запускать редко и осознанно). Сценарии боя проверяют § БОЙ из systemPrompt.md, в частности
// пункт 10 самопроверки промпта: финальная запись хода врагов ОБЯЗАНА вернуть ход герою,
// продвинуть раунд и сбросить действия/скорость — если промпт это перестанет делать, тест упадёт.
public class CombatPromptTests
{
    private readonly ITestOutputHelper _output;

    public CombatPromptTests(ITestOutputHelper output) => _output = output;

    // Тот же сценарий, что и NaviDnD.UiTests.OpportunityAttackScenarioTests — герой отступает из
    // клетки, смежной с орком, во время боя → триггер opportunity_attack_on_hero (существо атакует
    // героя само, без ask_player — "это не выбор героя", см. промпт).
    [Fact]
    public async Task OpportunityAttackOnHero_ResolvesWithoutAskingPlayer()
    {
        var storage = PromptScenario.LoadUiTestFixture("opportunity_attack_game.json");
        var orc = storage.WorldState.Map.Entities![0]; // "Орк"

        var ctx = new AiContextBuilder(storage.WorldState);
        string userMessage = ctx.TriggerState([(orc, "opportunity_attack_on_hero")], [], []);

        // Автоответчик — подстраховка: промпт явно запрещает ask_player для этого триггера, но если
        // модель всё же его вызовет, тест не должен зависнуть на реальный таймаут MCP (50с).
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, userMessage);
        _output.WriteLine("Ответ модели:\n" + result.RawResponse);

        Assert.False(result.AskPlayerTriggered, "Промпт явно запрещает ask_player для opportunity_attack_on_hero — это не выбор героя.");

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Атака по возможности должна быть описана в history.");
        Assert.Contains(parsed.HistoryTexts, t => t.Contains('[') && t.Contains(']'));

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "OpportunityAttack", action: "SendAction");
    }

    // Тот же сценарий, что и NaviDnD.UiTests.EnemyTurnBehaviorScenarioTests — ход гоблина
    // (CombatTurnState). Проверяет пункт 10 самопроверки промпта: финальная запись ОБЯЗАНА вернуть
    // ход герою, продвинуть раунд и сбросить действия/скорость героя на максимум.
    [Fact]
    public async Task EnemyTurn_FinalEntryHandsControlBackToHero()
    {
        var storage = PromptScenario.LoadUiTestFixture("enemy_turn_game.json");
        int initialRound = storage.WorldState.Time.TotalRounds;
        string heroSymbol = storage.WorldState.Hero!.Symbol!;

        var ctx = new AiContextBuilder(storage.WorldState);
        string userMessage = ctx.CombatTurnState();

        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, userMessage);
        _output.WriteLine("Ответ модели:\n" + result.RawResponse);

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Ход врага должен быть описан в history.");

        Assert.Equal(heroSymbol, storage.WorldState.Combat?.CurrentTurn);
        Assert.True(storage.WorldState.Time.TotalRounds > initialRound,
            $"Раунд должен был продвинуться: было {initialRound}, стало {storage.WorldState.Time.TotalRounds}.");

        var actions = storage.WorldState.Hero!.Actions ?? [];
        Assert.True(actions.Count > 0, "У героя должны остаться действия (Действие/Бон.действие/Реакция).");
        Assert.All(actions.Where(a => a.Deleted != true), a =>
            Assert.True(a.Value == a.MaxValue, $"Действие «{a.Name}» должно быть сброшено на максимум: value={a.Value}, maxValue={a.MaxValue}."));
        Assert.Equal(storage.WorldState.Hero.SpeedMax, storage.WorldState.Hero.SpeedLeft);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "EnemyAttack", action: "SendAction");
    }
}
