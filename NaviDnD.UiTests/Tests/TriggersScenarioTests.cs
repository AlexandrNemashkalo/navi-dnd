using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: движение героя по карте активирует триггеры (MovementHandler.TryMove → GameAiClient.FireTrigger),
// заявленные в CLAUDE.md как готовая часть "Исследования", но ранее не проверенные через реальный UI —
// ни один существующий тест не проводил героя через onStep-ловушку или onVisible-триггер скрытого
// объекта. FireTrigger использует тот же системный промпт (Prompts/Dnd5e/SendAction), что и обычный
// SendAction (см. GameAiClient.BuildSendActionContext) — поэтому мок с именем действия "SendAction"
// перехватывает и обычные действия игрока, и срабатывание триггеров одинаково.
public class TriggersScenarioTests
{
    private readonly ITestOutputHelper _output;

    public TriggersScenarioTests(ITestOutputHelper output) => _output = output;

    // Fixtures/trap_game.json: герой в [1,3], объект-ловушка "Капкан" в [2,3] с triggers.onStep —
    // один шаг на восток должен наступить прямо на неё.
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

        // Шаг на ловушку должен вызвать FireTrigger (мок SendAction, искусственная задержка 5с) —
        // спиннер "Ожидание..." доказывает, что реально произошёл AI-вызов, а не просто локальный
        // рендер новой позиции (обычное движение без триггеров ИИ вообще не вызывает — см.
        // MovementFogOfWarTests, где силент-шаг проверяется от противного).
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

    // Fixtures/hidden_object_game.json: герой в [1,3], скрытый объект "Тайный символ" в [5,3]
    // (hidden:true, triggers.onVisible), зрение героя 10фт (2 клетки). Первый шаг на восток
    // (дистанция станет 3 клетки) triggers.onVisible ещё не должен сработать — герой не увидел
    // объект; второй шаг (дистанция 2 клетки) должен его "заметить" и вызвать FireTrigger,
    // несмотря на то что сам объект никогда не отображается на сетке (hidden:true).
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

        // Обычное локальное движение (без триггеров) не должно вызывать ИИ вообще — отсутствие
        // спиннера в течение короткого окна доказывает, что FireTrigger НЕ был вызван преждевременно.
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
