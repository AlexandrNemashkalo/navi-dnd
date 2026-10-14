using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: two real bugs in the character editor (Screen.NewGameCharacter) - hovering the mouse over an inventory
// item did not show the picture, and Tab did not scroll through the item selection. The root of both: calling
// MouseUiHelper.HandleGameScreen in Program.cs for this screen did not transfer onTab/onShiftTab/
// onInventoryItemHover (unlike the exact same call for gameplay Screen.Character, where these callbacks are) -
// with null- callbacks, HandleGameScreen silently does nothing for Tab and does not call
// MouseUiHelper.SetSelectedImage on hover. Worse still: the untransmitted onTab meant that the literal string
// "Tab" failed to be processed as normal text input from the player and flew away. FixHeroForNewGame as if the
// player had literally written "Tab".
public class NewGameCharacterEditorScenarioTests
{
    private readonly ITestOutputHelper _output;

    public NewGameCharacterEditorScenarioTests(ITestOutputHelper output) => _output = output;

    private static void TypeField(int gamePid, string text)
    {
        GameConsole.SendText(gamePid, text);
        GameConsole.SendKey(gamePid, ConsoleKey.Enter);
        Thread.Sleep(150);
    }

    // F2 → profile (5 fields) → character editor. General approach, duplicates the beginning of
    // NewGameScenarioTests.CreateHeroAndStartNewGame - your own GameSession to the file, there is no need to
    // copy the rest of the run here (F3/StartNewGame, etc.), we are testing only the editor.
    private void OpenEditor(GameSession game)
    {
        Assert.True(Navigation.ToAnketa(game.Pid),
            "Не дождались анкеты создания персонажа после [F2] в меню.");

        TypeField(game.Pid, "Тестовый Герой");
        TypeField(game.Pid, "TST");
        TypeField(game.Pid, "Проверочный герой для e2e-теста.");
        GameConsole.SendKey(game.Pid, ConsoleKey.F3);   // “NEXT” of the questionnaire - the same as [F3] (blank button without arrows)

        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ РЕДАКТОР ←", 20000),
            "Не дождались экрана редактора персонажа — CreateNewGame (мок) не отработал.");
        Thread.Sleep(200);
    }

    // "Test Sword" is equipped (equipmentedSlot:[ "P Hand"]) - its name appears on the screen TWICE: in the
    // list of equipment slots ("P Hand Test Sword") and in the inventory list itself ("• Test Sword: ..."). We
    // are looking for exactly the second line - with a colon after the name, which is not in the equipment slot
    // line.
    private static string InventoryLineMarker(string itemName) => itemName + ":";

    // Fixtures/MockResponses/CreateNewGame/response.json: "Test Sword" is the only item with image
    // ("lorc/broadsword") specified specifically for this test. The mock hero does not have his own hero.image,
    // so the right panel before hovering shows the default D20 (DiceArt.D20, see
    // DialogDisplay.GetDefaultRightPanelLines) - any change in the contents of the panel after hovering means
    // that the item’s picture has actually been drawn.
    [Fact]
    public void HoverInventoryItemWithImage_UpdatesRightPanel()
    {
        using var game = GameSession.LaunchForNewGame();
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");
        OpenEditor(game);
        _output.WriteLine("Редактор персонажа отрисован.");

        string marker = InventoryLineMarker("Тестовый меч");
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, marker, 5000),
            "Предмет «Тестовый меч» не найден в списке инвентаря редактора.");

        var beforeRows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        int itemRow = beforeRows.FindIndex(r => r.Contains(marker));
        Assert.True(itemRow >= 0, "Строка предмета «Тестовый меч» не найдена в списке инвентаря.");

        int hoverCol = beforeRows[itemRow].IndexOf(marker, StringComparison.Ordinal) + 2;
        _output.WriteLine("Навожу мышь на «Тестовый меч» (есть image в мок-ответе)...");
        GameConsole.SendMouseMove(game.Pid, hoverCol, itemRow);

        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => r[itemRow].Contains(marker) && r[itemRow].Contains("←"), 3000),
            "Наведение на предмет в редакторе не подсветило строку маркером «←».");
        _output.WriteLine("Строка подсветилась — наведение зарегистрировано движком.");

        // RerenderRightPanel is called separately from highlighting the line (the same pattern as redrawing the
        // card after the mock response in other tests) - we poll, and do not sleep fixedly.
        bool panelChanged = GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
            afterRows =>
            {
                for (int i = 0; i < beforeRows.Count && i < afterRows.Count; i++)
                {
                    if (i == itemRow) continue;
                    if (beforeRows[i] != afterRows[i]) return true;
                }
                return false;
            }, 3000);

        Assert.True(panelChanged,
            "После наведения на предмет с image картинка не появилась — правая панель диалога не изменилась " +
            "(регрессия: onInventoryItemHover не передан в MouseUiHelper.HandleGameScreen для Screen.NewGameCharacter).");
        _output.WriteLine("Правая панель изменилась — картинка предмета отрисована.");
        TestPacing.Step();
    }

    // Tab should cycle through inventory items (like in the Character gameplay screen) - the "←" marker should
    // move from one item to another. Previously, onTab was not transmitted: Tab did not do anything AT ALL for
    // selection, and (worse) flew away as the literal text "Tab" in FixHeroForNewGame - we check that the
    // marker is actually moving, and that the message "Tab" from the player does not appear in the dialogue
    // history.
    [Fact]
    public void TabKey_CyclesInventorySelection_AndIsNotSentAsPlayerText()
    {
        using var game = GameSession.LaunchForNewGame();
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");
        OpenEditor(game);
        _output.WriteLine("Редактор персонажа отрисован.");

        // The sword is equipped (a double in the equipment slots - you need a marker with ":"), the torch is
        // not equipped (equipmentedSlot:[], there is no double - but it has quantity:3, the format is "Test
        // torch ×3:", so we are looking for the usual name without ":" for it, not InventoryLineMarker).
        string swordMarker = InventoryLineMarker("Тестовый меч");
        const string torchMarker = "Тестовый факел";
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, torchMarker, 5000),
            "Предмет «Тестовый факел» не найден в списке инвентаря редактора.");

        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        int swordRow = rows.FindIndex(r => r.Contains(swordMarker));
        int torchRow = rows.FindIndex(r => r.Contains(torchMarker));
        Assert.True(swordRow >= 0 && torchRow >= 0, "Оба тестовых предмета должны быть видны на первой странице инвентаря.");

        _output.WriteLine("Нажимаю [Tab] — жду выбор первого предмета («Тестовый меч»)...");
        GameConsole.SendKey(game.Pid, ConsoleKey.Tab);
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => r[swordRow].Contains("←"), 3000),
            "[Tab] в редакторе персонажа не выбрал первый предмет инвентаря (маркер «←» не появился).");

        _output.WriteLine("Нажимаю [Tab] ещё раз — жду переход маркера на второй предмет («Тестовый факел»)...");
        GameConsole.SendKey(game.Pid, ConsoleKey.Tab);
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => r[torchRow].Contains("←") && !r[swordRow].Contains("←"), 3000),
            "Повторный [Tab] не переключил маркер «←» со «Тестовый меч» на «Тестовый факел».");
        _output.WriteLine("Tab корректно циклически переключает выбор предмета.");

        var afterRows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        Assert.DoesNotContain(afterRows, r => r.Contains("Тестовый Герой: Tab"));
        _output.WriteLine("Tab не попал в историю диалога как текстовое сообщение игрока.");
        TestPacing.Step();
    }
}
