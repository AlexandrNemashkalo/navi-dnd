using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: handling incorrect/unprocessed AI response (GameAiClient.RecordError) - so far no test has fed the game
// a broken response to confirm that an error message is shown instead of crashing or freezing. Mock response
// (Fixtures/MockResponses/MalformedResponse/SendAction/ response.json) - plain text without a single character
// JSON, which is why SequentialHistoryPlayer. PlayAsync (JsonNode.Parse) throws an exception, which catches
// GameAiClient.SendAction and adds "Error: ..." directly to the dialog history.
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

        // The game should remain working - the character screen is still rendered (frames/tabs in place) and
        // not stuck in some in-between/broken state. A short pause before scanning ALL lines - immediately
        // after the first appearance of “Error”, redrawing of the dialog panel may still be in progress
        // (line-by-line rendering).
        Thread.Sleep(300);
        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        var failures = new List<string>();
        ScreenAssertions.CheckBorders(rows, "экран персонажа после ошибки ИИ", failures);
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
        TestPacing.Step();
    }
}
