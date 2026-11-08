using System.Text.Json;
using System.Text.Json.Nodes;

namespace NaviDnD.UiTests;

public class AnimationDragTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MapsCanBeDraggedDuringWaitingAndDice(bool rolling, bool worldMap)
    {
        string fixtureName = "animation-" + Guid.NewGuid().ToString("N") + ".json";
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        try
        {
            string worldId = "animation-" + Guid.NewGuid().ToString("N");
            var state = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures",
                worldMap ? "world_location_game.json" : "full_game.json")))!;
            if (worldMap) state["world"]!["id"] = worldId;
            File.WriteAllText(fixturePath, state.ToJsonString());
            using var game = GameSession.Launch(fixtureFileName: fixtureName, mockedActions: ["SendAction"], mockResponsesFolder: "DiceRoll");
            if (worldMap)
            {
                string worldDir = Path.Combine(game.RealStorageDir, "Worlds", worldId);
                Directory.CreateDirectory(worldDir);
                var world = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world_uitest.json")))!;
                world["id"] = worldId;
                File.WriteAllText(Path.Combine(worldDir, "world.json"), world.ToJsonString());
            }
            Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
            Assert.True(GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000));
            GameConsole.SendText(game.Pid, "wait");
            GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount,
                GameConsole.TitleReadWidth, "Ожидание", 2000));

            string requestPath = Path.Combine(game.RealStorageDir, "roll_request.json");
            if (rolling)
            {
                File.WriteAllText(requestPath, JsonSerializer.Serialize(new
                {
                    history = Array.Empty<object>(), difficulty = 15,
                    modifiers = new[] { new { name = "test", value = 2 } }
                }));
                Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount,
                    GameConsole.TitleReadWidth, "бросить", 2500));
                GameConsole.SendKey(game.Pid, ConsoleKey.Spacebar);
                Thread.Sleep(120);
            }

            if (worldMap)
            {
                GameConsole.SendKey(game.Pid, ConsoleKey.F2);
                Assert.True(GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth,
                    "→ МИР ←", 1200));
                GameConsole.SendMouseWheel(game.Pid, 45, 10, 3);
                Thread.Sleep(150);
            }

            // Restrict comparison to the map; dice/spinner/history must not satisfy this assertion.
            string before = string.Join("\n", GameConsole.ReadRows(game.Pid, 3, 12, 65));
            GameConsole.SendDrag(game.Pid, 45, 10, 25, 14);
            Assert.True(GameConsole.WaitForRowsMatch(game.Pid, 3, 12, 65,
                rows => string.Join("\n", rows) != before, 1200),
                "Map viewport did not move while waiting/rolling.");
            if (rolling)
            {
                Assert.True(GameConsole.WaitForAnyRowContainsAny(game.Pid, 0, GameConsole.ScanRowCount,
                    GameConsole.TitleReadWidth, ["УСПЕХ", "ПРОВАЛ"], 7000) != null,
                    "Dragging interrupted the dice result.");
            }
        }
        finally { File.Delete(fixturePath); }
    }
}
