using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: отрисовка состояния боя в легенде (LegendDisplay.BuildCombatLines) — порядок хода,
// доступные/потраченные действия, доступная/потраченная скорость, раунд/день. Чисто рендер, без
// обращения к ИИ (Fixtures/combat_state_game.json задаёт Combat.Active/Initiative/Actions
// статично) — движок уже показывает всё это на экране, но раньше это не проверялось ни одним
// UI-тестом.
public class CombatStateScenarioTests
{
    internal static readonly (byte r, byte g, byte b) MainBackground = (18, 18, 22);
    internal const int ColorTolerance = 30;

    private readonly ITestOutputHelper _output;

    public CombatStateScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void CombatState_ShowsTurnOrderSpeedAndActionsCorrectly()
    {
        using var game = GameSession.Launch(fixtureFileName: "combat_state_game.json");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000),
            "Не дождались экрана Карты после [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован.");
        TestPacing.Step();

        // Легенда с активным боем выше обычной (+7 строк CombatReservedLines) — окну консоли
        // нужен момент, чтобы досчитать размер и дорисовать нижнюю часть после первичной отрисовки
        // заголовка. Ждём строку «Время:» — она рисуется ПОСЛЕДНЕЙ в BuildCombatLines, так что её
        // появление гарантирует, что все строки ВЫШЕ (порядок хода/действия/скорость) уже отрисованы;
        // ждать первую строку («Порядок боя») недостаточно — сама она может появиться раньше, чем
        // остальные строки того же блока успеют дорисоваться.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Время:", 5000),
            "Строка «Время:» так и не появилась в легенде.");

        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        var failures = new List<string>();

        _output.WriteLine("Проверяю порядок хода — текущий ход героя помечен «⚔️HRO», гоблин виден без метки...");
        var initiativeRow = rows.FirstOrDefault(r => r.Contains("Порядок боя"));
        if (initiativeRow == null) failures.Add("Строка «Порядок боя» не найдена в легенде.");
        else
        {
            if (!initiativeRow.Contains("⚔️HRO")) failures.Add($"Текущий ход героя не помечен «⚔️HRO»: «{initiativeRow.TrimEnd()}»");
            if (!initiativeRow.Contains("GB1")) failures.Add($"Гоблин «GB1» не найден в порядке хода: «{initiativeRow.TrimEnd()}»");
        }
        TestPacing.Step();

        _output.WriteLine("Проверяю отображение скорости (15/30, половина клеток потрачена)...");
        CheckMovementSquares(game.Pid, rows, "15/30", (215, 215, 225), (65, 68, 78), failures);
        TestPacing.Step();

        _output.WriteLine("Проверяю действия: «Действие» потрачено (серое ●), «Бон. действие»/«Реакция» доступны (цветные ■/▲)...");
        CheckActionSymbol(game.Pid, rows, "Действие", '●', (65, 68, 78), spent: true, failures);
        CheckActionSymbol(game.Pid, rows, "Бон. действие", '■', (160, 100, 50), spent: false, failures);
        CheckActionSymbol(game.Pid, rows, "Реакция", '▲', (220, 70, 70), spent: false, failures);
        TestPacing.Step();

        _output.WriteLine("Проверяю строку раунда/дня...");
        var timeRow = rows.FirstOrDefault(r => r.Contains("Время:"));
        if (timeRow == null) failures.Add("Строка «Время:» не найдена в легенде.");
        else if (!timeRow.Contains("Раунд 3") || !timeRow.Contains("День 1") || !timeRow.Contains("Утро"))
            failures.Add($"Строка времени не содержит ожидаемые «Утро · День 1 · Раунд 3»: «{timeRow.TrimEnd()}»");
        TestPacing.Step();

        _output.WriteLine(failures.Count == 0
            ? "Все проверки пройдены."
            : $"Провалено проверок: {failures.Count}.");
        TestPacing.Step();
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }

    // Fixture: speedLeft=15, speedMax=30 → 6 квадратов всего (max/5), 3 белых (доступно) + 3 серых (потрачено).
    internal static void CheckMovementSquares(
        int gamePid, List<string> rows, string expectedFraction, (int r, int g, int b) availableColor, (int r, int g, int b) spentColor, List<string> failures)
    {
        var row = rows.FirstOrDefault(r => r.Contains("Движение:"));
        if (row == null) { failures.Add("Строка «Движение:» не найдена в легенде."); return; }
        if (!row.Contains(expectedFraction))
            failures.Add($"Строка движения не показывает «{expectedFraction}»: «{row.TrimEnd()}»");

        int rowIndex = rows.IndexOf(row);
        int firstSquare = row.IndexOf('■');
        int lastSquare = row.LastIndexOf('■');
        if (firstSquare < 0) { failures.Add("Квадраты скорости «■» не найдены в строке движения."); return; }

        // Первый квадрат — доступная скорость, последний — потраченная (если весь остаток доступен,
        // это одна и та же ячейка — тогда обе проверки просто совпадут, что и ожидается).
        ScreenAssertions.CheckCellColor(gamePid, firstSquare, rowIndex, availableColor, ColorTolerance, MainBackground, "квадрат скорости (доступно)", failures);
        ScreenAssertions.CheckCellColor(gamePid, lastSquare, rowIndex, spentColor, ColorTolerance, MainBackground, "квадрат скорости (потрачено)", failures);
    }

    internal static void CheckActionSymbol(
        int gamePid, List<string> rows, string label, char symbol, (int r, int g, int b) expectedColor, bool spent, List<string> failures)
    {
        var row = rows.FirstOrDefault(r => r.Contains(label + ":"));
        if (row == null) { failures.Add($"Строка действия «{label}» не найдена в легенде."); return; }
        int rowIndex = rows.IndexOf(row);

        // Символ ищем ПОСЛЕ метки (сам символ действия должен быть правее двоеточия метки).
        int labelEnd = row.IndexOf(label + ":") + label.Length + 1;
        int symbolCol = row.IndexOf(symbol, labelEnd);
        if (symbolCol < 0) { failures.Add($"Символ «{symbol}» ({(spent ? "потрачено" : "доступно")}) не найден в строке «{label}»: «{row.TrimEnd()}»"); return; }

        ScreenAssertions.CheckCellColor(gamePid, symbolCol, rowIndex, expectedColor, ColorTolerance, MainBackground,
            $"действие «{label}» ({(spent ? "потрачено" : "доступно")})", failures);
    }
}
