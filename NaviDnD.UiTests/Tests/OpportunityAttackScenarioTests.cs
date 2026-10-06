using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: атака по возможности (MovementHandler.GetOpportunityAttackTriggers) — во время активного
// боя отступление героя из клетки, смежной с сущностью, провоцирует триггер
// "opportunity_attack_on_hero", который идёт через тот же механизм FireTrigger, что и обычные
// триггеры карты (см. TriggersScenarioTests) — раньше это не проверялось ни одним UI-тестом.
//
// Fixtures/opportunity_attack_game.json: герой в [3,3], орк "ORC" в [4,3] (смежная клетка), бой
// активен, ход героя. Шаг на запад увеличивает дистанцию с 1 до 2 клеток — ровно момент, когда
// провоцируется атака по возможности.
public class OpportunityAttackScenarioTests
{
    private readonly ITestOutputHelper _output;

    public OpportunityAttackScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void RetreatingFromAdjacentEnemyInCombat_TriggersOpportunityAttack()
    {
        using var game = GameSession.Launch(
            fixtureFileName: "opportunity_attack_game.json", mockedActions: ["SendAction"], mockResponsesFolder: "OpportunityAttack");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000),
            "Не дождались экрана Карты после [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован. Отступаю на запад — от орка, смежного с героем...");
        TestPacing.Step();

        GameConsole.SendKey(game.Pid, ConsoleKey.LeftArrow);

        // Отступление из смежной клетки должно вызвать FireTrigger (мок SendAction, искусственная
        // задержка 5с) — спиннер доказывает реальный AI-вызов, а не просто локальный редрав позиции.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Ожидание", 3000),
            "После отступления из клетки, смежной с орком, не появился спиннер ожидания — атака по возможности не сработала.");
        _output.WriteLine("Спиннер ожидания появился — атака по возможности сработала, FireTrigger вызван. Жду ответ (мок)...");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "продолжается", 10000),
            "Не дождались ответа FireTrigger (мок) после атаки по возможности.");
        _output.WriteLine("Ответ на атаку по возможности отображён.");
        TestPacing.Step();
    }
}
