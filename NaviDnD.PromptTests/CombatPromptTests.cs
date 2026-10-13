using NaviDnD.Clients;
using NaviDnD.Data.Models;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MeleeFinisher_DeathOrKnockoutChangesState(bool nonlethal)
    {
        var storage = PromptScenario.LoadUiTestFixture("opportunity_attack_game.json");
        var enemy = storage.WorldState.Map.Entities![0];
        enemy.Hp = "1/10";
        enemy.MonsterKey = "Орк";
        string request = nonlethal ? "бью орка мечом плашмя, хочу оглушить, а не убить" : "бью орка длинным мечом, чтобы убить";
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            new AiContextBuilder(storage.WorldState).MinimalState(request), rollValue: 20);
        _output.WriteLine(result.RawResponse);
        Assert.True(result.RollDiceTriggered);
        PromptScenario.ApplyAndParse(storage, result.RawResponse);
        enemy = storage.WorldState.Map.Entities![0];
        Assert.Equal(0, int.Parse(enemy.Hp.Split('/')[0]));
        if (nonlethal)
        {
            Assert.NotEqual(true, enemy.Deleted);
            Assert.Contains(enemy.Effects ?? [], e => e.Deleted != true);
            Assert.DoesNotContain(storage.WorldState.Map.Objects ?? [], o => o.Symbol == "(†)");
        }
        else
        {
            Assert.True(enemy.Deleted);
            Assert.Contains(storage.WorldState.Map.Objects ?? [], o => o.Name == L.WF("Тело: {0}", enemy.Name));
            Assert.Contains("Орк", storage.WorldState.Narrative?.KnownMonsters ?? []);
            Assert.NotEqual(true, storage.WorldState.Combat?.Active);
        }
    }

    [Fact]
    public async Task CombatStartsWithThreeHostilesWhenOnlyOneSeesHero()
    {
        var storage = PromptScenario.LoadUiTestFixture("opportunity_attack_game.json");
        storage.WorldState.Combat = null;
        var watcher = storage.WorldState.Map.Entities![0];
        watcher.Symbol = "OR1";
        watcher.VisionFt = 60;
        watcher.Triggers = new CellTriggers { Spotted = new TriggerEffect { Effect = "Три орка одной шайки нападают на героя." } };
        storage.WorldState.Map.Entities.Add(new LivingEntity { Symbol = "OR2", Name = "Орк-лучник", Hp = "10/10", Position = [5, 5], VisionFt = 5, MonsterKey = "Орк", Triggers = watcher.Triggers });
        storage.WorldState.Map.Entities.Add(new LivingEntity { Symbol = "OR3", Name = "Орк-копейщик", Hp = "10/10", Position = [5, 1], VisionFt = 5, MonsterKey = "Орк", Triggers = watcher.Triggers });
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            new AiContextBuilder(storage.WorldState).TriggerState([(watcher, "spotted")], [], []));
        _output.WriteLine(result.RawResponse);
        PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(storage.WorldState.Combat?.Active);
        foreach (string symbol in new[] { "HRO", "OR1", "OR2", "OR3" })
            Assert.Contains(storage.WorldState.Combat!.Initiative!, i => i.Symbol == symbol);
    }

}
