using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E- regression: during combat, the round advances the FINAL RECORDING OF ENEMIES' MOVE (totalRounds+1, see
// SendAction systemPrompt § COMBAT), and not just SendAction outside of combat (see
// BurningEffectRoundTransitionTests) and not only movement on the grid. Before the
// MapScreenLoop.RunEnemyTurnAndCheckRoundEnd fix, the engine did not check at all Storage.TakeRoundBundle()
// after the enemies' turn - the hero's effect (for example, "Burning") would never expire during the battle,
// while the rounds are promoted exclusively by the enemy's turn. Fixtures/combat_effect_round_game.json: the
// battle is active, the goblin's turn (all actions / speed of the hero have already been spent - a typical
// reason why the enemy's move is set with expiresAtRound:1 (and not onRound) - with totalRounds:1); It hasn’t
// expired YET (currentRound > expiresAtRound strictly), so the first check after the player’s action (round 1
// more) does not work - this is important, otherwise the effect would have worked BEFORE the enemy’s move and
// the test would not have proved anything about the fix itself. Three mock answers "SendAction": 1) stub the
// player's action, 2) the goblin's move - attacks and. transfers the turn to the hero with totalRounds+1 (now
// currentRound(2) > expiresAtRound(1) - the effect has expired), 3) round-transition, called ONLY if the fix
// actually worked - removes “Burning”.
public class CombatEffectRoundScenarioTests
{
    private readonly ITestOutputHelper _output;

    public CombatEffectRoundScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EnemyTurnEndingRound_TriggersHeroOnRoundEffect_DuringCombat()
    {
        using var game = GameSession.Launch(
            fixtureFileName: "combat_effect_round_game.json", mockedActions: ["SendAction"], mockResponsesFolder: "CombatEffectRound");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");
        var failures = new List<string>();

        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000),
            "Не дождались экрана Карты после [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован. Отправляю действие игрока (1й вызов мока — заглушка, ход у гоблина)...");
        TestPacing.Step();

        GameConsole.SendText(game.Pid, "жду своего хода");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Ожидание", 3000),
            "Не дождались спиннера ожидания для действия игрока.");
        _output.WriteLine("Действие обрабатывается. Далее движок сам вызывает ход гоблина (2й вызов мока)...");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Раунд 2", 25000),
            "Раунд не продвинулся до 2 — финальная запись хода врагов (2й вызов мока) не применилась.");
        _output.WriteLine("Ход гоблина завершён, раунд продвинулся до 2. Ждём тик «Горящего» (3й вызов мока)...");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "наконец гаснет", 25000),
            "Не дождались текста 3го мок-ответа (истечение «Горящего» во время боя) — MapScreenLoop.RunEnemyTurnAndCheckRoundEnd не сработал.");
        TestPacing.Step();

        _output.WriteLine("Проверяю итоговое HP героя (9 от атаки гоблина, затем 8 от тика «Горящего»)...");
        if (!GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "❤ 8/10", 5000))
            failures.Add("После атаки гоблина и тика «Горящего» HP героя не обновилось до «❤ 8/10».");

        _output.WriteLine(failures.Count == 0 ? "Все проверки пройдены." : $"Провалено проверок: {failures.Count}.");
        TestPacing.Step();
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }
}
