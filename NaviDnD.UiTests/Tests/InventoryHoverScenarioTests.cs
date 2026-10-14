using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: selection of an inventory item by mouse hover (MouseUiHelper.MakeCharacterPollAction - "Inventory item
// hover" - and HeroDisplay.WriteInvLine, which adds a "←" to the selected first line of the item) - a recently
// added feature (see commit history), covered only by clickable pagination
// (GameScenarioTests.CheckInventoryPagination), but not by itself hovering and not resetting the selection when
// the mouse moves away.
public class InventoryHoverScenarioTests
{
    private readonly ITestOutputHelper _output;

    public InventoryHoverScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void HoverOverItem_HighlightsRow_AndResetsWhenMouseLeaves()
    {
        using var game = GameSession.Launch();
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.ToCharacterScreen(game.Pid);
        // The screen title and the body of the card (inventory) are drawn by DIFFERENT calls - the title could
        // change before the inventory list below was completed (the same race that was fixed in
        // GameScenarioTests.FullGameplayScenario). We are clearly waiting for the item to appear, and not
        // relying on the fact that it managed to be drawn by the time we returned from
        // Navigation.ToCharacterScreen.
        const string itemMarker = "Длинный меч";
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, itemMarker, 5000),
            $"Предмет «{itemMarker}» не появился на экране Персонажа (карточка не успела отрисоваться?).");
        _output.WriteLine("Экран Персонажа (вкладка Инвентарь) отрисован.");
        TestPacing.Step();

        // "Long Sword" - the first unequipped item on the first inventory page (see also
        // GameScenarioTests.CheckInventoryPagination) - guaranteed to be visible without pagination.
        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        int itemRow = rows.FindIndex(r => r.Contains(itemMarker));
        Assert.True(itemRow >= 0, $"Предмет «{itemMarker}» не найден на первой странице инвентаря.");
        Assert.DoesNotContain("←", rows[itemRow]);
        _output.WriteLine($"Предмет «{itemMarker}» найден в строке {itemRow}, без наведения — маркера «←» ещё нет.");

        int hoverCol = rows[itemRow].IndexOf(itemMarker, StringComparison.Ordinal) + 2;
        _output.WriteLine("Навожу мышь на строку предмета...");
        GameConsole.SendMouseMove(game.Pid, hoverCol, itemRow);
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => r[itemRow].Contains(itemMarker) && r[itemRow].Contains("←"), 3000),
            $"После наведения мыши на «{itemMarker}» строка не подсветилась маркером «←».");
        _output.WriteLine("Строка подсветилась маркером «←» при наведении.");
        TestPacing.Step();

        _output.WriteLine("Увожу мышь за пределы списка инвентаря — выбор должен сброситься...");
        GameConsole.SendMouseMove(game.Pid, 2, 2);

        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => !r[itemRow].Contains("←"), 3000),
            $"После ухода мыши с предмета «{itemMarker}» маркер «←» не пропал — выбор наведением не сбрасывается.");
        _output.WriteLine("Выбор наведением сброшен после ухода мыши.");
        TestPacing.Step();
    }
}
