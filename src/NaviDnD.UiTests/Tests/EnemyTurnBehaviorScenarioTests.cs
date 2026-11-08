using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: automatic move of the enemy (MapScreenLoop.RunEnemyTurn → GameAiClient.FireEnemyTurn) - after ANY free
// action of the player, the engine itself calls the move of the one whose CombatState.CurrentTurn is currently
// active, if this is a creature (not a hero). FireEnemyTurn uses the same system prompt
// (Prompts/Dnd5e/SendAction), as the usual SendAction - that is, both calls within the same test are
// intercepted by ONE mock action name "SendAction". We distinguish them through numbered response files
// SelectiveMockProvider (response.json - 1st call, response2.json - 2nd) - this gives REALLY DIFFERENT, not
// duplicated content for the "player action" and for the "automatic move of the enemy response2.json" in both
// folders. (EnemyAttack/EnemyMove) is written strictly according to the format from
// Prompts/Dnd5e/SendAction/systemPrompt.md § BATTLE "Structure history of the enemy's turn": the patch is NOT
// the root key, but is embedded in history[].patch, movement is through movePath, and the final record of the
// enemies' move must transfer the move back to the hero (combat.currentTurn=hero, totalRounds+1, hero.actions/
// speedLeft are reset) - self-test of the prompt, point 10. Fixtures/enemy_turn_game.json: hero at [1,3] with
// ALREADY spent actions/speed (this is the reason why it is now the goblin's move "GBL" at [5,3]),
// Combat.Active=true, CurrentTurn="GBL".
public class EnemyTurnBehaviorScenarioTests
{
    private readonly ITestOutputHelper _output;

    public EnemyTurnBehaviorScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EnemyAttackBehavior_AppliesDamageToHero_AndHandsTurnBackToHero()
    {
        using var game = GameSession.Launch(
            fixtureFileName: "enemy_turn_game.json", mockedActions: ["SendAction"], mockResponsesFolder: "EnemyAttack");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");
        var failures = new List<string>();

        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000),
            "Не дождались экрана Карты после [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован. Отправляю действие игрока (1й вызов мока — заглушка без патча)...");
        TestPacing.Step();

        GameConsole.SendText(game.Pid, "жду своего хода");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Ожидание", 3000),
            "Не дождались спиннера ожидания для действия игрока.");
        _output.WriteLine("Действие игрока обрабатывается. После него движок должен автоматически вызвать ход гоблина (2й вызов мока)...");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Атака: d20", 20000),
            "Не дождались хода гоблина (2й вызов мока — атака) — автоматический вызов FireEnemyTurn не произошёл.");
        _output.WriteLine("Гоблин атаковал. Жду передачу хода обратно герою (финальная запись хода врагов)...");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Раунд 2", 5000),
            "После хода врага раунд не продвинулся до «Раунд 2» — финальная запись хода не передала ход обратно герою.");
        TestPacing.Step();

        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);

        _output.WriteLine("Проверяю урон герою, возврат хода герою и сброс скорости/действий...");
        if (!rows.Any(r => r.Contains("❤ 6/10")))
            failures.Add("После хода врага HP героя не обновилось до «❤ 6/10» — FireEnemyTurn не применил патч атаки.");

        var initiativeRow = rows.FirstOrDefault(r => r.Contains("Порядок боя"));
        if (initiativeRow == null || !initiativeRow.Contains("⚔️HRO"))
            failures.Add($"После хода врага ход должен вернуться герою («⚔️HRO»): «{initiativeRow?.TrimEnd()}»");

        // The speed is reset to the full maximum (30/30) - not a single cell is “wasted”, so here it is enough
        // to check the text (CheckMovementSquares also checks the color of the SPENT cell, and at 30/30 there
        // are simply no such cells - there would be nothing to sample).
        var moveRow = rows.FirstOrDefault(r => r.Contains("Движение:"));
        if (moveRow == null || !moveRow.Contains("30/30"))
            failures.Add($"После хода врага скорость должна быть сброшена до 30/30: «{moveRow?.TrimEnd()}»");

        CombatStateScenarioTests.CheckActionSymbol(game.Pid, rows, "Действие", '●', (70, 200, 70), spent: false, failures);
        CombatStateScenarioTests.CheckActionSymbol(game.Pid, rows, "Бон. действие", '■', (160, 100, 50), spent: false, failures);
        CombatStateScenarioTests.CheckActionSymbol(game.Pid, rows, "Реакция", '▲', (220, 70, 70), spent: false, failures);
        TestPacing.Step();

        _output.WriteLine(failures.Count == 0
            ? "Все проверки пройдены."
            : $"Провалено проверок: {failures.Count}.");
        TestPacing.Step();
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }

    [Fact]
    public void EnemyMoveBehavior_ChangesEnemyPositionOnGrid_AndHandsTurnBackToHero()
    {
        using var game = GameSession.Launch(
            fixtureFileName: "enemy_turn_game.json", mockedActions: ["SendAction"], mockResponsesFolder: "EnemyMove");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");
        var failures = new List<string>();

        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000),
            "Не дождались экрана Карты после [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован. Запоминаю исходную колонку гоблина на сетке...");
        // Legend with active battle is higher than usual (+7 lines CombatReservedLines) - the console window
        // needs a moment to finish drawing the bottom after the initial drawing of the title (see the same race
        // class in CombatStateScenarioTests). Waiting for “Time:” - drawn by the last line of the block.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Время:", 5000),
            "Строка «Время:» так и не появилась в легенде.");
        TestPacing.Step();

        var initialRows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        int initialCol = FindGoblinGridColumn(initialRows);
        Assert.True(initialCol >= 0, "Символ гоблина «GBL» не найден на сетке (в области карты, а не в легенде) в исходном состоянии.");

        _output.WriteLine("Отправляю действие игрока (1й вызов мока — заглушка без патча)...");
        GameConsole.SendText(game.Pid, "жду своего хода");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Ожидание", 3000),
            "Не дождались спиннера ожидания для действия игрока.");
        _output.WriteLine("Действие игрока обрабатывается. После него движок должен автоматически подвинуть гоблина (2й вызов мока)...");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "перемещается ближе", 20000),
            "Не дождались хода гоблина (2й вызов мока — движение) — автоматический вызов FireEnemyTurn не произошёл.");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Раунд 2", 5000),
            "После хода врага раунд не продвинулся до «Раунд 2» — финальная запись хода не передала ход обратно герою.");
        TestPacing.Step();

        var finalRows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        int finalCol = FindGoblinGridColumn(finalRows);
        Assert.True(finalCol >= 0, "Символ гоблина «GBL» не найден на сетке (в области карты) после хода врага.");

        _output.WriteLine($"Колонка гоблина: было {initialCol}, стало {finalCol} (должна уменьшиться — гоблин сдвинулся ближе к герою).");
        if (finalCol >= initialCol)
            failures.Add($"Гоблин не сдвинулся на сетке после автоматического хода врага (поведение «движение»): колонка была {initialCol}, стала {finalCol}.");

        _output.WriteLine("Проверяю возврат хода герою и сброс скорости/действий...");
        var initiativeRow = finalRows.FirstOrDefault(r => r.Contains("Порядок боя"));
        if (initiativeRow == null || !initiativeRow.Contains("⚔️HRO"))
            failures.Add($"После хода врага ход должен вернуться герою («⚔️HRO»): «{initiativeRow?.TrimEnd()}»");

        // The speed is reset to the full maximum (30/30) - not a single cell is “wasted”, so here it is enough
        // to check the text (CheckMovementSquares also checks the color of the SPENT cell, and at 30/30 there
        // are simply no such cells - there would be nothing to sample).
        var moveRow = finalRows.FirstOrDefault(r => r.Contains("Движение:"));
        if (moveRow == null || !moveRow.Contains("30/30"))
            failures.Add($"После хода врага скорость должна быть сброшена до 30/30: «{moveRow?.TrimEnd()}»");

        CombatStateScenarioTests.CheckActionSymbol(game.Pid, finalRows, "Действие", '●', (70, 200, 70), spent: false, failures);
        CombatStateScenarioTests.CheckActionSymbol(game.Pid, finalRows, "Бон. действие", '■', (160, 100, 50), spent: false, failures);
        CombatStateScenarioTests.CheckActionSymbol(game.Pid, finalRows, "Реакция", '▲', (220, 70, 70), spent: false, failures);
        TestPacing.Step();

        _output.WriteLine(failures.Count == 0
            ? "Все проверки пройдены."
            : $"Провалено проверок: {failures.Count}.");
        TestPacing.Step();
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }

    // The map and legend are drawn on the SAME line side-by-side, and the legend ALSO shows the creature's
    // name/symbol in text ("GBL : Goblin - ...") - searching for "GBL" on the entire line may find the legend
    // instead of the grid itself (they are not necessarily on the same physical line, since the legend is not
    // tied to the creature's Y- coordinate). We limit the search to the left part of the line - the map area,
    // to the internal border with the legend (MapDisplay: mapWidth = cols*(cellWidth+1)+7 = 20*4+7 = 87).
    private const int MapContentWidth = 87;

    private static int FindGoblinGridColumn(List<string> rows)
    {
        foreach (var row in rows)
        {
            if (row.Length <= 1) continue;
            var gridPart = row[..Math.Min(row.Length, MapContentWidth)];
            int col = gridPart.IndexOf("GBL", StringComparison.Ordinal);
            if (col >= 0) return col;
        }
        return -1;
    }
}
