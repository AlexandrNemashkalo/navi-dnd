using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// Each test case raises ITS independent NaviDnD.exe through GameSession.Launch() - cases do not depend on each
// other and do not reuse the game state.  State - from Fixtures/full_game.json, collected from a real game save
// (hero + dungeon map), and not made up from scratch - so that checks cover a realistic amount of data (full
// stats/skills sheet, 9 inventory items, map for 6 rooms/10 doors/4 creatures).  _output.WriteLine writes to
// the test log (visible in `dotnet test` with -v n/detailed, in Test Explorer - always) - so from the log you
// can see WHAT exactly was checked, and not just the final PASS/FAIL from the accumulated list failures.
public class GameScenarioTests
{
    private readonly ITestOutputHelper _output;

    public GameScenarioTests(ITestOutputHelper output) => _output = output;

    // Scenario: Menu → [F1] → Card (check frames and tabs) → click on [F3] → Character, Inventory tab (card +
    // dialog box with story text, hovering over tab, pagination) → [F7] → Abilities tab (card + pagination).
    [Fact]
    public void FullGameplayScenario()
    {
        using var game = GameSession.Launch();
        _output.WriteLine($"Игра запущена (pid {game.Pid}), экран Меню отрисован.");
        var failures = new List<string>();

        const string MapActiveMarker = "→ КАРТА ←";
        const string CharacterActiveMarker = "→ ПЕРСОНАЖ ←";

        _output.WriteLine("Отправляю [F1] — жду экран Карты...");
        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, MapActiveMarker, 5000),
            "Не дождались экрана Карты после нажатия [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован.");
        CheckMapScreen(game.Pid, failures);
        TestPacing.Step();

        string mapTitle = GameConsole.ReadRow(game.Pid, 1, GameConsole.TitleReadWidth);
        var f2Tab = ScreenAssertions.FindTab(mapTitle, "[F4]");
        Assert.NotNull(f2Tab); // otherwise there is nowhere else to click - we report via explicit Assert, and not failures

        _output.WriteLine("Кликаю мышью по вкладке [F4] — жду экран Персонажа...");
        GameConsole.SendLeftClick(game.Pid, f2Tab!.Value.startX, 1);
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, CharacterActiveMarker, 5000),
            "Не дождались экрана Персонажа после клика мышью по вкладке [F4].");
        // The screen title (line 1) and the card body (dialog box, inventory) are drawn by DIFFERENT calls -
        // the title could have changed before the dialog box below was drawn (see the same race that was fixed
        // in DialogDisplay.Draw()). We are clearly waiting for the card body, and not relying on the fact that
        // it had already been drawn by the time the title was changed - otherwise CheckCharacterScreen below
        // reads the screen ahead of time and does not find the "DIALOG BOX"/inventory contents.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "ДИАЛОГОВОЕ ОКНО", 5000),
            "Диалоговое окно не отрисовалось после перехода на экран Персонажа.");
        _output.WriteLine("Экран Персонажа отрисован (вкладка Инвентарь).");
        CheckCharacterScreen(game.Pid, expectAbilities: false, failures);
        TestPacing.Step();

        CheckTopTabHoverEffects(game.Pid, "вкладка Инвентарь", failures);
        TestPacing.Step();
        CheckInventoryPagination(game.Pid, "вкладка Инвентарь", failures);
        TestPacing.Step();

        _output.WriteLine("Отправляю [F7] — жду подвкладку Способности...");
        GameConsole.SendKey(game.Pid, ConsoleKey.F7);
        // "→ CHARACTER ←" does not change when changing the sub-tab (this is the SCREEN title, not the sub-tab)
        // - you need to wait for the active SUB-TAB marker (drawn inside the card, not in line 1), otherwise
        // the check will take place instantly and without confirmation that [F7] was processed at all.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "→ СПОСОБНОСТИ ←", 5000),
            "Подвкладка Способности не активировалась после нажатия [F7].");
        _output.WriteLine("Подвкладка Способности отрисована.");
        CheckCharacterScreen(game.Pid, expectAbilities: true, failures);
        TestPacing.Step();
        CheckAbilitiesPagination(game.Pid, "подвкладка Способности", failures);
        TestPacing.Step();

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

    private void CheckMapScreen(int gamePid, List<string> failures)
    {
        _output.WriteLine("[экран карты] Проверяю вкладки [F2]/[F3]/[F4]/[F5]/[Esc]...");
        string title = GameConsole.ReadRow(gamePid, 1, GameConsole.TitleReadWidth);
        ScreenAssertions.RequireTab(title, "[F2]", "МИР", "экран карты", failures);
        ScreenAssertions.RequireTab(title, "[F3]", "ЖУРНАЛ", "экран карты", failures);
        ScreenAssertions.RequireTab(title, "[F4]", "ПЕРСОНАЖ", "экран карты", failures);
        ScreenAssertions.RequireTab(title, "[F5]", "СПУТНИК", "экран карты", failures);
        ScreenAssertions.RequireTab(title, "[Esc]", "МЕНЮ", "экран карты", failures);
        TestPacing.Step();

        _output.WriteLine("[экран карты] Проверяю рамки...");
        var rows = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        ScreenAssertions.CheckBorders(rows, "экрана карты", failures);
        TestPacing.Step();

        _output.WriteLine("[экран карты] Проверяю номера колонок сетки...");
        // Map column headings (consecutive numbers) are a sign that the grid is actually drawn and not just an
        // empty frame. Numbers are the coordinates of the field under the camera, not necessarily starting with
        // 1.
        if (!rows.Any(r => System.Text.RegularExpressions.Regex.Matches(r, @"\b\d{1,3}\b").Count >= 10))
            failures.Add("На экране карты не найдены номера колонок сетки.");
        TestPacing.Step();
    }

    private void CheckCharacterScreen(int gamePid, bool expectAbilities, List<string> failures)
    {
        string context = expectAbilities ? "подвкладка Способности" : "вкладка Инвентарь";

        _output.WriteLine($"[{context}] Проверяю вкладки [F1]/[F5]/[Esc]...");
        string title = GameConsole.ReadRow(gamePid, 1, GameConsole.TitleReadWidth);
        ScreenAssertions.RequireTab(title, "[F1]", "КАРТА", context, failures);
        ScreenAssertions.RequireTab(title, "[F5]", "СПУТНИК", context, failures);
        ScreenAssertions.RequireTab(title, "[Esc]", "МЕНЮ", context, failures);
        TestPacing.Step();

        _output.WriteLine($"[{context}] Проверяю рамки...");
        var rows = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        ScreenAssertions.CheckBorders(rows, context, failures);
        TestPacing.Step();

        _output.WriteLine($"[{context}] Проверяю имя героя, диалоговое окно и текст истории (реплики ДМ и героя)...");
        bool sawHeroName = rows.Any(r => r.Contains("Рустем"));
        bool sawDialogWindow = rows.Any(r => r.Contains("ДИАЛОГОВОЕ ОКНО"));
        // From the fixture (Fixtures/full_game.json → history) - we check that the real text of the story
        // actually reaches the screen, and not just the dialog box frame.
        bool sawDmMessage = rows.Any(r => r.Contains("факелы едва потрескивают"));
        bool sawHeroMessage = rows.Any(r => r.Contains("Осматриваюсь"));

        if (!sawHeroName) failures.Add($"[{context}] Имя героя «Рустем» не найдено на экране.");
        if (!sawDialogWindow) failures.Add($"[{context}] Диалоговое окно («ДИАЛОГОВОЕ ОКНО») не найдено.");
        if (!sawDmMessage) failures.Add($"[{context}] Текст реплики DM из истории не найден.");
        if (!sawHeroMessage) failures.Add($"[{context}] Текст реплики героя из истории не найден.");
        TestPacing.Step();

        if (expectAbilities)
        {
            _output.WriteLine($"[{context}] Проверяю список способностей...");
            // From the fixture (hero.abilities) - several different abilities should be visible at the same
            // time (checks that the list is not cut off after one entry). "Second Wind" is deliberately not
            // included here - it is both an ability and a resource AT THE SAME TIME (hero.resources), and
            // resources are always visible at the top of the card regardless of the subtab - a match on this
            // name does not prove anything.
            string[] abilityNames = ["Боевой стиль: Стрельба", "Фейское наследие", "Обострённые чувства"];
            foreach (var name in abilityNames)
                if (!rows.Any(r => r.Contains(name)))
                    failures.Add($"[{context}] Способность «{name}» не найдена на экране.");
        }
        else
        {
            _output.WriteLine($"[{context}] Проверяю подвкладку ИНВЕНТАРЬ и предметы (включая количество)...");
            if (!rows.Any(r => r.Contains("ИНВЕНТАРЬ")))
                failures.Add($"[{context}] Подвкладка «ИНВЕНТАРЬ» не найдена.");

            // From the fixture (hero.inventory) - several different objects must be visible at the same time,
            // including the quantity (×38 for arrows) - checks the column markings.
            string[] itemNames = ["Длинный лук", "Кольчуга", "Стрелы"];
            foreach (var name in itemNames)
                if (!rows.Any(r => r.Contains(name)))
                    failures.Add($"[{context}] Предмет «{name}» не найден на экране.");
            if (!rows.Any(r => r.Contains("×38")))
                failures.Add($"[{context}] Количество стрел (×38) не найдено — возможна проблема с отступами колонки.");
        }
        TestPacing.Step();

        CheckHeroNameColor(gamePid, rows, context, failures);
        TestPacing.Step();
    }

    // hero.color from Fixtures/full_game.json is the expected color of the hero's name.
    private static readonly (int r, int g, int b) HeroNameColor = (80, 160, 120);
    // DisplayConfig.MainBackground by default - what to compare with to find the “ink” of the text.
    private static readonly (byte r, byte g, byte b) MainBackground = (18, 18, 22);
    private const int ColorTolerance = 60;

    private void CheckHeroNameColor(int gamePid, List<string> rows, string context, List<string> failures)
    {
        if (!rows.Any(r => r.Contains("Рустем"))) return; // the very fact of missing text is already reported by a separate check
        _output.WriteLine($"[{context}] Проверяю цвет имени героя (скриншот + сэмплинг пикселя)...");
        ScreenAssertions.CheckTextColor(gamePid, rows, "Рустем", HeroNameColor, ColorTolerance, MainBackground, context, failures);
    }

    // Hovering over a clickable element should change color (MouseUiHelper.SetTabHighlight) - we check on the
    // inactive tab [F1] (on the character screen this tab now does nothing when hovering except highlighting -
    // we compare the BEFORE/AFTER state, and not the specific expected color, this is more reliable). Cursor
    // (ConsoleMouseReader.SetCursorShape) we don’t check: this is a real system cursor (GetCursorInfo), which
    // depends on where the real mouse pointer is physically located in the OS, and not on synthetic events
    // WriteConsoleInput - the highlighting is called by the same hover handler and reliably confirms its
    // firing.
    private const int HoverColorDelta = 10;

    private void CheckTopTabHoverEffects(int gamePid, string context, List<string> failures)
    {
        string title = GameConsole.ReadRow(gamePid, 1, GameConsole.TitleReadWidth);
        var tab = ScreenAssertions.FindTab(title, "[F1]");
        if (tab == null) return; // missing tab already reported RequireTab

        _output.WriteLine($"[{context}] Проверяю смену цвета вкладки [F1] при наведении мышью...");

        (byte r, byte g, byte b) background = (18, 18, 22);
        int sampleCol = tab.Value.startX + 1;

        // Neutral point away from any tabs/arrows - reset guidance before measuring.
        GameConsole.SendMouseMove(gamePid, 2, 10);
        Thread.Sleep(250);

        (byte r, byte g, byte b) colorBefore;
        try { colorBefore = GameConsole.SampleCellColor(gamePid, sampleCol, 1, background); }
        catch (Exception ex) { failures.Add($"[{context}] Не удалось снять цвет вкладки [F1] до наведения: {ex.Message}"); return; }

        GameConsole.SendMouseMove(gamePid, sampleCol, 1);
        Thread.Sleep(300);

        (byte r, byte g, byte b) colorAfter;
        try { colorAfter = GameConsole.SampleCellColor(gamePid, sampleCol, 1, background); }
        catch (Exception ex) { failures.Add($"[{context}] Не удалось снять цвет вкладки [F1] после наведения: {ex.Message}"); return; }

        bool colorChanged = Math.Abs(colorBefore.r - colorAfter.r) > HoverColorDelta
                         || Math.Abs(colorBefore.g - colorAfter.g) > HoverColorDelta
                         || Math.Abs(colorBefore.b - colorAfter.b) > HoverColorDelta;
        if (!colorChanged)
            failures.Add($"[{context}] Цвет вкладки [F1] не изменился при наведении: до ({colorBefore.r},{colorBefore.g},{colorBefore.b}), после ({colorAfter.r},{colorAfter.g},{colorAfter.b}).");
        TestPacing.Step();

        // Move the mouse away so as not to interfere with the next steps of the script.
        GameConsole.SendMouseMove(gamePid, 2, 10);
        Thread.Sleep(150);
    }

    // Fixture (Fixtures/full_game.json) specifically contains many items/abilities with long descriptions - the
    // list does not fit on one page, so the ◄/► arrows must actually scroll through. Marker - the first
    // UN-equipped item: equipped ones (for example, "Longbow") are always visible in the block of equipment
    // slots at the top of the card, regardless of the inventory page, and are not suitable as a page marker.
    private void CheckInventoryPagination(int gamePid, string context, List<string> failures) =>
        CheckPagination(gamePid, context, "Длинный меч", "предмет", failures);

    private void CheckAbilitiesPagination(int gamePid, string context, List<string> failures) =>
        CheckPagination(gamePid, context, "Боевой стиль: Стрельба", "способность", failures);

    private void CheckPagination(int gamePid, string context, string firstMarker, string itemKind, List<string> failures)
    {
        _output.WriteLine($"[{context}] Проверяю пагинацию ({itemKind}): ► вперёд, ◄ назад...");
        var page1 = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);

        if (!page1.Any(r => r.Contains(firstMarker)))
        {
            failures.Add($"[{context}] Первый {itemKind} «{firstMarker}» не найден на первой странице.");
            return;
        }

        int arrowRow = page1.FindIndex(r => r.Contains('►'));
        if (arrowRow < 0)
        {
            failures.Add($"[{context}] Стрелка «►» не найдена — список должен не помещаться на одну страницу (в фикстуре достаточно записей).");
            return;
        }
        int arrowCol = page1[arrowRow].IndexOf('►');

        GameConsole.SendLeftClick(gamePid, arrowCol, arrowRow);
        Thread.Sleep(500);
        TestPacing.Step();

        var page2 = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        if (page2.Any(r => r.Contains(firstMarker)))
            failures.Add($"[{context}] После клика по ► «{firstMarker}» всё ещё виден — страница не сменилась.");

        int backArrowRow = page2.FindIndex(r => r.Contains('◄'));
        if (backArrowRow < 0)
        {
            failures.Add($"[{context}] Стрелка «◄» не найдена после перехода на вторую страницу.");
            return;
        }
        int backArrowCol = page2[backArrowRow].IndexOf('◄');

        GameConsole.SendLeftClick(gamePid, backArrowCol, backArrowRow);
        Thread.Sleep(500);
        TestPacing.Step();

        var page1Again = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        if (!page1Again.Any(r => r.Contains(firstMarker)))
            failures.Add($"[{context}] После клика по ◄ не вернулись на первую страницу — «{firstMarker}» не виден.");
        TestPacing.Step();
    }
}
