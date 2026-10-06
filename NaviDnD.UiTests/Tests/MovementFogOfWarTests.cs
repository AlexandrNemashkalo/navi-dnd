using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: движение стрелками и туман войны (MapObjectsProvider.ComputeHeroVisibility/WriteCellEntity)
// покрыты юнит-тестами логики (CalculateMovementTests), но никогда не прогонялись через реальный
// игровой цикл/UI — ни один существующий UI-тест не отправляет герою стрелку движения и не
// проверяет, что видимость сущностей на сетке меняется динамически в зависимости от дистанции.
//
// Fixtures/fog_of_war_game.json: герой в [1,3] с visionFt=5 (1 клетка), существо "Скелет" (SK1) в
// [4,3] с собственным visionFt=0 (никогда не "замечает" героя само — иначе сработал бы encounter-
// триггер и потребовался бы мок ИИ). Обычное движение без триггеров вообще не вызывает ИИ (см.
// MapScreenLoop.RunAsync: moved == false → редрим на месте и снова ждём ввод), поэтому здесь не
// нужен ни один мок — это чистая проверка рендера тумана войны через реальный процесс игры.
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
