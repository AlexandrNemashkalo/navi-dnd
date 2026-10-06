using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: вне боя свободное движение, которое превышает оставшуюся скорость, автоматически
// продвигает раунд и сбрасывает скорость/действия героя (Storage.ApplyClientMovement /
// ResetActionsForNewRound) — пока не проверялось ни одним UI-тестом, только то, что легенда вообще
// умеет рисовать раунд/действия (см. CombatStateScenarioTests, там раунд статичен).
//
// Fixtures/round_advance_game.json: speedMax=speedLeft=10фт (2 клетки), все действия уже
// потрачены (value:0). Шаг 1 и 2 (по 5фт) укладываются ровно в остаток скорости — раунд не
// меняется. Шаг 3 требует ещё 5фт при остатке 0 — это и есть момент, когда раунд продвигается,
// скорость и действия сбрасываются на новый максимум.
public class RoundAndActionEconomyScenarioTests
{
    private readonly ITestOutputHelper _output;

    public RoundAndActionEconomyScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ExhaustingSpeedOutsideCombat_AdvancesRoundAndResetsActions()
    {
        using var game = GameSession.Launch(fixtureFileName: "round_advance_game.json");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000),
            "Не дождались экрана Карты после [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован. Проверяю исходное состояние: Раунд 1, все действия потрачены...");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Время:", 5000),
            "Строка «Время:» так и не появилась в легенде.");
        var initialRows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        var failures = new List<string>();
        var initialTimeRow = initialRows.FirstOrDefault(r => r.Contains("Время:"));
        if (initialTimeRow == null || !initialTimeRow.Contains("Раунд 1"))
            failures.Add($"Исходно ожидался «Раунд 1»: «{initialTimeRow?.TrimEnd()}»");
        TestPacing.Step();

        _output.WriteLine("Шаг 1 на восток — списывает 5фт, остаток должен стать 5/10...");
        GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "5/10", 3000),
            "После первого шага скорость не показала 5/10.");
        _output.WriteLine("После шага 1: «5/10» отображено.");
        TestPacing.Step();

        _output.WriteLine("Шаг 2 на восток — списывает оставшиеся 5фт ровно в ноль, раунд ещё не должен смениться...");
        GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "0/10", 3000),
            "После второго шага скорость не показала 0/10.");
        _output.WriteLine("После шага 2: «0/10» отображено.");
        TestPacing.Step();

        var midRows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        var midTimeRow = midRows.FirstOrDefault(r => r.Contains("Время:"));
        if (midTimeRow == null || !midTimeRow.Contains("Раунд 1"))
            failures.Add($"После двух шагов (ровно в остаток скорости) раунд не должен был смениться: «{midTimeRow?.TrimEnd()}»");
        TestPacing.Step();

        _output.WriteLine("Третий шаг — скорости уже не хватает (0фт), это должно продвинуть раунд и сбросить скорость/действия...");
        TestPacing.Step();
        GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Раунд 2", 3000),
            "После шага, требующего больше скорости, чем осталось, раунд не продвинулся до «Раунд 2».");
        _output.WriteLine("Раунд продвинулся. Проверяю сброс скорости и действий...");
        TestPacing.Step();

        var finalRows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);

        // Новый раунд: полный запас (10фт) минус стоимость текущего шага (5фт) = 5/10.
        CombatStateScenarioTests.CheckMovementSquares(game.Pid, finalRows, "5/10", (215, 215, 225), (65, 68, 78), failures);

        // Действия должны были сброситься с "потрачено" на "доступно".
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
}
