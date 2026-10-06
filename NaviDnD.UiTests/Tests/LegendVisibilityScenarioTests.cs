using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: легенда карты (LegendDisplay.GetLegendLines) должна показывать ТОЛЬКО то, что герой реально
// видит (совпадает с фильтром отрисовки MapObjectsProvider — Hidden!=true && VisibleCells), и НЕ
// показывать скрытые (hidden:true) объекты вроде ловушек, которые герой ещё не обнаружил.
//
// Исключение: сетка рисует только ОДИН символ на клетку (сущности всегда перекрывают объекты —
// MapObjectsProvider.WriteCellEntity проверяет entities раньше objects, а герой перекрывает всё на
// своей клетке). Если объект оказался в той же клетке, что герой или другая сущность, легенда всё
// равно должна его перечислять (сам факт видимости уже отдельно проверен), но с координатами — иначе
// игрок не поймёт, где именно этот объект находится, раз его символ не виден на сетке.
//
// Fixtures/legend_visibility_game.json: герой в [1,3] стоит на "Свиток" (объект перекрыт героем);
// НПС "Стражник" в [5,3] стоит на "Кольцо" (объект перекрыт другой сущностью); "Факел" в [2,3] —
// обычный видимый предмет, ничем не перекрыт (контрольная проверка, что для него координаты НЕ
// дописываются); "Ловушка" в [3,3] — hidden:true (герой её не обнаружил, не должна быть в легенде).
public class LegendVisibilityScenarioTests
{
    private readonly ITestOutputHelper _output;

    public LegendVisibilityScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Legend_ShowsOnlyVisibleItems_AndAnnotatesCoveredOnesWithCoordinates()
    {
        using var game = GameSession.Launch(fixtureFileName: "legend_visibility_game.json");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000),
            "Не дождались экрана Карты после [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован.");
        TestPacing.Step();

        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        var failures = new List<string>();

        _output.WriteLine("Проверяю, что скрытая (hidden:true) ловушка вообще не отображается на экране...");
        if (rows.Any(r => r.Contains("Ловушка")))
            failures.Add("Скрытая (hidden:true) «Ловушка» видна на экране — герой не должен был её обнаружить.");
        TestPacing.Step();

        _output.WriteLine("Проверяю, что сетка показывает героя и НПС, а не перекрытые ими предметы...");
        var heroRow = rows.FirstOrDefault(r => r.Contains("HRO"));
        if (heroRow == null) failures.Add("Символ героя «HRO» не найден на сетке.");
        else if (heroRow.Contains("[S]")) failures.Add("Символ перекрытого предмета «[S]» виден на клетке героя — герой должен его перекрывать.");

        var npcRow = rows.FirstOrDefault(r => r.Contains("NPC"));
        if (npcRow == null) failures.Add("Символ НПС «NPC» не найден на сетке.");
        else if (npcRow.Contains("[R]")) failures.Add("Символ перекрытого предмета «[R]» виден на клетке НПС — сущность должна его перекрывать.");
        TestPacing.Step();

        _output.WriteLine("Проверяю, что перекрытые предметы всё равно перечислены в легенде — с координатами...");
        CheckCoveredInLegend(rows, "Свиток", "[1,3]", failures);
        CheckCoveredInLegend(rows, "Кольцо", "[5,3]", failures);
        TestPacing.Step();

        _output.WriteLine("Проверяю, что обычный (не перекрытый) предмет в легенде — без координат...");
        var torchRow = rows.FirstOrDefault(r => r.Contains("Факел"));
        if (torchRow == null) failures.Add("Предмет «Факел» не найден в легенде.");
        else if (torchRow.Contains('[') || torchRow.Contains(']'))
            failures.Add($"У обычного (не перекрытого) предмета «Факел» неожиданно появились координаты: «{torchRow.TrimEnd()}»");
        TestPacing.Step();

        _output.WriteLine(failures.Count == 0
            ? "Все проверки пройдены."
            : $"Провалено проверок: {failures.Count}.");
        TestPacing.Step();
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }

    private static void CheckCoveredInLegend(List<string> rows, string itemName, string expectedCoords, List<string> failures)
    {
        var row = rows.FirstOrDefault(r => r.Contains(itemName));
        if (row == null) { failures.Add($"Перекрытый предмет «{itemName}» не найден в легенде — должен быть перечислен, несмотря на перекрытие на сетке."); return; }
        if (!row.Contains(expectedCoords))
            failures.Add($"Перекрытый предмет «{itemName}» найден в легенде, но без ожидаемых координат «{expectedCoords}»: «{row.TrimEnd()}»");
    }
}
