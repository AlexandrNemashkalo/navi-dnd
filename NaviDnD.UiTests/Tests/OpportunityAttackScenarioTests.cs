using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: attack of opportunity (MovementHandler.GetOpportunityAttackTriggers) - during active combat, the hero's
// retreat from a cell adjacent to the entity provokes the "opportunity_attack_on_hero" trigger, which goes
// through the same FireTrigger mechanism as regular map triggers (see TriggersScenarioTests) - this has not
// been tested by any UI- test before. Fixtures/opportunity_attack_game.json: hero in [3,3], orc "ORC" at [4,3]
// (adjacent cell), the battle is active, the hero's turn. A step to the west increases the distance from 1 to 2
// cells - exactly the moment when an attack of opportunity is provoked.
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

        // Retreating from an adjacent cell should cause FireTrigger (mock SendAction, artificial delay 5s) -
        // the spinner proves a real AI- challenge, and not just a local redraw of the position.
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
