using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: автоматический ход врага (MapScreenLoop.RunEnemyTurn → GameAiClient.FireEnemyTurn) — после
// ЛЮБОГО свободного действия игрока движок сам вызывает ход того, чей CombatState.CurrentTurn
// сейчас активен, если это существо (не герой). FireEnemyTurn использует тот же системный промпт
// (Prompts/Dnd5e/SendAction), что и обычный SendAction — то есть оба вызова в рамках одного теста
// перехватываются ОДНИМ мок-именем действия "SendAction". Различаем их через нумерованные файлы
// ответа SelectiveMockProvider (response.json — 1й вызов, response2.json — 2й) — это даёт РЕАЛЬНО
// РАЗНЫЙ, а не задублированный контент для "действия игрока" и для "автоматического хода врага".
//
// response2.json в обеих папках (EnemyAttack/EnemyMove) написан строго по формату из
// Prompts/Dnd5e/SendAction/systemPrompt.md § БОЙ "Структура history хода врага": патч НЕ корневой
// ключ, а вложен в history[].patch, движение — через movePath, а финальная запись хода врагов
// обязана передать ход обратно герою (combat.currentTurn=hero, totalRounds+1, hero.actions/
// speedLeft сброшены) — самопроверка промпта, пункт 10.
//
// Fixtures/enemy_turn_game.json: герой в [1,3] с УЖЕ потраченными действиями/скоростью (это и
// есть причина, почему сейчас ход гоблина "GBL" в [5,3]), Combat.Active=true, CurrentTurn="GBL".
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

        // Скорость сброшена ДО полного максимума (30/30) — ни одна клетка не "потрачена", поэтому
        // здесь достаточно проверить текст (CheckMovementSquares проверяет ещё и цвет ПОТРАЧЕННОЙ
        // клетки, а таких при 30/30 попросту нет — сэмплировать было бы нечего).
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
        // Легенда с активным боем выше обычной (+7 строк CombatReservedLines) — окну консоли нужен
        // момент, чтобы дорисовать нижнюю часть после первичной отрисовки заголовка (см. тот же
        // класс гонки в CombatStateScenarioTests). Ждём «Время:» — рисуется последней строкой блока.
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

        // Скорость сброшена ДО полного максимума (30/30) — ни одна клетка не "потрачена", поэтому
        // здесь достаточно проверить текст (CheckMovementSquares проверяет ещё и цвет ПОТРАЧЕННОЙ
        // клетки, а таких при 30/30 попросту нет — сэмплировать было бы нечего).
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

    // Карта и легенда рисуются на ОДНОЙ строке side-by-side, и легенда ТОЖЕ показывает имя/символ
    // существа текстом ("GBL : Гоблин - ...") — поиск "GBL" по всей строке может найти легенду
    // вместо самой сетки (они не обязательно на одной физической строке, раз легенда не привязана
    // к Y-координате существа). Ограничиваем поиск левой частью строки — областью карты, до
    // внутренней границы с легендой (MapDisplay: mapWidth = cols*(cellWidth+1)+7 = 20*4+7 = 87).
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
