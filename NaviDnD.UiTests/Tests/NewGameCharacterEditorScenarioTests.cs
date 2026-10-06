using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: два реальных бага в редакторе персонажа (Screen.NewGameCharacter) — наведение мышью на
// предмет инвентаря не показывало картинку, и Tab не листал выбор предмета. Корень обоих: вызов
// MouseUiHelper.HandleGameScreen в Program.cs для этого экрана не передавал onTab/onShiftTab/
// onInventoryItemHover (в отличие от точно такого же вызова для геймплейного Screen.Character,
// где эти колбэки есть) — при null-колбэках HandleGameScreen молча не делает ничего для Tab и
// не вызывает MouseUiHelper.SetSelectedImage при наведении. Хуже того: непереданный onTab означал,
// что литеральная строка "Tab" проваливалась в обработку как обычный текстовый ввод игрока и
// улетала в FixHeroForNewGame как если бы игрок буквально написал "Tab".
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

    // F2 → анкета (5 полей) → редактор персонажа. Общий заход, дублирует начало
    // NewGameScenarioTests.CreateHeroAndStartNewGame — свой GameSession на файл, копировать сюда
    // весь остальной прогон (F3/StartNewGame и т.п.) не нужно, тестируем только редактор.
    private void OpenEditor(GameSession game)
    {
        Assert.True(Navigation.ToAnketa(game.Pid),
            "Не дождались анкеты создания персонажа после [F2] в меню.");

        TypeField(game.Pid, "Тестовый Герой");
        TypeField(game.Pid, "TST");
        TypeField(game.Pid, "Проверочный герой для e2e-теста.");
        GameConsole.SendKey(game.Pid, ConsoleKey.F3);   // «ДАЛЕЕ» анкеты — то же, что [F3] (кнопка-плашка без стрелок)

        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ РЕДАКТОР ←", 20000),
            "Не дождались экрана редактора персонажа — CreateNewGame (мок) не отработал.");
        Thread.Sleep(200);
    }

    // "Тестовый меч" экипирован (equipmentedSlot:["П Рука"]) — его название встречается на экране
    // ДВАЖДЫ: в списке слотов экипировки ("П Рука   Тестовый меч") и в самом списке инвентаря
    // ("• Тестовый меч: ..."). Ищем именно вторую строку — с двоеточием после названия, которого
    // в строке слота экипировки нет.
    private static string InventoryLineMarker(string itemName) => itemName + ":";

    // Fixtures/MockResponses/CreateNewGame/response.json: "Тестовый меч" — единственный предмет с
    // заданным image ("lorc/broadsword"), специально для этого теста. Мок-герой не имеет своего
    // hero.image, поэтому правая панель до наведения показывает дефолтный D20 (DiceArt.D20,
    // см. DialogDisplay.GetDefaultRightPanelLines) — любое изменение содержимого панели после
    // наведения означает, что картинка предмета реально отрисовалась.
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

        // RerenderRightPanel вызывается отдельно от подсветки строки (тот же паттерн, что и
        // перерисовка карточки после ответа мока в других тестах) — опрашиваем, а не спим фиксированно.
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

    // Tab должен циклически выбирать предметы инвентаря (как на геймплейном экране Персонажа) —
    // маркер «←» должен переходить с одного предмета на другой. Раньше onTab был не передан:
    // Tab не делал ВООБЩЕ ничего для выбора, и (хуже) улетал как буквальный текст "Tab" в
    // FixHeroForNewGame — проверяем и то, что маркер реально движется, и что в истории диалога
    // не появляется сообщение "Tab" от игрока.
    [Fact]
    public void TabKey_CyclesInventorySelection_AndIsNotSentAsPlayerText()
    {
        using var game = GameSession.LaunchForNewGame();
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");
        OpenEditor(game);
        _output.WriteLine("Редактор персонажа отрисован.");

        // Меч экипирован (дубль в слотах экипировки — нужен маркер с ":"), факел не экипирован
        // (equipmentedSlot:[], нет дубля — но у него quantity:3, формат "Тестовый факел ×3:",
        // поэтому обычное имя без ":" ищем ему, а не InventoryLineMarker).
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
