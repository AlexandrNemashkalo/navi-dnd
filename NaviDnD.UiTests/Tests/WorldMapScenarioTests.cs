using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: world map ([F2] World) and terrain ([F1] Map). Fixtures/world_game.json - a game without a tactical map
// (hero in the village Keys of the World world-uitest, Fixtures/world_uitest.json: chronicle - Erdwald,
// Lesogorye, capital Svetlograd): “Continue” opens the world (“@ You: Keys”), the Map tab is gray and does not
// open according to F1, the magazine [F8] “Locations” - famous places, click on the capital - route in the
// legend, [F10] - journey (GM's answer - mock SendAction). The meeting on the way is random - it is checked
// that the hero left the Keys, and not where exactly he came.
public class WorldMapScenarioTests
{
    private readonly ITestOutputHelper _output;

    public WorldMapScenarioTests(ITestOutputHelper output) => _output = output;

    // Fixtures/world_location_game.json - the same game in the Keys, but with a tactical map (approach +
    // village along the world cell, hero at the entrance from below). F2 - world, F1 - terrain; step down
    // through the exit to the world map - the hero goes into the world, the location is saved behind the Keys
    // (Worlds/<id>/locations), the Map tab becomes gray.
    [Fact]
    public void Location_F1TogglesWorld_StepOutThroughWorldExit_SavesLocation()
    {
        using var game = GameSession.Launch(fixtureFileName: "world_location_game.json");
        string worldDir = Path.Combine(game.RealStorageDir, "Worlds", "world-uitest");
        Directory.CreateDirectory(worldDir);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world_uitest.json"), Path.Combine(worldDir, "world.json"), overwrite: true);
        try
        {
            Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
            Assert.True(GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000), "Не открылась вкладка Карта.");
            bool local = GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Время:", 5000);
            if (!local) Dump(game.Pid);
            Assert.True(local, "С тактической картой вкладка Карта должна открыться на местности (легенда «Время:»).");

            GameConsole.SendKey(game.Pid, ConsoleKey.F2);
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Вы: Ключи", 5000),
                "F2 не открыл мир.");
            GameConsole.SendKey(game.Pid, ConsoleKey.F1);
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Время:", 5000),
                "F1 не вернул местность.");
            TestPacing.Step();

            // Step outside through the exit to the world map.
            GameConsole.SendKey(game.Pid, ConsoleKey.DownArrow);
            bool left = GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Вы: Ключи", 6000);
            if (!left) Dump(game.Pid);
            Assert.True(left, "Шаг через выход не увёл героя на карту мира.");
            Assert.True(GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ МИР ←", 5000), "После выхода — не вкладка Мир.");
            Assert.True(Directory.Exists(Path.Combine(worldDir, "locations")) && Directory.GetFiles(Path.Combine(worldDir, "locations")).Length > 0,
                "Локация не сохранилась за местом при выходе.");
        }
        finally
        {
            game.Dispose();
            try { Directory.Delete(worldDir, recursive: true); } catch { }
        }
    }

    private void Dump(int pid)
    {
        foreach (var r in GameConsole.ReadRows(pid, 0, 60, GameConsole.TitleReadWidth)) _output.WriteLine(r.TrimEnd());
    }

    [Fact]
    public void NoLocation_ShowsWorld_JournalPlaces_TravelByClickAndF10()
    {
        using var game = GameSession.Launch(fixtureFileName: "world_game.json", mockedActions: ["SendAction"], mockResponsesFolder: "WorldTravel");
        // Game world - in the worlds library next to the save; After the test we remove it.
        string worldDir = Path.Combine(game.RealStorageDir, "Worlds", "world-uitest");
        Directory.CreateDirectory(worldDir);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world_uitest.json"), Path.Combine(worldDir, "world.json"), overwrite: true);
        try
        {
            Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
            Assert.True(GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ МИР ←", 5000),
                "Без тактической карты ПРОДОЛЖИТЬ должно открыть вкладку Мир.");
            bool worldShown = GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Вы: Ключи", 5000);
            if (!worldShown) Dump(game.Pid);
            Assert.True(worldShown, "Без тактической карты вкладка Карта должна показывать мир с героем («Вы: Ключи» в легенде).");
            TestPacing.Step();

            // The map is gray - F1 does not open it.
            GameConsole.SendKey(game.Pid, ConsoleKey.F1);
            Thread.Sleep(600);
            Assert.Contains("→ МИР ←", GameConsole.ReadRow(game.Pid, 1, GameConsole.TitleReadWidth));

            // Journal → Locations: famous places.
            GameConsole.SendKey(game.Pid, ConsoleKey.F3);
            Assert.True(GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ ЖУРНАЛ ←", 5000), "Не открылся журнал по [F3].");
            GameConsole.SendKey(game.Pid, ConsoleKey.F8);
            Assert.True(GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                rows => rows.Any(r => r.Contains("Ключи")) && rows.Any(r => r.Contains("Светлоград")), 5000),
                "В журнале «Локации» нет известных мест (Ключи, Светлоград).");
            TestPacing.Step();

            // Back to the world.
            GameConsole.SendKey(game.Pid, ConsoleKey.F2);
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Вы: Ключи", 5000),
                "После [F2] из журнала не открылась карта мира.");

            // Click on the capital icon on the map (the label “Svetlograd” to the right of the icon; the
            // leftmost one is on the map, to the right is the list of places in the legend).
            var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
            var label = rows.Select((r, i) => (row: i, col: r.IndexOf("Светлоград", StringComparison.Ordinal)))
                .Where(t => t.col > 0).OrderBy(t => t.col).FirstOrDefault();
            Assert.True(label.col > 0, "На карте мира не видно подписи столицы Светлоград.");
            _output.WriteLine($"Подпись столицы: строка {label.row}, колонка {label.col}");
            GameConsole.SendLeftClick(game.Pid, label.col - 2, label.row);
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Путь: Светлоград", 5000),
                "После клика по столице в легенде нет строки пути.");
            TestPacing.Step();

            // Let's go.
            GameConsole.SendKey(game.Pid, ConsoleKey.F10);
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "разбиваешь лагерь", 20000),
                "Не появился рассказ мастера о пути (мок SendAction).");
            Assert.True(GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => r.Any(x => x.Contains("Вы: ")) && !r.Any(x => x.Contains("Вы: Ключи")), 8000),
                "После путешествия герой остался в Ключах.");
        }
        finally
        {
            game.Dispose();
            try { Directory.Delete(worldDir, recursive: true); } catch { }
        }
    }
}
