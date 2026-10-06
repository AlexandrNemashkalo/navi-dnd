using Xunit.Abstractions;


namespace NaviDnD.UiTests;

// E2E: чтение записки в инвентаре (HeroInventory.Text) — MouseUiHelper.SetSelectedInventoryItem
// (постраничный показ: страница 0 — картинка+название, дальше — сам текст) и механизм "закрепления"
// выбора (DisplayConfig.PinnedInventoryIndex) поверх обычного наведения (DisplayConfig.
// SelectedInventoryIndex). Дизайн: клик/Tab закрепляют предмет («←» переживает уход мыши); обычное
// наведение на ДРУГОЙ предмет временно меняет картинку справа, но НЕ снимает «←» с закреплённого —
// она переходит на новый предмет только по явному клику/Tab (HeroDisplay.DrawInventorySection:
// arrow1/arrow2 следуют за pinIdx, а не за hover, пока что-то закреплено). Цвет стрелки одинаковый
// в обоих случаях (обычный "бледный" цвет выделения) — отличает закрепление не цвет, а то, что
// стрелка не пропадает при уходе мыши. См. Fixtures/full_game.json: "Длинный меч"
// (hero.inventory[1]) — единственный предмет с заданным `text`.
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
        // "Стрелы" (не экипировано) — в отличие от "Длинный лук" (экипирован в обе руки) не дублируется
        // строкой в слотах экипировки выше списка инвентаря, где FindIndex поймал бы не ту строку.
        const string otherItem = "Стрелы";

        // HeroDisplay.DrawSubTabTitle рисует СВОЙ индикатор "◄ N/M ►" в строке с ярлыками
        // ИНВЕНТАРЬ/СПОСОБНОСТИ/СОСТОЯНИЯ (пагинация списка инвентаря) — при 18 предметах в фикстуре
        // у него тоже 2 страницы, поэтому "◄ 1/2 ►" на экране одновременно с индикатором записки.
        // Отличаем по строке: у панели записки (DialogDisplay) метки "ИНВЕНТАРЬ" в той же строке нет.
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

        // 1. Наведение показывает страницу 1/2 (картинка+название) — до клика без закрепления,
        // маркер «←» пока просто следует за наведением (как обычный, не-note предмет).
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

        // 2. Клик закрепляет предмет — в отличие от обычного наведения, маркер и индикатор страниц
        // переживают уход мыши в пустую область (обычное наведение сбросило бы их в null, как
        // проверяет InventoryHoverScenarioTests).
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

        // 3. Наведение на ДРУГОЙ предмет временно меняет картинку справа (у него нет text — индикатор
        // страниц пропадает), но маркер «←» НЕ уходит с закреплённого — только явный клик/Tab
        // переносит закрепление.
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

        // 4. Уход мыши с B возвращает показ закреплённого A — маркер и так был на месте, но контент
        // справа должен вернуться к записке.
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

        // 5. Клик по стрелке ► листает на страницу текста записки.
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

        // 6. Клик по стрелке ◄ возвращает картинку (страница 1/2).
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

        // 7. Клавиатурное листание (RightArrow/LeftArrow при закреплённой записке — InputBox
        // возвращает "NoteLeft"/"NoteRight" вместо обычного текстового ввода).
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
