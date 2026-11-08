using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: map legend (LegendDisplay.GetLegendLines) should show ONLY what the hero actually sees (matches the
// rendering filter MapObjectsProvider - Hidden!=true && VisibleCells), and NOT show hidden (hidden:true)
// objects like traps that the hero has not yet discovered. Exception: the grid draws only ONE symbol per cell
// (entities always overlap objects - MapObjectsProvider.WriteCellEntity checks entities before objects, and the
// hero overlaps everything on his cell). If the object is in the same cell as the hero or another entity, the
// legend should still list it (the fact of visibility has already been checked separately), but with
// coordinates - otherwise the player will not understand where exactly this object is located, since its symbol
// is not visible on the Fixtures/legend_visibility_game.json: hero grid. [1,3] is on “Scroll” (the object is
// blocked by the hero); NPC “Guard” in [5,3] is on “Ring” (the object is blocked by another entity); “Torch” in
// [2,3] is an ordinary visible object, not blocked by anything (check that the coordinates are NOT added for
// it); “Trap” in [3,3] - hidden:true (the hero did not find it, should not be in it); legend).
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
