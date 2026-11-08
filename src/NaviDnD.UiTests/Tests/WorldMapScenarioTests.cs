using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: F1 switches automatically between scenery and tactical cells; F2 keeps world routes.
public class WorldMapScenarioTests
{
    private readonly ITestOutputHelper _output;

    public WorldMapScenarioTests(ITestOutputHelper output) => _output = output;

    // Fixtures/world_location_game.json - the same game in the Keys, but with a tactical map (approach +
    // village along the world cell, hero at the entrance from below). F2 - world, F1 - terrain; step down
    // through the exit saves the location and returns to scenery on F1.
    [Fact]
    public void Location_F1TogglesWorld_StepOutThroughWorldExit_SavesLocation()
    {
        using var game = GameSession.Launch(fixtureFileName: "world_location_game.json");
        string worldDir = Path.Combine(game.RealStorageDir, "Worlds", "world-uitest");
        var original = Snapshot(worldDir);
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
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Ввод:", 10000));
            Thread.Sleep(250); // Let the tab redraw finish before injecting the next input record.
            GameConsole.SendKey(game.Pid, ConsoleKey.DownArrow);
            bool left = GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "[F11] Другой вид", 6000);
            if (!left) Dump(game.Pid);
            Assert.True(left, "Шаг через выход не показал сцену приключения.");
            Assert.True(GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000), "После выхода — не вкладка Карта.");
            Assert.True(Directory.Exists(Path.Combine(worldDir, "locations")) && Directory.GetFiles(Path.Combine(worldDir, "locations")).Length > 0,
                "Локация не сохранилась за местом при выходе.");
        }
        finally
        {
            game.Dispose();
            Restore(worldDir, original);
        }
    }

    private void Dump(int pid)
    {
        foreach (var r in GameConsole.ReadRows(pid, 0, 60, GameConsole.TitleReadWidth)) _output.WriteLine(r.TrimEnd());
    }

    [Fact]
    public void NoLocation_ShowsScene_JournalPlaces_TravelByClickAndF10()
    {
        using var game = GameSession.Launch(fixtureFileName: "world_game.json", mockedActions: ["SendAction"], mockResponsesFolder: "WorldTravel");
        // Game world - in the worlds library next to the save; After the test we remove it.
        string worldDir = Path.Combine(game.RealStorageDir, "Worlds", "world-uitest");
        var original = Snapshot(worldDir);
        Directory.CreateDirectory(worldDir);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world_uitest.json"), Path.Combine(worldDir, "world.json"), overwrite: true);
        try
        {
            Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "[F11] Другой вид", 5000),
                "Без тактической карты ПРОДОЛЖИТЬ должно показать пейзаж.");
            GameConsole.SendKey(game.Pid, ConsoleKey.F2);
            bool worldShown = GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Вы: Ключи", 5000);
            if (!worldShown) Dump(game.Pid);
            Assert.True(worldShown, "F2 должен показывать карту мира с героем.");
            TestPacing.Step();

            // F1 is available without tactical cells.
            GameConsole.SendKey(game.Pid, ConsoleKey.F1);
            Thread.Sleep(600);
            Assert.Contains("→ КАРТА ←", GameConsole.ReadRow(game.Pid, 1, GameConsole.TitleReadWidth));

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
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "[F11] Другой вид", 5000),
                "Во время путешествия должен открыться пейзаж на F1.");
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "разбиваешь лагерь", 20000),
                "Не появился рассказ мастера о пути (мок SendAction).");
            GameConsole.SendKey(game.Pid, ConsoleKey.F2);
            Assert.True(GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                r => r.Any(x => x.Contains("Вы: ")) && !r.Any(x => x.Contains("Вы: Ключи")), 8000),
                "После путешествия герой остался в Ключах.");
        }
        finally
        {
            game.Dispose();
            Restore(worldDir, original);
        }
    }

    private static Dictionary<string, byte[]> Snapshot(string path) => Directory.Exists(path)
        ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes)
        : [];

    private static void Restore(string path, Dictionary<string, byte[]> original)
    {
        if (Directory.Exists(path))
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                if (!original.ContainsKey(file)) File.Delete(file);
        foreach (var (file, bytes) in original)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);
        }
    }
}
