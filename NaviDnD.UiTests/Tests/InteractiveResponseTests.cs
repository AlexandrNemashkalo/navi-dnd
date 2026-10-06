using System.Text.Json;
using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: диалог броска кубика и диалог вопроса от ИИ (ask_player) — оба в реальной игре запускает
// GameAiClient.SpinAsync, который поллит roll_request.json/ask_player_request.json в
// NaviDnD/Storage/ ПОКА ждёт ответ SendAction (мок или реальный — не важно). Раз это чтение
// файла, а не сам вызов нейронки, тест может вызвать диалог, просто записав такой файл во время
// искусственной задержки мок-ответа (SelectiveMockProvider.MockResponseDelayMs, 5с) — без единого
// реального обращения к AI.
public class InteractiveResponseTests
{
    private readonly ITestOutputHelper _output;

    public InteractiveResponseTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void DiceRollRequest_DisplaysAndResolves()
    {
        using var game = GameSession.Launch(mockedActions: ["SendAction"], mockResponsesFolder: "DiceRoll");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.ToCharacterScreen(game.Pid);
        _output.WriteLine("Экран Персонажа отрисован.");
        TestPacing.Step();

        _output.WriteLine("Отправляю действие — жду начала ожидания SendAction (мок)...");
        GameConsole.SendText(game.Pid, "атакую врага мечом");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
        Thread.Sleep(150);

        string rollRequestPath = Path.Combine(game.RealStorageDir, "roll_request.json");
        var rollRequest = new
        {
            history = new[] { new { text = "Бросай атаку!" } },
            difficulty = 15,
            modifiers = new[] { new { name = "Ловкость", value = 4 } },
        };
        _output.WriteLine("Пишу roll_request.json — жду, что игра подхватит его и покажет диалог броска...");
        File.WriteAllText(rollRequestPath, JsonSerializer.Serialize(rollRequest));
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "СЛ: 15", 4000),
            "Диалог броска кубика не показал сложность (СЛ: 15).");
        TestPacing.Step();
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Ловкость", 2000),
            "Диалог броска кубика не показал модификатор (+4 Ловкость).");
        TestPacing.Step();
        // "бросить" появляется только в ИНТЕРАКТИВНОМ цикле ПОСЛЕ печати request.History —
        // до этого момента нажатия клавиш просто сбрасываются (см. DialogDisplay.PlayDiceRollRequest:
        // `while (Console.KeyAvailable) Console.ReadKey(true);` сразу после стриминга истории).
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "бросить", 3000),
            "Не дождались интерактивного приглашения «[Пробел/Enter] бросить».");
        _output.WriteLine("Диалог броска отображён корректно. Бросаю (Space)...");
        TestPacing.Step();

        GameConsole.SendKey(game.Pid, ConsoleKey.Spacebar);

        // Исход броска случаен (d20) — ждём любой из 4 возможных вариантов.
        string[] outcomes = ["КРИТИЧЕСКИЙ УСПЕХ!", "КРИТИЧЕСКИЙ ПРОВАЛ!", "УСПЕХ", "ПРОВАЛ"];
        string? outcome = GameConsole.WaitForAnyRowContainsAny(
            game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, outcomes, 8000);
        Assert.True(outcome != null, "Анимация броска не показала результат ни одного из ожидаемых исходов.");
        _output.WriteLine($"Показан исход: «{outcome}».");
        TestPacing.Step();

        // Критический успех/провал коммитить (80,220,80)/(220,80,80) — общий "успех"/"провал"
        // красится теми же двумя цветами (см. DialogDisplay.BuildRollPanel: _outcomeColor).
        bool isSuccess = outcome!.Contains("успех", StringComparison.OrdinalIgnoreCase);
        (int r, int g, int b) expectedColor = isSuccess ? (80, 220, 80) : (220, 80, 80);
        (byte r, byte g, byte b) background = (18, 18, 22);
        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        var failures = new List<string>();
        _output.WriteLine($"Проверяю цвет исхода (ожидается {(isSuccess ? "зелёный" : "красный")})...");
        ScreenAssertions.CheckTextColor(game.Pid, rows, outcome, expectedColor, 60, background, "диалог броска", failures);
        TestPacing.Step();

        _output.WriteLine("Жду завершения SendAction (мок) после броска...");
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "продолжается", 10000),
            "Не дождались финального сообщения SendAction (мок) после разрешения броска.");

        // Финальный Step() — до Assert, а не после: если проверки провалились, Assert.True ниже
        // бросит исключение и код после него не выполнится, а пауза для отладки нужна в любом случае.
        TestPacing.Step();
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }

    // Преимущество/помеха (парный d20 + метка "ПРЕИМУЩЕСТВО"/"ПОМЕХА" — см. DialogDisplay.
    // BuildRollPanel/PlayDiceRollRequest: isPair = request.Advantage || request.Disadvantage) —
    // до сих пор проверялся только обычный одиночный бросок (DiceRollRequest_DisplaysAndResolves).
    [Fact]
    public void DiceRollRequest_WithAdvantage_DisplaysPairAndResolves()
    {
        using var game = GameSession.Launch(mockedActions: ["SendAction"], mockResponsesFolder: "DiceRollAdvantage");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.ToCharacterScreen(game.Pid);
        _output.WriteLine("Экран Персонажа отрисован.");
        TestPacing.Step();

        _output.WriteLine("Отправляю действие — жду начала ожидания SendAction (мок)...");
        GameConsole.SendText(game.Pid, "пытаюсь скрытно проскользнуть мимо стражи");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
        Thread.Sleep(150);

        string rollRequestPath = Path.Combine(game.RealStorageDir, "roll_request.json");
        var rollRequest = new
        {
            history = new[] { new { text = "Бросай на Скрытность с преимуществом!" } },
            difficulty = 14,
            advantage = true,
            modifiers = new[] { new { name = "Ловкость", value = 4 } },
        };
        _output.WriteLine("Пишу roll_request.json (advantage: true) — жду диалог броска с парой кубиков...");
        File.WriteAllText(rollRequestPath, JsonSerializer.Serialize(rollRequest));
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "ПРЕИМУЩЕСТВО", 4000),
            "Диалог броска не показал метку «ПРЕИМУЩЕСТВО» для парного броска.");
        _output.WriteLine("Метка «ПРЕИМУЩЕСТВО» отображена.");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "бросить", 3000),
            "Не дождались интерактивного приглашения «[Пробел/Enter] бросить» для парного броска.");
        _output.WriteLine("Бросаю (Space)...");
        TestPacing.Step();

        GameConsole.SendKey(game.Pid, ConsoleKey.Spacebar);

        string[] outcomes = ["КРИТИЧЕСКИЙ УСПЕХ!", "КРИТИЧЕСКИЙ ПРОВАЛ!", "УСПЕХ", "ПРОВАЛ"];
        string? outcome = GameConsole.WaitForAnyRowContainsAny(
            game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, outcomes, 8000);
        Assert.True(outcome != null, "Анимация парного броска не показала результат ни одного из ожидаемых исходов.");
        _output.WriteLine($"Показан исход: «{outcome}».");
        TestPacing.Step();

        _output.WriteLine("Жду завершения SendAction (мок) после броска...");
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "продолжается", 10000),
            "Не дождались финального сообщения SendAction (мок) после разрешения парного броска.");
        TestPacing.Step();
    }

    // Помеха (disadvantage) — тот же парный d20, что и преимущество, но с меткой "ПОМЕХА" и
    // используется МЕНЬШИЙ из двух бросков (DialogDisplay.PlayDiceRollRequest: usedRoll =
    // Math.Min(roll1, roll2)). Модификаторов намеренно 3 (а не 1, как в остальных тестах) —
    // проверяет пошаговую анимацию (BuildRollPanel: подсветка модификатора синхронно с числом на
    // используемой кости, суммарно ~2.8с на все модификаторы поровну — с тремя модификаторами
    // каждый короче, чем был бы с одним).
    [Fact]
    public void DiceRollRequest_WithDisadvantage_DisplaysPairAndMultipleModifiers()
    {
        using var game = GameSession.Launch(mockedActions: ["SendAction"], mockResponsesFolder: "DiceRollDisadvantage");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.ToCharacterScreen(game.Pid);
        _output.WriteLine("Экран Персонажа отрисован.");
        TestPacing.Step();

        _output.WriteLine("Отправляю действие — жду начала ожидания SendAction (мок)...");
        GameConsole.SendText(game.Pid, "пытаюсь пройти по узкому обледенелому карнизу");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
        Thread.Sleep(150);

        string rollRequestPath = Path.Combine(game.RealStorageDir, "roll_request.json");
        var rollRequest = new
        {
            history = new[] { new { text = "Бросай на Ловкость (Акробатика) с помехой!" } },
            difficulty = 16,
            disadvantage = true,
            modifiers = new[]
            {
                new { name = "Ловкость", value = 3 },
                new { name = "Акробатика", value = 2 },
                new { name = "Гололёд", value = -2 },
            },
        };
        _output.WriteLine("Пишу roll_request.json (disadvantage: true, 3 модификатора) — жду диалог броска с парой кубиков...");
        File.WriteAllText(rollRequestPath, JsonSerializer.Serialize(rollRequest));
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "ПОМЕХА", 4000),
            "Диалог броска не показал метку «ПОМЕХА» для парного броска.");
        _output.WriteLine("Метка «ПОМЕХА» отображена.");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "бросить", 3000),
            "Не дождались интерактивного приглашения «[Пробел/Enter] бросить» для парного броска.");
        _output.WriteLine("Бросаю (Space)...");
        TestPacing.Step();

        GameConsole.SendKey(game.Pid, ConsoleKey.Spacebar);

        // Все 3 модификатора должны появиться в панели (BuildRollPanel: footer, Take(3)) —
        // независимо от того, на какой фазе анимации (подсветка/пауза) их поймает опрос.
        foreach (var mod in rollRequest.modifiers)
            Assert.True(
                GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, mod.name, 6000),
                $"Модификатор «{mod.name}» не отобразился в панели броска.");
        _output.WriteLine("Все 3 модификатора отображены.");
        TestPacing.Step();

        string[] outcomes = ["КРИТИЧЕСКИЙ УСПЕХ!", "КРИТИЧЕСКИЙ ПРОВАЛ!", "УСПЕХ", "ПРОВАЛ"];
        string? outcome = GameConsole.WaitForAnyRowContainsAny(
            game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, outcomes, 10000);
        Assert.True(outcome != null, "Анимация парного броска с помехой не показала результат ни одного из ожидаемых исходов.");
        _output.WriteLine($"Показан исход: «{outcome}».");
        TestPacing.Step();

        _output.WriteLine("Жду завершения SendAction (мок) после броска...");
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "продолжается", 10000),
            "Не дождались финального сообщения SendAction (мок) после разрешения парного броска с помехой.");
        TestPacing.Step();
    }

    // Бросок без сложности (RollRequest.Difficulty == null) — именно так промпт (§ НАЧАЛО БОЯ)
    // требует бросать инициативу героя: roll_dice с modifiers:[{"name":"Инициатива",...}], БЕЗ
    // difficulty (враги инициативу не бросают, ИИ придумывает её сам). DialogDisplay.BuildRollPanel
    // должен просто не показывать строку «СЛ:» и не показывать «Успех»/«Провал» — сам факт того,
    // что оба поля опциональны (RollRequest.Difficulty — int?), нигде раньше не проверялся.
    [Fact]
    public void DiceRollRequest_WithoutDifficulty_DisplaysNoDcOrOutcome()
    {
        using var game = GameSession.Launch(mockedActions: ["SendAction"], mockResponsesFolder: "DiceRollInitiative");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.ToCharacterScreen(game.Pid);
        _output.WriteLine("Экран Персонажа отрисован.");
        TestPacing.Step();

        _output.WriteLine("Отправляю действие — жду начала ожидания SendAction (мок)...");
        GameConsole.SendText(game.Pid, "нападают враги, начинаем бой");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
        Thread.Sleep(150);

        string rollRequestPath = Path.Combine(game.RealStorageDir, "roll_request.json");
        // "difficulty" сознательно ОТСУТСТВУЕТ — как при реальном броске инициативы.
        var rollRequest = new
        {
            history = new[] { new { text = "Бросай инициативу!" } },
            modifiers = new[] { new { name = "Инициатива", value = 2 } },
        };
        _output.WriteLine("Пишу roll_request.json (без difficulty) — жду диалог броска без СЛ...");
        File.WriteAllText(rollRequestPath, JsonSerializer.Serialize(rollRequest));
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Инициатива", 4000),
            "Диалог броска не показал модификатор («Инициатива»).");
        _output.WriteLine("Модификатор «Инициатива» отображён.");
        TestPacing.Step();

        var beforeRoll = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        Assert.DoesNotContain(beforeRoll, r => r.Contains("СЛ:"));

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "бросить", 3000),
            "Не дождались интерактивного приглашения «[Пробел/Enter] бросить».");
        _output.WriteLine("Бросаю (Space)...");
        TestPacing.Step();

        GameConsole.SendKey(game.Pid, ConsoleKey.Spacebar);

        _output.WriteLine("Жду завершения SendAction (мок) после броска...");
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "продолжается", 10000),
            "Не дождались финального сообщения SendAction (мок) после разрешения броска без сложности.");

        // Финальный снимок — СЛ: не должна была появиться НИ на одном этапе анимации броска.
        // "Успех"/"Провал" не проверяем: критический текст ("Критический успех!"/"...провал!")
        // всё равно может появиться на нат.1/20 даже без сложности — это не то, что здесь важно.
        var afterRoll = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        Assert.DoesNotContain(afterRoll, r => r.Contains("СЛ:"));
        TestPacing.Step();
    }

    [Fact]
    public void AskPlayerRequest_DisplaysAndResolves()
    {
        using var game = GameSession.Launch(mockedActions: ["SendAction"], mockResponsesFolder: "AskPlayer");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.ToCharacterScreen(game.Pid);
        _output.WriteLine("Экран Персонажа отрисован.");
        TestPacing.Step();

        _output.WriteLine("Отправляю действие — жду начала ожидания SendAction (мок)...");
        GameConsole.SendText(game.Pid, "подхожу к развилке");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
        Thread.Sleep(150);

        string askPlayerPath = Path.Combine(game.RealStorageDir, "ask_player_request.json");
        string[] options = ["Драться", "Бежать", "Договориться"];
        var askRequest = new
        {
            history = new[] { new { text = "Что будешь делать?" } },
            options,
        };
        _output.WriteLine("Пишу ask_player_request.json — жду, что игра подхватит его и покажет вопрос с вариантами...");
        File.WriteAllText(askPlayerPath, JsonSerializer.Serialize(askRequest));
        TestPacing.Step();

        foreach (var option in options)
            Assert.True(
                GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, option, 4000),
                $"Вариант ответа «{option}» не отображён в диалоге вопроса.");
        _output.WriteLine("Все варианты ответа отображены. Выбираю первый (Enter)...");
        TestPacing.Step();

        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);

        // Выбранный вариант должен появиться в истории диалога СРАЗУ (DialogDisplay.
        // PlayAskPlayerRequest перерисовывает блок диалога немедленно после выбора), а НЕ
        // только когда весь SendAction (мок, искусственная задержка 5с) полностью завершится.
        // Таймаут здесь намеренно короткий, а не "с запасом" — широкое окно замаскировало бы
        // регрессию (тест прошёл бы всё равно, просто дождавшись финального ответа мока).
        // ВАЖНО: ищем именно "Рустем: <ответ>" (с префиксом автора) — а не голый текст варианта,
        // который какое-то время ещё виден в самом СПИСКЕ ВАРИАНТОВ (PlayOptionsSelection) и дал
        // бы ложное срабатывание независимо от того, попал ли ответ в историю диалога.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, $"Рустем: {options[0]}", 2000),
            "Выбранный вариант ответа не появился в истории диалога СРАЗУ после выбора — должен отображаться немедленно, не дожидаясь ответа мока.");
        TestPacing.Step();

        _output.WriteLine("Жду завершения SendAction (мок) после ответа...");
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "принят", 10000),
            "Не дождались финального сообщения SendAction (мок) после ответа на вопрос.");

        // Финальный Step() — пауза в конце теста для визуальной проверки итогового состояния
        // перед тем, как GameSession.Dispose() убьёт процесс игры.
        TestPacing.Step();
    }

    // Без "options" DialogDisplay.PlayAskPlayerRequest уходит в ДРУГУЮ ветку — PlayFreeTextInput
    // (открытое текстовое поле "Ввод (Ns): ") вместо выбора из вариантов (PlayOptionsSelection).
    // Это отдельный, ранее не проверенный путь отображения — со своим полем ввода и курсором.
    [Fact]
    public void AskPlayerRequest_FreeText_DisplaysAndResolves()
    {
        using var game = GameSession.Launch(mockedActions: ["SendAction"], mockResponsesFolder: "AskPlayerFreeText");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.ToCharacterScreen(game.Pid);
        _output.WriteLine("Экран Персонажа отрисован.");
        TestPacing.Step();

        _output.WriteLine("Отправляю действие — жду начала ожидания SendAction (мок)...");
        GameConsole.SendText(game.Pid, "осматриваюсь по сторонам");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
        Thread.Sleep(150);

        string askPlayerPath = Path.Combine(game.RealStorageDir, "ask_player_request.json");
        // "options" сознательно ОТСУТСТВУЕТ в запросе — именно это переключает диалог на
        // открытый текстовый ввод вместо списка вариантов.
        var askRequest = new { history = new[] { new { text = "Что именно ты ищешь взглядом?" } } };
        _output.WriteLine("Пишу ask_player_request.json (без options) — жду открытое текстовое поле...");
        File.WriteAllText(askPlayerPath, JsonSerializer.Serialize(askRequest));
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "взглядом", 4000),
            "Текст вопроса не отобразился.");
        // ВАЖНО: обычное диалоговое окно (InputBox) само по умолчанию показывает "Ввод: " как
        // placeholder в простое — просто "Ввод" совпало бы с ним МГНОВЕННО и до того, как реально
        // откроется интерактивный цикл открытого ввода (PlayFreeTextInput), и набранный текст
        // ушёл бы в никуда. У PlayFreeTextInput формат другой — "Ввод (Ns): " со скобкой и
        // таймером — ищем именно "Ввод (", это не совпадает с обычным плейсхолдером InputBox.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Ввод (", 3000),
            "Открытое текстовое поле ответа («Ввод (Ns): ») не отобразилось.");
        _output.WriteLine("Текстовое поле отображено. Ввожу свободный ответ...");
        TestPacing.Step();

        const string freeTextAnswer = "ищу потайную дверь в стене";
        GameConsole.SendText(game.Pid, freeTextAnswer);
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);

        // Введённый ответ должен появиться в истории диалога СРАЗУ (DialogDisplay.
        // PlayAskPlayerRequest перерисовывает блок диалога немедленно после ввода), а НЕ
        // только когда весь SendAction (мок, искусственная задержка 5с) полностью завершится.
        // Таймаут здесь намеренно короткий — широкое окно замаскировало бы регрессию (тест
        // прошёл бы всё равно, просто дождавшись финального ответа мока). ВАЖНО: ищем именно
        // "Рустем: <ответ>" (с префиксом автора) — голый текст ответа ещё виден в САМОМ ПОЛЕ
        // ВВОДА (PlayFreeTextInput рисует его в строке "Ввод (Ns): <текст>") независимо от того,
        // попал ли ответ в историю диалога, и дал бы ложное срабатывание.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, $"Рустем: {freeTextAnswer}", 2000),
            "Введённый свободный текстовый ответ не появился в истории диалога СРАЗУ после ввода — должен отображаться немедленно, не дожидаясь ответа мока.");
        TestPacing.Step();

        _output.WriteLine("Жду завершения SendAction (мок) после свободного ответа...");
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "продолжается", 15000),
            "Не дождались финального сообщения SendAction (мок) после свободного ответа.");

        TestPacing.Step();
    }
}
