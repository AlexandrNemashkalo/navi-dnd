using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: обработка некорректного/необрабатываемого ответа ИИ (GameAiClient.RecordError) — до сих пор
// ни один тест не скармливал игре битый ответ, чтобы подтвердить, что вместо краха или зависания
// показывается сообщение об ошибке. Мок-ответ (Fixtures/MockResponses/MalformedResponse/SendAction/
// response.json) — обычный текст без единого символа JSON, из-за чего SequentialHistoryPlayer.
// PlayAsync (JsonNode.Parse) бросает исключение, которое ловит GameAiClient.SendAction и добавляет
// "Ошибка: ..." прямо в историю диалога.
public class ErrorHandlingScenarioTests
{
    private readonly ITestOutputHelper _output;

    public ErrorHandlingScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void MalformedAiResponse_ShowsErrorMessage_InsteadOfCrashing()
    {
        using var game = GameSession.Launch(mockedActions: ["SendAction"], mockResponsesFolder: "MalformedResponse");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.ToCharacterScreen(game.Pid);
        _output.WriteLine("Экран Персонажа отрисован.");
        TestPacing.Step();

        _output.WriteLine("Отправляю действие — мок SendAction вернёт не-JSON текст...");
        GameConsole.SendText(game.Pid, "осматриваю комнату");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Ошибка", 15000),
            "После некорректного (не-JSON) ответа ИИ в диалоге не появилось сообщение об ошибке — обработка сломанного ответа не работает.");
        _output.WriteLine("Сообщение об ошибке отображено — игра не упала и не зависла.");
        TestPacing.Step();

        // Игра должна остаться в рабочем состоянии — экран персонажа по-прежнему отрисован
        // (рамки/вкладки на месте), а не застрял в каком-то промежуточном/сломанном состоянии.
        // Небольшая пауза перед сканированием ВСЕХ строк — сразу после первого появления "Ошибка"
        // перерисовка диалоговой панели ещё может быть в процессе (построчная отрисовка).
        Thread.Sleep(300);
        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        var failures = new List<string>();
        ScreenAssertions.CheckBorders(rows, "экран персонажа после ошибки ИИ", failures);
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
        TestPacing.Step();
    }
}
