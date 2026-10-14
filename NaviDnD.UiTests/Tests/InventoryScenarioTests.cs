using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: SendAction (mock) removes a bow, puts on a sword, removes one item and adds a new one - we check that
// the hero card (equipment slots + inventory list) reflects this correctly. AI-provider is locked
// (Fixtures/MockResponses/Inventory/SendAction/response.json) - the real AI is not called.
public class InventoryScenarioTests
{
    private readonly ITestOutputHelper _output;

    public InventoryScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EquipUnequipAndInventoryChanges_DisplayCorrectly()
    {
        using var game = GameSession.Launch(mockedActions: ["SendAction"], mockResponsesFolder: "Inventory");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");
        var failures = new List<string>();

        Navigation.ToCharacterScreen(game.Pid);
        _output.WriteLine("Экран Персонажа (вкладка Инвентарь) отрисован.");

        // Fixtures/full_game.json: hero.inventory[0]=Longbow (L/R Arm), [1]=Longsword (not equipped),
        // [6]=Dagger. Mock answer: the bow is removed, the sword is put in [R Hand], the dagger is removed, the
        // “Magic Ring” is added.
        _output.WriteLine("Отправляю игровое действие — жду ответ SendAction (мок)...");
        GameConsole.SendText(game.Pid, "убираю лук за спину, беру меч в руку — кинжал куда-то делся");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);

        // “ring” is the LAST word of the message (see response.json): while the text is being printed letter by
        // letter (SequentialHistoryPlayer, ~25ms/character), the hero card on the left is still OLD - it is
        // redrawn only when SendAction() is completely completed and the external game loop (Program.cs) will
        // redraw the screen again. The patch on hero.inventory has already been applied (it is applied BEFORE
        // the start of printing the history - see SequentialHistoryPlayer.PlayAsync), but the screen does not
        // show this yet. We are waiting for the last word + a small margin, and not the first word of the
        // phrase (otherwise we will catch the data BEFORE applying the patch/redrawing).
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                "кольцо", 15000),
            "Не дождались ответа SendAction (мок) — сообщение из истории не появилось.");
        _output.WriteLine("Ответ применён. Жду перерисовку карточки героя...");

        // NOT "sleep fixed time + one snapshot": the hero card is redrawn by a DIFFERENT call and at a
        // DIFFERENT time than the dialog panel - under load (the entire set of tests in a row), the fixed pause
        // is sometimes not enough. Do NOT just look for “Long Sword” - it is ALREADY visible in the regular
        // inventory list (unequipped) BEFORE the patch, a simple Contains would give a false positive on
        // outdated data. We are waiting for the [P Hand] SLOT with a sword.
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                rows => rows.Any(r => r.Contains("П Рука") && r.Contains("Длинный меч")), 5000),
            "Карточка героя не перерисовалась с обновлённой экипировкой после ответа SendAction (мок).");
        TestPacing.Step();

        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);

        _output.WriteLine("Проверяю смену экипировки: лук снят ([Л Рука] пусто), меч надет в [П Рука]...");
        CheckEquipSlot(rows, "П Рука", "Длинный меч", failures);
        CheckEquipSlotEmpty(rows, "Л Рука", failures);
        TestPacing.Step();

        _output.WriteLine("Проверяю добавление/удаление предметов инвентаря (с перелистыванием страниц)...");
        var allPagesText = CollectAcrossPages(game.Pid);
        if (!allPagesText.Any(r => r.Contains("Волшебное кольцо")))
            failures.Add("Добавленный предмет «Волшебное кольцо» не найден ни на одной странице инвентаря.");
        if (allPagesText.Any(r => r.Contains("Кинжал")))
            failures.Add("Удалённый предмет «Кинжал» всё ещё виден на одной из страниц инвентаря.");
        TestPacing.Step();

        _output.WriteLine(failures.Count == 0
            ? "Все проверки пройдены."
            : $"Провалено проверок: {failures.Count}.");
        // The final Step() is before Assert, not after: if the checks fail, Assert.True below will throw an
        // exception and the code after it will not be executed, and a pause for debugging is needed in any
        // case.
        TestPacing.Step();
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }

    // Equipment slot - a line like "P Arm Long Sword" (see HeroDisplay.DrawHeroCard - leftSlots draws the slot
    // name and the item name on ONE line).
    private static void CheckEquipSlot(List<string> rows, string slotName, string expectedItem, List<string> failures)
    {
        var row = rows.FirstOrDefault(r => r.Contains(slotName));
        if (row == null) { failures.Add($"Слот экипировки «{slotName}» не найден на экране."); return; }
        if (!row.Contains(expectedItem))
            failures.Add($"Слот «{slotName}» не показывает «{expectedItem}»: «{row.TrimEnd()}»");
    }

    private static void CheckEquipSlotEmpty(List<string> rows, string slotName, List<string> failures)
    {
        var row = rows.FirstOrDefault(r => r.Contains(slotName));
        if (row == null) { failures.Add($"Слот экипировки «{slotName}» не найден на экране."); return; }
        if (!row.Contains('—'))
            failures.Add($"Слот «{slotName}» должен быть пуст («—»), но: «{row.TrimEnd()}»");
    }

    // Goes through all inventory pages (► while there is one), collecting all the text - to check the
    // presence/absence of an item, regardless of which page it appears on.
    private static List<string> CollectAcrossPages(int gamePid, int maxPages = 10)
    {
        var all = new List<string>();
        for (int page = 0; page < maxPages; page++)
        {
            var rows = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
            all.AddRange(rows);

            int arrowRow = rows.FindIndex(r => r.Contains('►'));
            if (arrowRow < 0) break;
            int arrowCol = rows[arrowRow].IndexOf('►');
            GameConsole.SendLeftClick(gamePid, arrowCol, arrowRow);
            Thread.Sleep(400);
            TestPacing.Step();
        }
        return all;
    }
}
