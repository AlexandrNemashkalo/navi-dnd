using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NaviDnD.UiTests;

public class AnimationHoverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstStreamingVisitSupportsHover(bool journal)
    {
        using var game = GameSession.Launch(mockedActions: ["SendAction"], mockResponsesFolder: "HoverTyping");
        Navigation.MenuSelect(game.Pid, "\u041f\u0420\u041e\u0414\u041e\u041b\u0416\u0418\u0422\u042c");
        Assert.True(GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "\u2192 \u041a\u0410\u0420\u0422\u0410 \u2190", 5000));
        GameConsole.SendText(game.Pid, "wait");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
        Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount,
            GameConsole.TitleReadWidth, "tick000", 15000));
        GameConsole.SendKey(game.Pid, journal ? ConsoleKey.F3 : ConsoleKey.F4);
        string marker = journal ? "\u0417\u0410\u0414\u0410\u041d\u0418\u042f" : "\u0414\u043b\u0438\u043d\u043d\u044b\u0439 \u043c\u0435\u0447";
        Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 3, GameConsole.ScanRowCount - 3,
            GameConsole.TitleReadWidth, marker, 3000));
        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        int row = rows.FindIndex(r => r.Contains(marker));
        int col = journal ? 8 : rows[row].IndexOf(marker, StringComparison.Ordinal) + 2;
        if (journal) row += 3;
        int Progress() => Regex.Matches(string.Join("\n", GameConsole.ReadRows(game.Pid, 0,
            GameConsole.ScanRowCount, GameConsole.TitleReadWidth)), @"tick(\d{3})")
            .Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max();
        int previous = Progress();
        for (int window = 0; window < 3; window++)
        {
            for (int move = 0; move < 8; move++)
            {
                GameConsole.SendMouseMove(game.Pid, move % 2 == 0 ? col : 2, move % 2 == 0 ? row : 2);
                Thread.Sleep(125);
            }
            int next = Progress();
            Assert.True(next > previous, $"Typing stalled on tab: {previous} -> {next}");
            previous = next;
        }
        GameConsole.SendMouseMove(game.Pid, col, row);
        Assert.True(GameConsole.WaitForRowsMatch(game.Pid, row, 1, GameConsole.TitleReadWidth,
            r => r[0].Contains("\u2190"), 3000), "Hover selection did not update on first streaming visit.");
    }

    [Fact]
    public void TextKeepsAdvancingWhileMapSelectionChanges()
    {
        string fixtureName = "hover-" + Guid.NewGuid().ToString("N") + ".json";
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        var state = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "legend_visibility_game.json")))!;
        state["map"]!["entities"]![0]!["image"] = "zeromancer/heart-plus.svg";
        File.WriteAllText(fixturePath, state.ToJsonString());
        try
        {
            using var game = GameSession.Launch(fixtureFileName: fixtureName,
                mockedActions: ["SendAction"], mockResponsesFolder: "HoverTyping");
            Navigation.MenuSelect(game.Pid, "\u041f\u0420\u041e\u0414\u041e\u041b\u0416\u0418\u0422\u042c");
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 3, GameConsole.ScanRowCount - 3, 90, "NPC", 5000), string.Join("\n", GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth)));
            var map = GameConsole.ReadRows(game.Pid, 3, GameConsole.ScanRowCount - 3, 90);
            int row = map.FindIndex(r => r.Contains("NPC"));
            int col = map[row].IndexOf("NPC", StringComparison.Ordinal) + 1;
            GameConsole.SendText(game.Pid, "wait");
            GameConsole.SendKey(game.Pid, ConsoleKey.Enter);
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount,
                GameConsole.TitleReadWidth, "tick000", 15000));
            int Progress() => Regex.Matches(string.Join("\n", GameConsole.ReadRows(game.Pid, 0,
                GameConsole.ScanRowCount, GameConsole.TitleReadWidth)), @"tick(\d{3})")
                .Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max();
            int previous = Progress();
            for (int window = 0; window < 4; window++)
            {
                for (int move = 0; move < 10; move++)
                {
                    GameConsole.SendMouseMove(game.Pid, move % 2 == 0 ? col : 2,
                        move % 2 == 0 ? row + 3 : 2);
                    Thread.Sleep(100);
                }
                int next = Progress();
                Assert.True(next > previous, $"Typing stopped during hover: {previous} -> {next}.");
                previous = next;
            }
            GameConsole.SendMouseMove(game.Pid, col, row + 3);
            Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 3, 20,
                GameConsole.TitleReadWidth, "\u2190", 3000), "Map selection marker did not update.");
        }
        finally { File.Delete(fixturePath); }
    }
}
