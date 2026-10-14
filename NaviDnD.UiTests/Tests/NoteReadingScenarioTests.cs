using Xunit.Abstractions;


namespace NaviDnD.UiTests;

// E2E: reading a note in the inventory (HeroInventory.Text) - MouseUiHelper.SetSelectedInventoryItem
// (page-by-page display: page 0 - picture + title, then the text itself) and the mechanism for "fixing" the
// selection (DisplayConfig.PinnedInventoryIndex) on top of normal hovering (DisplayConfig.
// SelectedInventoryIndex). Design: click / Tab pin the item ("←" experiences moving the mouse); normal pointing
// to ANOTHER item temporarily changes the picture on the right, but does NOT remove the “←” from the pinned one
// - it moves to the new item only by explicit click/Tab (HeroDisplay.DrawInventorySection: arrow1/arrow2
// follows pinIdx, and not hover, while something is pinned the same in both cases (the usual “pale” color).
// highlight) - what distinguishes the fixing is not the color, but the fact that the arrow does not disappear
// when the mouse leaves. See Fixtures/full_game.json: "Long Sword" (hero.inventory[1]) is the only item with
// the given `text`.
public class NoteReadingScenarioTests
{
    private readonly ITestOutputHelper _output;

    public NoteReadingScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void PinNoteItem_PersistsAcrossHover_AndPagesViaMouseAndKeyboard()
    {
        using var game = GameSession.Launch();
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.ToCharacterScreen(game.Pid);
        const string noteItem = "Длинный меч";
        // "Arrows" (not equipped) - unlike "Longbow" (both hands equipped) is not duplicated by a string in the
        // equipment slots above the inventory list, where FindIndex would catch the wrong string.
        const string otherItem = "Стрелы";

        // HeroDisplay.DrawSubTabTitle draws HIS indicator "◄ N/M ►" in the line with the
        // INVENTORY/ABILITIES/CONDITIONS labels (pagination of the inventory list) - with 18 items in the
        // fixture, it also has 2 pages, so "◄ 1/2 ►" is on the screen at the same time as the note indicator.
        // We distinguish by line: the note panel (DialogDisplay)) does not have the “INVENTORY” label in the
        // same line.
        static bool IsNoteIndicatorRow(string line, string marker) => line.Contains(marker) && !line.Contains("ИНВЕНТАРЬ");
        bool NoteIndicatorPresent(List<string> rows, string marker) => rows.Any(r => IsNoteIndicatorRow(r, marker));

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, noteItem, 5000),
            $"Предмет «{noteItem}» не появился на экране Персонажа.");
        _output.WriteLine("Экран Персонажа (вкладка Инвентарь) отрисован.");
        TestPacing.Step();

        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        int noteRow = rows.FindIndex(r => r.Contains(noteItem));
        int otherRow = rows.FindIndex(r => r.Contains(otherItem));
        Assert.True(noteRow >= 0 && otherRow >= 0, "Оба тестовых предмета должны быть видны на первой странице инвентаря.");
        int noteCol = rows[noteRow].IndexOf(noteItem, StringComparison.Ordinal) + 2;
        int otherCol = rows[otherRow].IndexOf(otherItem, StringComparison.Ordinal) + 2;

        // 1. Hover shows page 1/2 (picture+title) - before clicking without pinning, the “←” marker just
        // follows the hover (like a regular, non-note item).
        _output.WriteLine($"Навожу мышь на «{noteItem}» (есть text в фикстуре)...");
        GameConsole.SendMouseMove(game.Pid, noteCol, noteRow);
        Thread.Sleep(150);
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => r[noteRow].Contains(noteItem) && r[noteRow].Contains("←"), 3000),
            $"Наведение на «{noteItem}» не подсветило строку маркером «←».");
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => NoteIndicatorPresent(r, "◄ 1/2 ►"), 3000),
            "После наведения на предмет с текстом не появился индикатор страниц «◄ 1/2 ►».");
        _output.WriteLine("Индикатор страниц 1/2 появился при наведении.");
        TestPacing.Step();

        // 2. Clicking locks the item - unlike normal hover, the marker and page indicator survive the mouse
        // moving into an empty area (normal hover would reset them to null, as InventoryHoverScenarioTests).
        // checks
        _output.WriteLine($"Кликаю по «{noteItem}» — закрепляю выбор, затем увожу мышь в пустую область...");
        GameConsole.SendLeftClick(game.Pid, noteCol, noteRow);
        Thread.Sleep(150);
        GameConsole.SendMouseMove(game.Pid, 2, 2);
        Thread.Sleep(150);
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => r[noteRow].Contains("←"), 3000),
            "После клика и ухода мыши маркер «←» пропал — предмет не закрепился.");
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => NoteIndicatorPresent(r, "◄ 1/2 ►"), 3000),
            "После клика и ухода мыши индикатор страниц пропал — закреплённый предмет должен остаться в правой панели.");
        _output.WriteLine("Закрепление пережило уход мыши.");
        TestPacing.Step();

        // 3. Pointing at ANOTHER item temporarily changes the picture on the right (it does not have text - the
        // page indicator disappears), but the “←” marker does NOT move away from the pinned one - only an
        // explicit click/Tab transfers the pinning.
        _output.WriteLine($"Навожу мышь на «{otherItem}» — временный превью, закрепление не должно сняться...");
        GameConsole.SendMouseMove(game.Pid, otherCol, otherRow);
        Thread.Sleep(150);
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => !NoteIndicatorPresent(r, "◄ 1/2 ►"), 3000),
            $"Индикатор страниц записки всё ещё виден при наведении на «{otherItem}» (у него нет text) — превью не переключилось.");
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => r[noteRow].Contains("←") && !r[otherRow].Contains("←"), 3000),
            $"Наведение на «{otherItem}» сняло маркер «←» с закреплённого «{noteItem}» (должен оставаться, пока по «{otherItem}» явно не кликнули).");
        _output.WriteLine("Превью сменился, но закрепление осталось на месте.");
        TestPacing.Step();

        // 4. Mouse leaving B returns the display of the pinned A - the marker was already in place, but the
        // content on the right should return to the note.
        _output.WriteLine($"Увожу мышь с «{otherItem}» — контент справа должен вернуться к закреплённому «{noteItem}»...");
        GameConsole.SendMouseMove(game.Pid, 2, 2);
        Thread.Sleep(150);
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => NoteIndicatorPresent(r, "◄ 1/2 ►"), 3000),
            $"После ухода мыши с «{otherItem}» индикатор страниц закреплённого «{noteItem}» не вернулся.");
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => r[noteRow].Contains("←") && !r[otherRow].Contains("←"), 3000),
            $"После ухода мыши с «{otherItem}» маркер «←» не на закреплённом «{noteItem}».");
        _output.WriteLine("Показ закреплённого предмета корректно восстановился.");
        TestPacing.Step();

        // 5. Click on the arrow ► to scroll to the note text page.
        rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        int indicatorRow = rows.FindIndex(r => IsNoteIndicatorRow(r, "◄ 1/2 ►"));
        Assert.True(indicatorRow >= 0, "Индикатор страниц «◄ 1/2 ►» не найден для определения позиции стрелки.");
        int indicatorStart = rows[indicatorRow].IndexOf("◄ 1/2 ►", StringComparison.Ordinal);
        int rightArrowCol = indicatorStart + "◄ 1/2 ►".Length - 1;
        _output.WriteLine("Кликаю по стрелке ► — листаю на страницу текста...");
        GameConsole.SendLeftClick(game.Pid, rightArrowCol, indicatorRow);
        Thread.Sleep(150);
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "◄ 2/2 ►", 3000),
            "Клик по ► не перелистнул на страницу 2/2.");
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "выгравировано", 3000),
            "Текст записки («...выгравировано...») не отобразился на странице 2/2.");
        _output.WriteLine("Текст записки отобразился на странице 2/2.");
        TestPacing.Step();

        // 6. Clicking on the arrow ◄ returns the picture (page 1/2).
        rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        indicatorRow = rows.FindIndex(r => r.Contains("◄ 2/2 ►"));
        Assert.True(indicatorRow >= 0, "Индикатор страниц «◄ 2/2 ►» не найден для определения позиции стрелки.");
        int leftArrowCol = rows[indicatorRow].IndexOf("◄ 2/2 ►", StringComparison.Ordinal);
        GameConsole.SendLeftClick(game.Pid, leftArrowCol, indicatorRow);
        Thread.Sleep(150);
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => NoteIndicatorPresent(r, "◄ 1/2 ►"), 3000),
            "Клик по ◄ не вернул страницу 1/2.");
        _output.WriteLine("Клик по ◄ вернул картинку (страница 1/2).");
        TestPacing.Step();

        // 7. Keyboard scrolling (RightArrow/LeftArrow with a pinned note - InputBox returns
        // "NoteLeft"/"NoteRight" instead of regular text input).
        _output.WriteLine("Проверяю клавиатурное листание (RightArrow/LeftArrow)...");
        GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);
        Thread.Sleep(150);
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "◄ 2/2 ►", 3000),
            "RightArrow не перелистнул записку на страницу 2/2.");
        GameConsole.SendKey(game.Pid, ConsoleKey.LeftArrow);
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => NoteIndicatorPresent(r, "◄ 1/2 ►"), 3000),
            "LeftArrow не вернул записку на страницу 1/2.");
        _output.WriteLine("Клавиатурное листание работает.");
        TestPacing.Step();
    }
}
