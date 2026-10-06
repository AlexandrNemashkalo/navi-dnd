using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E-регрессия: во время боя раунд продвигает ФИНАЛЬНАЯ ЗАПИСЬ ХОДА ВРАГОВ (totalRounds+1, см.
// SendAction systemPrompt § БОЙ), а не только SendAction вне боя (см. BurningEffectRoundTransitionTests)
// и не только движение по сетке. До фикса MapScreenLoop.RunEnemyTurnAndCheckRoundEnd движок вообще
// не проверял Storage.TakeRoundBundle() после хода врагов — эффект героя (например, «Горящий»)
// никогда бы не истёк во время боя, пока раунды продвигает исключительно вражеский ход.
//
// Fixtures/combat_effect_round_game.json: бой активен, ход гоблина (все действия/скорость героя уже
// потрачены — типичная причина, почему ход именно у врага); «Горящий» задан с expiresAtRound:1 (а не
// onRound) — при totalRounds:1 он ЕЩЁ не истёк (currentRound > expiresAtRound строго), поэтому первая
// же проверка после действия игрока (round ещё 1) не срабатывает — это принципиально, иначе эффект
// сработал бы ДО хода врага и тест ничего бы не доказал про сам фикс. Три мок-ответа "SendAction":
// 1) заглушка действия игрока, 2) ход гоблина — атакует и передаёт ход герою с totalRounds+1 (теперь
// currentRound(2) > expiresAtRound(1) — эффект истёк), 3) round-transition, вызванный ТОЛЬКО если
// фикс действительно сработал — снимает «Горящий».
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
