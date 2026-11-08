using NaviDnD.Display;

namespace NaviDnD.UiTests;

public class TravelPreviewScenarioTests
{
    [Fact]
    public void MapWithoutCellsShowsScene_AndWorldRemainsSeparate()
    {
        var game = GameSession.Launch("world_game.json", mockedActions: ["*"]);
        string worldDir = Path.Combine(game.RealStorageDir, "Worlds", "world-uitest");
        string worldFile = Path.Combine(worldDir, "world.json");
        byte[]? originalWorld = File.Exists(worldFile) ? File.ReadAllBytes(worldFile) : null;
        try
        {
            Directory.CreateDirectory(worldDir);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world_uitest.json"), worldFile, overwrite: true);

            Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, 60, 150, "[F11] Другой вид", 10000));
            var before = GameConsole.ReadRows(game.Pid, 3, 33, 90);
            Assert.True(before.Sum(row => row.Count(c => c is >= '\u2580' and <= '\u259f' or >= '\u2800' and <= '\u28ff')) >= 87 * 33);
            GameConsole.SendKey(game.Pid, ConsoleKey.F11);
            Assert.True(GameConsole.WaitForRowsMatch(game.Pid, 3, 33, 90,
                rows => !rows.SequenceEqual(before), 5000));
            GameConsole.SendKey(game.Pid, ConsoleKey.F12);
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, 60, 150, "Анимация: пауза", 5000));
            GameConsole.SendKey(game.Pid, ConsoleKey.F2);
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, 60, 150, "Вы: Ключи", 5000));
            GameConsole.SendKey(game.Pid, ConsoleKey.F1);
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, 60, 150, "[F11] Другой вид", 5000));

        }
        finally
        {
            game.Dispose(); // stop saving before restoring any pre-existing fixture world
            if (originalWorld != null)
            {
                Directory.CreateDirectory(worldDir);
                File.WriteAllBytes(worldFile, originalWorld);
            }
            else if (File.Exists(worldFile)) File.Delete(worldFile);
        }
    }
}
