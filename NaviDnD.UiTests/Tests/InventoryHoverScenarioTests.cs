using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: выбор элемента инвентаря наведением мыши (MouseUiHelper.MakeCharacterPollAction — "Inventory
// item hover" — и HeroDisplay.WriteInvLine, который дорисовывает "←" у выбранной первой строки
// предмета) — недавно добавленная фича (см. историю коммитов), покрытая только кликовой пагинацией
// (GameScenarioTests.CheckInventoryPagination), но не самим наведением и не сбросом выбора при
// уходе мыши.
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
        // Заголовок экрана и тело карточки (инвентарь) рисуются РАЗНЫМИ вызовами — заголовок мог
        // смениться раньше, чем дорисовался список инвентаря ниже (та же гонка, что чинили в
        // GameScenarioTests.FullGameplayScenario). Явно ждём появления предмета, а не полагаемся на
        // то, что он успел отрисоваться к моменту возврата из Navigation.ToCharacterScreen.
        const string itemMarker = "Длинный меч";
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, itemMarker, 5000),
            $"Предмет «{itemMarker}» не появился на экране Персонажа (карточка не успела отрисоваться?).");
        _output.WriteLine("Экран Персонажа (вкладка Инвентарь) отрисован.");
        TestPacing.Step();

        // "Длинный меч" — первый неэкипированный предмет на первой странице инвентаря (см. также
        // GameScenarioTests.CheckInventoryPagination) — гарантированно виден без пагинации.
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
