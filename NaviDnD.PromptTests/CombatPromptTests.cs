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

        Assert.Contains("calculate_movement →", result.ToolTrace);
        Assert.NotEqual(new List<int> { 5, 3 }, storage.WorldState.Map.Entities![0].Position);
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

    [Fact]
    public async Task ShieldReactionConsumesSlotAndReaction()
    {
        var storage = PromptScenario.LoadUiTestFixture("opportunity_attack_game.json");
        var hero = storage.WorldState.Hero!;
        hero.Spells = [new() { Name = "Щит", Level = 1, Prepared = true }];
        hero.Resources = [new() { Name = "Ячейки 1 круга", Value = "2/2", Category = ResourceCategories.Spells }];
        hero.Stats.Add(new() { Name = "Класс", Key = StatKeys.Class, Value = "Волшебник" });
        hero.Abilities = [new() { Name = "Щит", Description = "При попадании атаки реакцией потрать ячейку и повысь КД на 5 до начала следующего хода." }];
        var orc = storage.WorldState.Map.Entities![0];
        orc.Triggers = new CellTriggers { OnRound = new TriggerEffect { Effect = "Орк атакует героя секирой: итог атаки 17 против КД 15, урон 5 рубящий; предложи доступную реакцию Щит до урона." } };
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            new AiContextBuilder(storage.WorldState).TriggerState([(orc, "onRound")], [], []), askPlayerAnswer: "Щит");
        _output.WriteLine(result.RawResponse);
        PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.Contains("use_reaction →", result.ToolTrace);
        Assert.Equal("1/2", storage.WorldState.Hero!.Resources[0].Value);
        Assert.Equal(0, storage.WorldState.Hero.Actions![2].Value);
        Assert.Equal("10/10", storage.WorldState.Hero.Hp);
        Assert.Contains(storage.WorldState.Hero.Effects ?? [], e => e.Deleted != true && e.Name.Contains("Щит"));
    }

    [Fact]
    public async Task LivingEnemyFleesWithoutLeavingBody()
    {
        var storage = PromptScenario.LoadUiTestFixture("opportunity_attack_game.json");
        var enemy = storage.WorldState.Map.Entities![0];
        enemy.MonsterKey = "Орк";
        enemy.Triggers = new CellTriggers { OnRound = new TriggerEffect { Effect = "Орк сдаётся, герой отпускает его. Орк живым уходит из локации, без атаки по возможности; убери его с карты и закончи бой." } };
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            new AiContextBuilder(storage.WorldState).TriggerState([(enemy, "onRound")], [], []));
        _output.WriteLine(result.RawResponse);
        PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(storage.WorldState.Map.Entities![0].Deleted);
        Assert.True(int.Parse(storage.WorldState.Map.Entities[0].Hp.Split('/')[0]) > 0);
        Assert.DoesNotContain(storage.WorldState.Map.Objects ?? [], o => o.Symbol == "(†)");
        Assert.NotEqual(true, storage.WorldState.Combat?.Active);
    }

    [Fact]
    public async Task AttackingEndsInvisibility()
    {
        var storage = PromptScenario.LoadUiTestFixture("opportunity_attack_game.json");
        storage.WorldState.Hero!.Effects = [new() { Name = "Невидимость", Description = "Заклинание Невидимость: до часа или до атаки/заклинания", ExpiresAtRound = storage.WorldState.Time.TotalRounds + 600 }];
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            new AiContextBuilder(storage.WorldState).MinimalState("атакую орка длинным мечом"), rollValue: 20);
        _output.WriteLine(result.RawResponse);
        PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(result.RollDiceTriggered);
        Assert.DoesNotContain(storage.WorldState.Hero.Effects ?? [], e => e.Deleted != true && e.Name == "Невидимость");
    }

}
