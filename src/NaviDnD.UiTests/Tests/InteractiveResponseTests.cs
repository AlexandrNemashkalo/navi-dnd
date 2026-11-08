using System.Text.Json;
using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: dice roll dialog and AI question dialog (ask_player) - both in a real game are launched by
// GameAiClient.SpinAsync, which polls roll_request.json/ask_player_request.json into NaviDnD/Storage/ WHILE
// waiting for a response from SendAction (mock or real - it doesn’t matter). Since this is reading a file, and
// not the call of the AI itself, the test can cause a dialogue by simply writing such a file during an
// artificial delay mock response (SelectiveMockProvider.MockResponseDelayMs, 5c) - without a single real call
// to AI.
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
        // "abandon" only appears in the INTERACTIVE loop AFTER printing request.History - up to this point,
        // keystrokes are simply reset (see DialogDisplay.PlayDiceRollRequest: `while (Console.KeyAvailable)
        // Console.ReadKey(true);` immediately after the story is streamed).
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "бросить", 3000),
            "Не дождались интерактивного приглашения «[Пробел/Enter] бросить».");
        _output.WriteLine("Диалог броска отображён корректно. Бросаю (Space)...");
        TestPacing.Step();

        GameConsole.SendKey(game.Pid, ConsoleKey.Spacebar);
        // Navigation must respond while the dice is still settling, before its result.
        Thread.Sleep(120);
        GameConsole.SendKey(game.Pid, ConsoleKey.F1);
        Assert.True(GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth,
            "→ КАРТА ←", 1200), "Map navigation stalled during the dice animation.");
        GameConsole.SendKey(game.Pid, ConsoleKey.F4);
        Assert.True(GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth,
            "→ ПЕРСОНАЖ ←", 1200), "Character navigation stalled during the dice animation.");


        // The outcome of the roll is random (d20) - we are waiting for any of the 4 possible options.
        string[] outcomes = ["КРИТИЧЕСКИЙ УСПЕХ!", "КРИТИЧЕСКИЙ ПРОВАЛ!", "УСПЕХ", "ПРОВАЛ"];
        string? outcome = GameConsole.WaitForAnyRowContainsAny(
            game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, outcomes, 8000);
        Assert.True(outcome != null, "Анимация броска не показала результат ни одного из ожидаемых исходов.");
        _output.WriteLine($"Показан исход: «{outcome}».");
        TestPacing.Step();

        // Critical success/failure to commit (80,220,80)/(220,80,80) - the overall “success”/“failure” is
        // painted in the same two colors (see DialogDisplay.BuildRollPanel: _outcomeColor).
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

        // The final Step() is before Assert, not after: if the checks fail, Assert.True below will throw an
        // exception and the code after it will not be executed, and a pause for debugging is needed in any
        // case.
        TestPacing.Step();
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }

    // Advantage/Handler (paired d20 + label "ADVANTAGE"/"HANDLE" - see DialogDisplay.
    // BuildRollPanel/PlayDiceRollRequest: isPair = request.Advantage || request.Disadvantage) - so far only the
    // normal single throw has been checked (DiceRollRequest_DisplaysAndResolves).
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

    // Interference (disadvantage) - the same paired d20 as advantage, but with the label "INTERFERENCE" and the
    // LESSER of the two throws is used (DialogDisplay.PlayDiceRollRequest: usedRoll = Math.Min(roll1, roll2)).
    // There are intentionally 3 modifiers (and not 1, as in other tests) - checks step-by-step animation
    // (BuildRollPanel: modifier highlighting synchronously with the number on the die being used, in total
    // ~2.8s for all modifiers equally - with three modifiers, each is shorter than it would be with one).
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

        // All 3 modifiers should appear in the panel (BuildRollPanel: footer, Take(3)) - no matter what phase
        // of the animation (highlight/pause) the poll catches them.
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

    // Throw without difficulty (RollRequest.Difficulty == null) - this is exactly how the prompt (§ START OF
    // BATTLE) requires throwing the hero’s initiative: roll_dice with modifiers:[{"name":"Initiative",...}],
    // WITHOUT difficulty (enemies do not throw initiative, the AI comes up with it itself).
    // DialogDisplay.BuildRollPanel should simply not show the "DC:" line and not show "Success"/"Failure" - the
    // fact that both fields are optional (RollRequest.Difficulty - int?) has not been tested anywhere before.
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
        // "difficulty" is deliberately MISSING - as in a real initiative roll.
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

        // Final frame - DC: should not have appeared at ANY stage of the roll animation. "Success"/"Failure" is
        // not checked: critical text ("Critical success!"/"...failure!") can still appear on nat.1/20 even
        // without difficulty - that's not what's important here.
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

        // The selected option should appear in the dialogue history IMMEDIATELY (DialogDisplay.
        // PlayAskPlayerRequest redraws the dialogue block immediately after selection), and NOT only when the
        // entire SendAction (mock, artificial delay 5s) is completely completed. The timeout here is
        // intentionally short, and not “with a margin” - a wide window would mask the regression (the test
        // would pass anyway, just waiting for the final response of the mock). IMPORTANT: we are looking for
        // exactly “Rustem: <answer>” (with the author’s prefix) - and not the bare text of the option, which is
        // still visible for some time in the LIST OF OPTIONS itself (PlayOptionsSelection) and would give a
        // false positive regardless of whether the answer was included in the dialogue history.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, $"Рустем: {options[0]}", 2000),
            "Выбранный вариант ответа не появился в истории диалога СРАЗУ после выбора — должен отображаться немедленно, не дожидаясь ответа мока.");
        TestPacing.Step();

        _output.WriteLine("Жду завершения SendAction (мок) после ответа...");
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "принят", 10000),
            "Не дождались финального сообщения SendAction (мок) после ответа на вопрос.");

        // Final Step() - pause at the end of the test to visually check the final state before
        // GameSession.Dispose() kills the game process.
        TestPacing.Step();
    }

    // Without "options" DialogDisplay.PlayAskPlayerRequest goes to ANOTHER branch - PlayFreeTextInput (open
    // text field "Enter (Ns):") instead of selecting from options (PlayOptionsSelection). This is a separate,
    // previously untested display path - with its own input field and cursor.
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
        // "options" is deliberately MISSING from the request - this is what switches the dialog to open text
        // input instead of a list of options.
        var askRequest = new { history = new[] { new { text = "Что именно ты ищешь взглядом?" } } };
        _output.WriteLine("Пишу ask_player_request.json (без options) — жду открытое текстовое поле...");
        File.WriteAllText(askPlayerPath, JsonSerializer.Serialize(askRequest));
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "взглядом", 4000),
            "Текст вопроса не отобразился.");
        // IMPORTANT: a regular dialog box (InputBox) itself by default shows "Enter:" as placeholder in idle
        // time - just "Enter" would coincide with it INSTANTLY and before the interactive open input loop
        // actually opens (PlayFreeTextInput)), and the typed text would go to nowhere. PlayFreeTextInput has a
        // different format - "Enter (Ns): "with a bracket and a timer - we are looking for exactly "Input (),
        // this does not coincide with the usual placeholder InputBox.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Ввод (", 3000),
            "Открытое текстовое поле ответа («Ввод (Ns): ») не отобразилось.");
        _output.WriteLine("Текстовое поле отображено. Ввожу свободный ответ...");
        TestPacing.Step();

        const string freeTextAnswer = "ищу потайную дверь в стене";
        GameConsole.SendText(game.Pid, freeTextAnswer);
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);

        // The entered response should appear in the dialogue history IMMEDIATELY (DialogDisplay.
        // PlayAskPlayerRequest redraws the dialogue block immediately after input), and NOT only when the
        // entire SendAction (mock, artificial delay 5s) is completely completed. The timeout here is
        // intentionally short - a wide window would have masked the regression (the test would have passed
        // anyway, just waiting for the final response from the mock). IMPORTANT: we are looking for exactly
        // “Rustem: <answer>” (with the author’s prefix) - the bare text of the answer is still visible in the
        // INPUT FIELD ITSELF (PlayFreeTextInput draws it in the line “Input (Ns): <text>”), regardless of
        // whether the answer is included in the dialogue history, and would give a false positive.
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
