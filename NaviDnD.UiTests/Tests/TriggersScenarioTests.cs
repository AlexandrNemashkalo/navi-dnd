using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: the hero's movement across the map activates triggers (MovementHandler.TryMove →
// GameAiClient.FireTrigger), stated in CLAUDE.md as a ready-made part of the "Research", but not previously
// tested through the real UI - no existing test has put the hero through a onStep- trap or onVisible- hidden
// object trigger. FireTrigger uses the same system prompt (Prompts/Dnd5e/SendAction)) as the normal SendAction
// (see GameAiClient.BuildSendActionContext) - therefore, a mock with the action name "SendAction" intercepts
// both the player's normal actions and triggers in the same way.
public class TriggersScenarioTests
{
    private readonly ITestOutputHelper _output;

    public TriggersScenarioTests(ITestOutputHelper output) => _output = output;

    // Fixtures/trap_game.json: hero at [1,3], trap object "Trap" at [2,3] with triggers.onStep - one step to
    // the east should step directly on it.
    [Fact]
    public void OnStepTrap_MovingOntoIt_FiresAiTrigger()
    {
        using var game = GameSession.Launch(
            fixtureFileName: "trap_game.json", mockedActions: ["SendAction"], mockResponsesFolder: "Triggers");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000),
            "Не дождались экрана Карты после [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован. Двигаю героя на восток — на клетку ловушки [2,3]...");
        TestPacing.Step();

        GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);

        // A step on a trap should cause FireTrigger (mock SendAction, artificial delay 5s) - the "Waiting..."
        // spinner proves that a AI- call actually occurred, and not just a local rendering of a new position
        // (normal movement without AI triggers does not cause at all - see MovementFogOfWarTests, where the
        // silent step is checked by contradiction).
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Ожидание", 3000),
            "После шага на клетку с onStep-ловушкой не появился спиннер ожидания — FireTrigger не был вызван.");
        _output.WriteLine("Спиннер ожидания появился — FireTrigger вызван. Жду ответ (мок)...");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "продолжается", 10000),
            "Не дождались ответа FireTrigger (мок) после срабатывания onStep-ловушки.");
        _output.WriteLine("Ответ на срабатывание ловушки отображён.");
        TestPacing.Step();
    }

    // Fixtures/hidden_object_game.json: hero in [1,3], hidden object "Secret Symbol" in [5,3] (hidden:true,
    // triggers.onVisible), hero's vision 10ft (2 cells). The first step to the east (the distance will be 3
    // cells) triggers.onVisible should not work yet - the hero has not seen the object; the second step
    // (distance 2 cells) must "notice" it and call FireTrigger, even though the object itself is never shown on
    // the mesh (hidden:true).
    [Fact]
    public void HiddenObjectOnVisible_EnteringVisionRange_FiresAiTrigger()
    {
        using var game = GameSession.Launch(
            fixtureFileName: "hidden_object_game.json", mockedActions: ["SendAction"], mockResponsesFolder: "HiddenTrigger");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000),
            "Не дождались экрана Карты после [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован. Первый шаг на восток — объект ещё вне зоны видимости...");
        TestPacing.Step();

        GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);

        // Normal local movement (without triggers) should not trigger the AI at all - the absence of a spinner
        // within a short window proves that FireTrigger was NOT triggered prematurely.
        Thread.Sleep(1200);
        var rowsAfterFirstStep = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        Assert.DoesNotContain(rowsAfterFirstStep, r => r.Contains("Ожидание"));
        _output.WriteLine("Спиннер не появился — как и ожидалось, объект ещё не замечен. Второй шаг на восток...");
        TestPacing.Step();

        GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Ожидание", 3000),
            "После второго шага (объект должен войти в зону видимости) не появился спиннер ожидания — onVisible-триггер скрытого объекта не сработал.");
        _output.WriteLine("Спиннер ожидания появился — скрытый объект замечен, FireTrigger вызван. Жду ответ (мок)...");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "продолжается", 10000),
            "Не дождались ответа FireTrigger (мок) после срабатывания onVisible-триггера.");
        _output.WriteLine("Ответ на обнаружение скрытого объекта отображён.");
        TestPacing.Step();
    }
}
