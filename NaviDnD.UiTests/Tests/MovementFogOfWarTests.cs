using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: arrow movement and fog of war (MapObjectsProvider.ComputeHeroVisibility/WriteCellEntity) are covered in
// logic unit tests (CalculateMovementTests), but never run through an actual game loop/UI - no existing UI-
// test sends a movement arrow to the hero and checks that the visibility of entities on the grid changes
// dynamically depending on distance. Fixtures/fog_of_war_game.json: hero in [1,3] with visionFt=5 (1 cell),
// creature "Skeleton" (SK1) in [4,3] with its own visionFt=0 (never "notices" the hero itself - otherwise the
// encounter- trigger would work and an AI mock would be required). Normal movement without triggers does not
// trigger the AI at all (see MapScreenLoop.RunAsync: moved == false → redream on the spot and wait for input
// again), so no mock is needed here - this is a pure test of the fog of war rendering through the real game
// process.
public class MovementFogOfWarTests
{
    private readonly ITestOutputHelper _output;

    public MovementFogOfWarTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ArrowMovement_RevealsAndHidesEntity_AsVisionRangeChanges()
    {
        using var game = GameSession.Launch(fixtureFileName: "fog_of_war_game.json");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000),
            "Не дождались экрана Карты после [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован.");
        TestPacing.Step();

        var initialRows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        Assert.DoesNotContain(initialRows, r => r.Contains("SK1"));
        _output.WriteLine("Скелет (SK1) вне зоны видимости героя — символ на карте не отображается, как и ожидалось.");
        TestPacing.Step();

        _output.WriteLine("Двигаю героя на восток дважды — дистанция до скелета сократится до 1 клетки (5фт)...");
        GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);
        Thread.Sleep(400);
        GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "SK1", 3000),
            "После приближения на дистанцию видимости (5фт) символ скелета «SK1» не появился на карте — динамический туман войны не сработал.");
        _output.WriteLine("Скелет вошёл в зону видимости — символ отобразился.");
        TestPacing.Step();

        _output.WriteLine("Отхожу обратно на запад дважды — скелет должен снова скрыться в тумане...");
        GameConsole.SendKey(game.Pid, ConsoleKey.LeftArrow);
        Thread.Sleep(400);
        GameConsole.SendKey(game.Pid, ConsoleKey.LeftArrow);

        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                rows => !rows.Any(r => r.Contains("SK1")), 3000),
            "После отхода за пределы дистанции видимости символ скелета «SK1» всё ещё виден — видимость должна быть динамической (не «увидел один раз — виден навсегда»).");
        _output.WriteLine("Скелет снова скрылся из виду за пределами дистанции видимости.");
        TestPacing.Step();
    }
}
