using System.Text.Json;
using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: creating a new hero (Menu → [F2] → 5 profile fields) → character editor ([F3]START) → playing on the
// generated map. AI-provider is locked (GameSession.LaunchForNewGame) - CreateNewGame/StartNewGame take
// canonical answers from Fixtures/MockResponses/, the real AI is not called even once during the test.
public class NewGameScenarioTests
{
    private static readonly (byte r, byte g, byte b) MainBackground = (18, 18, 22);

    private readonly ITestOutputHelper _output;

    public NewGameScenarioTests(ITestOutputHelper output) => _output = output;

    // Esc at any time in the questionnaire must cancel the creation of the hero and return to the Menu
    // (ScreenRegistry: Screen.NewGame.Commands["Esc"] → Screen.Menu) - has not yet been checked either at the
    // beginning of the questionnaire, or (more importantly) in the middle of it, after some of the fields have
    // already been filled in. CreateNewGame should not be called at all (NewGameDisplay.GetFieldInput returns
    // null as soon as it recognizes "Esc" among the screen commands, before the final await
    // _aiClient.CreateNewGame(...)).
    [Fact]
    public void EscMidAnketa_CancelsCreation_ReturnsToMenu()
    {
        using var game = GameSession.LaunchForNewGame();
        _output.WriteLine($"Игра запущена (pid {game.Pid}), экран Меню отрисован.");

        _output.WriteLine("Отправляю [F2] — жду анкету создания персонажа...");
        Assert.True(Navigation.ToAnketa(game.Pid),
            "Не дождались анкеты создания персонажа после нажатия [F2] в меню.");
        TestPacing.Step();

        _output.WriteLine("Заполняю первые два поля анкеты (имя, символ), затем отменяю Esc посреди третьего...");
        TypeField(game.Pid, "Тестовый Герой Для Отмены");
        TypeField(game.Pid, "ESC");
        GameConsole.SendKey(game.Pid, ConsoleKey.Escape);
        // Exiting game creation - with a warning that progress will not be saved: [Enter] - exit.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Прогресс создания новой игры не сохранится", 5000),
            "После [Esc] посреди анкеты не появилось предупреждение о выходе в меню.");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "ГЛАВНОЕ МЕНЮ", 5000),
            "После [Esc] посреди анкеты создания персонажа не вернулись на экран Меню.");
        _output.WriteLine("Экран Меню отрисован — создание героя отменено, CreateNewGame не должен был вызваться.");
        TestPacing.Step();

        // Re-opening a profile should start with a clean slate - previously entered text should not leak into a
        // new attempt (regression against state leakage between profiles).
        _output.WriteLine("Открываю анкету заново — проверяю, что поля пустые (нет утечки предыдущего ввода)...");
        Assert.True(Navigation.ToAnketa(game.Pid),
            "Не удалось открыть анкету создания персонажа повторно после отмены.");

        // The header (line 1) is updated first - the body of the profile can be completed one poll- tick later
        // (the same race class as in CheckEditorScreen above) - we wait, rather than check one snapshot.
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => !r.Any(x => x.Contains("Тестовый Герой Для Отмены")), 3000),
            "Анкета, открытая заново после отмены, всё ещё показывает текст из предыдущей (отменённой) попытки.");
        _output.WriteLine("Анкета открылась заново без следов предыдущего отменённого ввода.");
        TestPacing.Step();
    }

    [Fact]
    public void CreateHeroAndStartNewGame()
    {
        using var game = GameSession.LaunchForNewGame();
        _output.WriteLine($"Игра запущена (pid {game.Pid}), экран Меню отрисован.");
        var failures = new List<string>();

        _output.WriteLine("Отправляю [F2] — жду анкету создания персонажа...");
        Assert.True(Navigation.ToAnketa(game.Pid),
            "Не дождались анкеты создания персонажа после нажатия [F2] в меню.");
        _output.WriteLine("Анкета отрисована. Заполняю 5 полей (имя/символ/описание/сеттинг/пожелание)...");

        // AI-the answer is locked and does not depend on the entered text - but a real questionnaire must
        // actually accept input (Cyrillic, Enter between fields), otherwise the test does not check UI.
        TypeField(game.Pid, "Тестовый Герой");
        TypeField(game.Pid, "TST");
        TypeField(game.Pid, "Проверочный герой для e2e-теста создания игры.");
        // The profile does not create a hero in the last field - then color/portrait and a button.
        GameConsole.SendKey(game.Pid, ConsoleKey.F3);   // “NEXT” of the questionnaire - the same as [F3] (blank button without arrows)

        // SelectiveMockProvider deliberately delays the mock response by 5s (MockResponseDelayMs)) - this is
        // the only way to actually check that the wait spinner (Spinner.While)) is drawn during this time, and
        // not just assume that it is.
        _output.WriteLine("Проверяю, что во время ожидания CreateNewGame (мок, искусственная задержка 5с) виден спиннер «Создаём персонажа»...");
        if (!GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Создаём персонажа", 3000))
            failures.Add("Спиннер «Создаём персонажа» не появился во время ожидания ответа CreateNewGame (мок).");

        _output.WriteLine("Жду ответ CreateNewGame (мок) и экран редактора персонажа...");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ РЕДАКТОР ←", 20000),
            "Не дождались экрана редактора персонажа после заполнения анкеты — CreateNewGame (мок) не отработал.");
        // The header (line 1) is updated first - the hero card below may be completed one poll- tick later;
        // without a pause here ReadRows sometimes catches a half-drawn card.
        Thread.Sleep(200);
        _output.WriteLine("Экран редактора персонажа отрисован.");
        CheckEditorScreen(game.Pid, failures);
        TestPacing.Step();

        _output.WriteLine("[редактор персонажа] Проверяю переключение подвкладок [F6]/[F7] (Инвентарь/Способности)...");
        CheckEditorTabSwitching(game.Pid, failures);
        TestPacing.Step();

        CheckFixHeroEditWithAskPlayer(game, failures);
        TestPacing.Step();

        // Settle-pause: FixHeroForNewGame+ask_player just ran the SpinAsync/ option selection loop - the
        // external game loop (Program.cs) needs a moment to return to the state of waiting for input on the
        // editor screen before sending the next key (the same race class that was already encountered when
        // reading the hero card immediately after the mock answer).
        Thread.Sleep(500);
        _output.WriteLine("Отправляю [F3] (НАЧАТЬ) → шаг «Приключение» → НАЧАТЬ ИГРУ...");
        Assert.True(Navigation.StartFromEditor(game.Pid), "После [F3] в редакторе не открылся шаг «Приключение».");

        _output.WriteLine("Проверяю, что во время ожидания StartNewGame (мок, искусственная задержка 5с) виден спиннер «Создаём мир»...");
        bool sawSpinner = GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Создаём мир", 3000);
        if (!sawSpinner)
        {
            // Occasionally [F3] does not reach the target the first time - but ONLY when running the ENTIRE set
            // of tests in a row (5+ raised-and-killed game processes before that), never in an isolated run; At
            // the same time, the screen stably returns to the correct state of the editor (not somewhere to the
            // side) - it looks like a race in the automation itself (sending a key through WriteConsoleInput
            // under load), and not a game bug. Repeat pressing once.
            _output.WriteLine("Спиннер не появился — повторяю Enter на шаге «Приключение»...");
            GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
            sawSpinner = GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Создаём мир", 5000);
        }
        if (!sawSpinner)
            failures.Add("Спиннер «Создаём мир» не появился во время ожидания ответа StartNewGame (мок), даже после повтора [F3].");

        _output.WriteLine("Жду экран Карты...");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 12000),
            "Не дождались экрана Карты после [F3] — StartNewGame (мок) не отработал.");
        Thread.Sleep(200);
        _output.WriteLine("Экран Карты отрисован (мир сгенерирован из мок-ответа StartNewGame).");
        CheckMapScreen(game.Pid, failures);
        TestPacing.Step();

        _output.WriteLine("Отправляю [F4] — жду экран Персонажа, проверяю итоговое состояние героя...");
        GameConsole.SendKey(game.Pid, ConsoleKey.F4);
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ ПЕРСОНАЖ ←", 5000),
            "Не дождались экрана Персонажа после [F4] на карте.");
        Thread.Sleep(200);
        CheckCharacterScreen(game.Pid, failures);
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

    private static void TypeField(int gamePid, string text)
    {
        GameConsole.SendText(gamePid, text);
        GameConsole.SendKey(gamePid, ConsoleKey.Enter);
        Thread.Sleep(150);
    }

    private void CheckEditorScreen(int gamePid, List<string> failures)
    {
        const string context = "редактор персонажа";

        _output.WriteLine($"[{context}] Проверяю вкладки [F2]/[F3]/[Esc]...");
        string title = GameConsole.ReadRow(gamePid, 1, GameConsole.TitleReadWidth);
        ScreenAssertions.RequireTab(title, "[F4]", "ПРИКЛЮЧЕНИЕ", context, failures);
        ScreenAssertions.RequireTab(title, "[Esc]", "МЕНЮ", context, failures);
        TestPacing.Step();

        _output.WriteLine($"[{context}] Проверяю рамки...");
        var rows = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        ScreenAssertions.CheckBorders(rows, context, failures);
        TestPacing.Step();

        _output.WriteLine($"[{context}] Проверяю карточку созданного героя (имя, hp, инвентарь)...");
        CheckMockedHeroCard(gamePid, rows, context, failures);
        TestPacing.Step();
    }

    // IMPORTANT: [F2] in EditorScreen leads to Screen.NewGameAbilities/AbilityDisplay - this is a REAL
    // placeholder (see AbilityDisplay.Draw(): draws only a frame with the title "SATELLITE", there is no
    // content at all - BuildAbilityLines exists, but is not called anywhere). The actual Inventory/Abilities
    // sub-tabs are F6/F7 INSIDE the Screen.NewGameCharacter screen (HeroDisplay, the same mechanism as in the
    // Character gameplay screen - see GameScenarioTests). These are the ones we are checking: switching the
    // sub-tab should change the visible content of the card.
    private void CheckEditorTabSwitching(int gamePid, List<string> failures)
    {
        const string context = "редактор персонажа";

        GameConsole.SendKey(gamePid, ConsoleKey.F7);
        if (!GameConsole.WaitForAnyRowContains(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "→ СПОСОБНОСТИ ←", 3000))
        {
            failures.Add($"[{context}] Не удалось переключиться на подвкладку [F7] (Способности) в карточке героя.");
            return;
        }
        var abilityRows = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        if (!abilityRows.Any(r => r.Contains("Тестовая способность 1")))
            failures.Add($"[{context}] После переключения на подвкладку [F7] способности из мок-ответа CreateNewGame не отображаются.");

        GameConsole.SendKey(gamePid, ConsoleKey.F6);
        if (!GameConsole.WaitForAnyRowContains(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "→ ИНВЕНТАРЬ ←", 3000))
        {
            failures.Add($"[{context}] Не удалось вернуться на подвкладку [F6] (Инвентарь) в карточке героя.");
            return;
        }
        var invRows = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        if (!invRows.Any(r => r.Contains("Тестовый меч")))
            failures.Add($"[{context}] После возврата на подвкладку [F6] инвентарь из мок-ответа CreateNewGame не отображается.");
    }

    // Checks that during character editing (FixHeroForNewGame, a separate action from SendAction), the
    // ask_player mechanism (question dialogue from the AI) works as correctly - GameAiClient.SpinAsync polls
    // ask_player_request.json the same for ANY action, not just for gameplay SendAction (see
    // InteractiveResponseTests).
    private void CheckFixHeroEditWithAskPlayer(GameSession game, List<string> failures)
    {
        const string context = "редактирование персонажа (FixHeroForNewGame)";

        _output.WriteLine("Отправляю запрос на редактирование персонажа — жду начала ожидания FixHeroForNewGame (мок)...");
        GameConsole.SendText(game.Pid, "добавь мне элегантный плащ, но сначала спроси о моих планах");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
        Thread.Sleep(150);

        string askPlayerPath = Path.Combine(game.RealStorageDir, "ask_player_request.json");
        string[] options = ["Исследовать подземелье", "Сражаться", "Договориться с обитателями"];
        var askRequest = new { history = new[] { new { text = "Каковы твои планы?" } }, options };
        _output.WriteLine("Пишу ask_player_request.json — проверяю, что вопрос отображается и при редактировании персонажа...");
        File.WriteAllText(askPlayerPath, JsonSerializer.Serialize(askRequest));

        foreach (var option in options)
            if (!GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, option, 4000))
            {
                failures.Add($"[{context}] Вариант ответа «{option}» не отображён в диалоге вопроса.");
                return;
            }
        _output.WriteLine("Вопрос при редактировании отображён — выбираю первый вариант...");

        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
        // The short timeout is intentional: the response should appear IMMEDIATELY (DialogDisplay.
        // PlayAskPlayerRequest redraws the dialog immediately after selection), and not just when
        // FixHeroForNewGame (mock, artificial delay 5s) is completely completed - a wide window would mask the
        // regression of the immediate redraw. We are looking specifically for “Test Hero: <answer>” (with the
        // author’s prefix) - the bare text of the option is still visible in the LIST OF OPTIONS ITSELF,
        // regardless of whether the answer was included in the story and would give a false positive.
        if (!GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, $"Test Hero: {options[0]}", 2000))
        {
            failures.Add($"[{context}] Выбранный вариант ответа не появился в истории диалога СРАЗУ после выбора.");
            return;
        }

        _output.WriteLine("Жду ответ FixHeroForNewGame (мок)...");
        if (!GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "плащ", 15000))
        {
            failures.Add($"[{context}] Не дождались ответа FixHeroForNewGame (мок) после ответа на вопрос.");
            return;
        }

        // NOT "sleep fixed time + one snapshot": the dialog panel (where the "cloak" in the replica DM) just
        // flashed and the hero card on the left is redrawn by DIFFERENT calls at DIFFERENT times - the card is
        // updated only at the next iteration of the external game loop. Under load (the entire set of tests in
        // a row), a fixed pause is sometimes not enough - so we poll the screen until the final item appears,
        // instead of guessing with Thread.Sleep. Only the beginning of the name: the equipment slot in the card
        // cuts off the long name with “...” (HeroDisplay.FitLine); with a capital one - in the DM’s replica it
        // is with a lowercase “elegant cloak”. We wait a long time: the patch is applied after the DM’s replica
        // is printed character by character.
        if (!GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Элегантный", 20000))
            failures.Add($"[{context}] Предмет «Элегантный плащ», добавленный мок-ответом FixHeroForNewGame, не найден на экране.");
    }

    private void CheckMapScreen(int gamePid, List<string> failures)
    {
        const string context = "экран карты (новая игра)";

        _output.WriteLine($"[{context}] Проверяю вкладки [F2]/[F3]/[Esc]...");
        string title = GameConsole.ReadRow(gamePid, 1, GameConsole.TitleReadWidth);
        ScreenAssertions.RequireTab(title, "[F2]", "МИР", context, failures);
        ScreenAssertions.RequireTab(title, "[F3]", "ЖУРНАЛ", context, failures);
        ScreenAssertions.RequireTab(title, "[F4]", "ПЕРСОНАЖ", context, failures);
        ScreenAssertions.RequireTab(title, "[F5]", "СПУТНИК", context, failures);
        ScreenAssertions.RequireTab(title, "[Esc]", "МЕНЮ", context, failures);
        TestPacing.Step();

        _output.WriteLine($"[{context}] Проверяю рамки...");
        var rows = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        ScreenAssertions.CheckBorders(rows, context, failures);
        TestPacing.Step();

        _output.WriteLine($"[{context}] Проверяю номера колонок сетки...");
        if (!rows.Any(r => r.Contains(" 1 ") || r.TrimEnd().EndsWith(" 1")))
            failures.Add($"[{context}] На экране карты не найдены номера колонок сетки.");
        TestPacing.Step();

        // From the mock answer StartNewGame (Fixtures/MockResponses/StartNewGame/response.json) - the hero
        // symbol, the symbol and the object must be actually drawn on the mesh.
        _output.WriteLine($"[{context}] Проверяю символы героя (TST), нпс (NPC) и объекта ([C]) на сетке...");
        if (!rows.Any(r => r.Contains("TST")))
            failures.Add($"[{context}] Символ героя «TST» не найден на карте.");
        if (!rows.Any(r => r.Contains("NPC")))
            failures.Add($"[{context}] Символ нпс «NPC» не найден на карте.");
        if (!rows.Any(r => r.Contains("[C]")))
            failures.Add($"[{context}] Символ объекта «[C]» не найден на карте.");
        TestPacing.Step();
    }

    private void CheckCharacterScreen(int gamePid, List<string> failures)
    {
        const string context = "экран персонажа (новая игра)";

        var rows = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        _output.WriteLine($"[{context}] Проверяю, что герой из CreateNewGame (мок) сохранился и виден после StartNewGame (Storage.Save)...");
        CheckMockedHeroCard(gamePid, rows, context, failures);

        // Edit from FixHeroForNewGame (made BEFORE StartNewGame) must survive Storage.Save() and redraw on this
        // screen - otherwise the character edit is lost when the game starts.
        _output.WriteLine($"[{context}] Проверяю, что правка из FixHeroForNewGame (Элегантный плащ) сохранилась...");
        if (!rows.Any(r => r.Contains("Элегантный плащ")))
            failures.Add($"[{context}] Предмет «Элегантный плащ» из FixHeroForNewGame не пережил StartNewGame/Storage.Save().");
        TestPacing.Step();
    }

    // Common card content checks for the editor and character screen - both screens draw it with the same
    // HeroDisplay.DrawHeroCard above the same WorldState.Hero.
    private void CheckMockedHeroCard(int gamePid, List<string> rows, string context, List<string> failures)
    {
        if (!rows.Any(r => r.Contains("Test Hero")))
            failures.Add($"[{context}] Имя героя «Test Hero» (из мок-ответа CreateNewGame) не найдено на экране.");
        if (!rows.Any(r => r.Contains("10/10")))
            failures.Add($"[{context}] HP героя «10/10» не найдено на экране.");
        if (!rows.Any(r => r.Contains("Тестовый меч")))
            failures.Add($"[{context}] Предмет «Тестовый меч» не найден в инвентаре.");
        if (!rows.Any(r => r.Contains("Тестовый факел")))
            failures.Add($"[{context}] Предмет «Тестовый факел» не найден в инвентаре.");
        // Abilities live on a separate subtab ([F7]/ separate NewGame screen) - they are not checked here along
        // with the inventory, which is open by default.

        // The hero's color is selected in the questionnaire and transferred over the answer CreateNewGame - by
        // default the first one in the palette is NewGameDisplay ("AMBER" [220, 170, 60]), and not [100, 200,
        // 255] from the mock.
        ScreenAssertions.CheckTextColor(gamePid, rows, "Test Hero", (220, 170, 60), 60, MainBackground, context, failures);
    }
}
