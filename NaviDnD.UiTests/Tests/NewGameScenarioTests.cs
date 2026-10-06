using System.Text.Json;
using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: создание нового героя (Меню → [F2] → 5 полей анкеты) → редактор персонажа ([F3]НАЧАТЬ)
// → игра на сгенерированной карте. AI-провайдер замокан (GameSession.LaunchForNewGame) —
// CreateNewGame/StartNewGame берут канонические ответы из Fixtures/MockResponses/, реальная
// нейронка не вызывается ни разу за тест.
public class NewGameScenarioTests
{
    private static readonly (byte r, byte g, byte b) MainBackground = (18, 18, 22);

    private readonly ITestOutputHelper _output;

    public NewGameScenarioTests(ITestOutputHelper output) => _output = output;

    // Esc в любой момент анкеты должен отменить создание героя и вернуть в Меню (ScreenRegistry:
    // Screen.NewGame.Commands["Esc"] → Screen.Menu) — до сих пор не проверялось ни в начале анкеты,
    // ни (что важнее) посреди неё, после того как часть полей уже заполнена. CreateNewGame при этом
    // вообще не должен вызываться (NewGameDisplay.GetFieldInput возвращает null сразу, как только
    // распознаёт "Esc" среди команд экрана, до финального await _aiClient.CreateNewGame(...)).
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
        // Выход из создания игры — с предупреждением, что прогресс не сохранится: [Enter] — выйти.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Прогресс создания новой игры не сохранится", 5000),
            "После [Esc] посреди анкеты не появилось предупреждение о выходе в меню.");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "ГЛАВНОЕ МЕНЮ", 5000),
            "После [Esc] посреди анкеты создания персонажа не вернулись на экран Меню.");
        _output.WriteLine("Экран Меню отрисован — создание героя отменено, CreateNewGame не должен был вызваться.");
        TestPacing.Step();

        // Повторное открытие анкеты должно начинаться с чистого листа — введённый ранее текст
        // не должен просочиться в новую попытку (регрессия против утечки состояния между анкетами).
        _output.WriteLine("Открываю анкету заново — проверяю, что поля пустые (нет утечки предыдущего ввода)...");
        Assert.True(Navigation.ToAnketa(game.Pid),
            "Не удалось открыть анкету создания персонажа повторно после отмены.");

        // Заголовок (строка 1) обновляется первым — тело анкеты может дорисоваться на один poll-тик
        // позже (тот же класс гонки, что и в CheckEditorScreen выше) — ждём, а не проверяем один снимок.
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

        // AI-ответ замокан и не зависит от введённого текста — но реальная анкета должна
        // реально принимать ввод (кириллица, Enter между полями), иначе тест не проверяет UI.
        TypeField(game.Pid, "Тестовый Герой");
        TypeField(game.Pid, "TST");
        TypeField(game.Pid, "Проверочный герой для e2e-теста создания игры.");
        // Анкета не создаёт героя по последнему полю — дальше цвет/портрет и кнопка.
        GameConsole.SendKey(game.Pid, ConsoleKey.F3);   // «ДАЛЕЕ» анкеты — то же, что [F3] (кнопка-плашка без стрелок)

        // SelectiveMockProvider намеренно задерживает мок-ответ на 5с (MockResponseDelayMs) —
        // это единственный способ реально проверить, что спиннер ожидания (Spinner.While) в
        // течение этого времени отрисован, а не просто предположить, что он есть.
        _output.WriteLine("Проверяю, что во время ожидания CreateNewGame (мок, искусственная задержка 5с) виден спиннер «Создаём персонажа»...");
        if (!GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Создаём персонажа", 3000))
            failures.Add("Спиннер «Создаём персонажа» не появился во время ожидания ответа CreateNewGame (мок).");

        _output.WriteLine("Жду ответ CreateNewGame (мок) и экран редактора персонажа...");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ РЕДАКТОР ←", 20000),
            "Не дождались экрана редактора персонажа после заполнения анкеты — CreateNewGame (мок) не отработал.");
        // Заголовок (строка 1) обновляется первым — карточка героя ниже может дорисоваться
        // на один poll-тик позже; без паузы здесь ReadRows иногда ловит недорисованную карточку.
        Thread.Sleep(200);
        _output.WriteLine("Экран редактора персонажа отрисован.");
        CheckEditorScreen(game.Pid, failures);
        TestPacing.Step();

        _output.WriteLine("[редактор персонажа] Проверяю переключение подвкладок [F6]/[F7] (Инвентарь/Способности)...");
        CheckEditorTabSwitching(game.Pid, failures);
        TestPacing.Step();

        CheckFixHeroEditWithAskPlayer(game, failures);
        TestPacing.Step();

        // Settle-пауза: FixHeroForNewGame+ask_player только что прогнали SpinAsync/цикл выбора
        // варианта — внешнему игровому циклу (Program.cs) нужен момент вернуться в состояние
        // ожидания ввода на экране редактора, прежде чем слать следующую клавишу (тот же класс
        // гонки, что уже встречался при чтении карточки героя сразу после ответа мока).
        Thread.Sleep(500);
        _output.WriteLine("Отправляю [F3] (НАЧАТЬ) → шаг «Приключение» → НАЧАТЬ ИГРУ...");
        Assert.True(Navigation.StartFromEditor(game.Pid), "После [F3] в редакторе не открылся шаг «Приключение».");

        _output.WriteLine("Проверяю, что во время ожидания StartNewGame (мок, искусственная задержка 5с) виден спиннер «Создаём мир»...");
        bool sawSpinner = GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Создаём мир", 3000);
        if (!sawSpinner)
        {
            // Изредка [F3] не долетает с первого раза — но ТОЛЬКО при прогоне ВСЕГО набора тестов
            // подряд (5+ поднятых-и-убитых процессов игры перед этим), никогда в изолированном
            // прогоне; экран при этом стабильно возвращается в корректное состояние редактора
            // (не куда-то в сторону) — похоже на гонку в самом автоматизаторе (посылка клавиши
            // через WriteConsoleInput под нагрузкой), а не баг игры. Повторяем нажатие один раз.
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
        // Финальный Step() — до Assert, а не после: если проверки провалились, Assert.True ниже
        // бросит исключение и код после него не выполнится, а пауза для отладки нужна в любом случае.
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

    // ВАЖНО: [F2] в EditorScreen ведёт на Screen.NewGameAbilities/AbilityDisplay — это НАСТОЯЩАЯ
    // заглушка (см. AbilityDisplay.Draw(): рисует только рамку с заголовком "СПУТНИК", контента
    // нет вообще — BuildAbilityLines существует, но нигде не вызывается). Реальные подвкладки
    // Инвентарь/Способности — это F6/F7 ВНУТРИ экрана Screen.NewGameCharacter (HeroDisplay,
    // тот же механизм, что и в геймплейном экране Персонажа — см. GameScenarioTests). Проверяем
    // именно их: переключение подвкладки должно менять видимый контент карточки.
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

    // Проверяет, что во время редактирования персонажа (FixHeroForNewGame, отдельное от
    // SendAction действие) механизм ask_player (диалог вопроса от ИИ) работает так же корректно —
    // GameAiClient.SpinAsync поллит ask_player_request.json одинаково для ЛЮБОГО действия,
    // не только для геймплейного SendAction (см. InteractiveResponseTests).
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
        // Короткий таймаут намеренно: ответ должен появиться СРАЗУ (DialogDisplay.
        // PlayAskPlayerRequest перерисовывает диалог немедленно после выбора), а не только
        // когда FixHeroForNewGame (мок, искусственная задержка 5с) полностью завершится —
        // широкое окно замаскировало бы регрессию немедленной перерисовки. Ищем именно
        // "Test Hero: <ответ>" (с префиксом автора) — голый текст варианта ещё виден в САМОМ
        // СПИСКЕ ВАРИАНТОВ независимо от того, попал ли ответ в историю, и дал бы ложное срабатывание.
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

        // НЕ "sleep фиксированное время + один снимок": диалоговая панель (где только что
        // мелькнуло "плащ" в реплике DM) и карточка героя слева перерисовываются РАЗНЫМИ вызовами
        // в РАЗНОЕ время — карточка обновляется только на следующей итерации внешнего игрового
        // цикла. Под нагрузкой (весь набор тестов подряд) фиксированной паузы иногда не хватает —
        // поэтому опрашиваем экран до появления итогового предмета, а не гадаем с Thread.Sleep.
        // Только начало названия: слот экипировки в карточке обрезает длинное имя с «...»
        // (HeroDisplay.FitLine); с заглавной — в реплике ДМ оно со строчной «элегантный плащ».
        // Ждём долго: патч применяется после того, как реплика ДМ допечатается посимвольно.
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

        // Из мок-ответа StartNewGame (Fixtures/MockResponses/StartNewGame/response.json) —
        // символ героя, символ и объект должны быть реально отрисованы на сетке.
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

        // Правка из FixHeroForNewGame (сделана ДО StartNewGame) должна пережить Storage.Save()
        // и перерисовку на этом экране — иначе редактирование персонажа теряется при старте игры.
        _output.WriteLine($"[{context}] Проверяю, что правка из FixHeroForNewGame (Элегантный плащ) сохранилась...");
        if (!rows.Any(r => r.Contains("Элегантный плащ")))
            failures.Add($"[{context}] Предмет «Элегантный плащ» из FixHeroForNewGame не пережил StartNewGame/Storage.Save().");
        TestPacing.Step();
    }

    // Общие для редактора и экрана персонажа проверки содержимого карточки — оба экрана рисуют
    // её тем же HeroDisplay.DrawHeroCard над одним и тем же WorldState.Hero.
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
        // Способности живут на отдельной подвкладке ([F7]/отдельный экран НовойИгры) —
        // не проверяются здесь вместе с инвентарём, который открыт по умолчанию.

        // Цвет героя выбирается в анкете и переносится поверх ответа CreateNewGame — по умолчанию
        // первый в палитре NewGameDisplay («ЯНТАРНЫЙ» [220, 170, 60]), а не [100, 200, 255] из мока.
        ScreenAssertions.CheckTextColor(gamePid, rows, "Test Hero", (220, 170, 60), 60, MainBackground, context, failures);
    }
}
