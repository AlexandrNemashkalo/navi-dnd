using System.Drawing.Imaging;
using System.Text.Json.Nodes;
using NaviDnD.Helpers;
using Xunit.Abstractions;

namespace NaviDnD.UiTests;

public sealed class ArtPicturesScenarioTests(ITestOutputHelper output)
{
    [Fact]
    public void NewCharacterShowsTheReviewedFaceInTheRunningGame()
    {
        using var game = GameSession.LaunchForNewGame();
        Assert.True(Navigation.ToAnketa(game.Pid));
        Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount,
            GameConsole.TitleReadWidth, "КАРТИНКИ", 5000));
        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        int row = rows.FindIndex(line => line.Contains("КАРТИНКИ"));
        if (!rows[row].Contains("DUNGEON CRAWL"))
        {
            GameConsole.SendLeftClick(game.Pid, rows[row].IndexOf("КАРТИНКИ") + 2, row);
        }
        Assert.True(GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount,
            GameConsole.TitleReadWidth, "ПОРТРЕТ 1", 5000), string.Join("\n", GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth)));
        var previous = ArtPictures.Current;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e");
            var expected = SvgToBrailleConverter.Convert(art.PortraitsForGroup("human")[0], "", applyPackMapping: false)!;
            var signatures = expected.Select(line => line.Trim()).Where(line => line.Length >= 8
                && line.Any(c => c is '▀' or '▄')).Distinct().ToArray();
            Assert.True(GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                screen => signatures.Count(signature => screen.Any(line => line.Contains(signature, StringComparison.Ordinal))) >= 3, 5000),
                "Expected:\n"+string.Join("\n",expected)+"\nActual:\n"+string.Join("\n",GameConsole.ReadRows(game.Pid,0,GameConsole.ScanRowCount,GameConsole.TitleReadWidth)));
            output.WriteLine("Character creation renders the reviewed 32px face, not a full-body map sprite.");
            rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
            int raceRow = rows.FindIndex(line => line.Contains("РАСА"));
            int raceCol = rows[raceRow].IndexOf("РАСА") + 1;
            GameConsole.SendLeftClick(game.Pid, raceCol, raceRow);
            var elf = SvgToBrailleConverter.Convert(art.PortraitsForGroup("elf")[0], "", applyPackMapping: false)!;
            var elfSignatures = elf.Select(line => line.Trim()).Where(line => line.Length >= 8 && line.Any(c => c is '▀' or '▄')).Distinct().ToArray();
            Assert.True(GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                screen => elfSignatures.Count(signature => screen.Any(line => line.Contains(signature, StringComparison.Ordinal))) >= 3, 5000));
            // High elf -> wood elf -> drow -> mountain dwarf -> hill dwarf -> halfling.
            for (int i = 0; i < 5; i++)
            {
                GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);
                Thread.Sleep(250);
            }
            void CheckFace(string group)
            {
                var picture = SvgToBrailleConverter.Convert(art.PortraitsForGroup(group)[0], "", applyPackMapping: false)!;
                var parts = picture.Select(line => line.Trim()).Where(line => line.Length >= 8 && line.Any(c => c is '▀' or '▄')).Distinct().ToArray();
                Assert.True(GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                    screen => parts.Count(part => screen.Any(line => line.Contains(part, StringComparison.Ordinal))) >= 3, 5000), group);
            }
            CheckFace("halfling");
            GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow); // Gnome shares the halfling gallery.
            CheckFace("halfling");
            Thread.Sleep(250);
            GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);
            CheckFace("elf"); // Half-elf.
            GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);
            CheckFace("orc"); // Half-orc.
            GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);
            CheckFace("dragonborn");
            GameConsole.SendKey(game.Pid, ConsoleKey.RightArrow);
            CheckFace("tiefling");
        }
        finally { ArtPictures.Current = previous; }
    }

    [Fact]
    public void UtilityPicturesAppearOnHoverInTheRunningGame()
    {
        string fixtureName = "art-pictures-"+Guid.NewGuid().ToString("N")+".json";
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        var state = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "full_game.json")))!;
        state["iconPack"] = CatalogSearch.DcssPack;
        string[] references = ["Spyglass", "Magnifying glass", "Hourglass", "Manacles", "Tent, two-person", "Hunting trap"];
        var names = references.Select(name => EquipmentDatabase.Find(name)!.Name).ToArray();
        state["hero"]!["inventory"] = new JsonArray(names.Select(name => (JsonNode)new JsonObject
        {
            ["name"] = name, ["quantity"] = 1, ["description"] = "", ["image"] = "lorc/swap-bag",
            ["equipmentedSlot"] = new JsonArray(), ["color"] = new JsonArray(220, 200, 160)
        }).ToArray());
        File.WriteAllText(fixturePath, state.ToJsonString());
        string artifactRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","artifacts","art-completion"));
        Directory.CreateDirectory(artifactRoot);
        try
        {
            using var game = GameSession.Launch(fixtureFileName: fixtureName, mockedActions: ["*"]);
            Navigation.ToCharacterScreen(game.Pid);
            var art = ArtPictures.Load("Dnd5e");
            var previous = ArtPictures.Current;
            ArtPictures.Current = art;
            try
            {
                foreach (var (reference, name) in references.Zip(names))
                {
                    Assert.True(GameConsole.WaitForAnyRowContains(game.Pid,0,GameConsole.ScanRowCount,GameConsole.TitleReadWidth,name,5000),name);
                    var rows = GameConsole.ReadRows(game.Pid,0,GameConsole.ScanRowCount,GameConsole.TitleReadWidth);
                    int row = rows.FindIndex(line => line.Contains(name, StringComparison.Ordinal));
                    int col = rows[row].IndexOf(name, StringComparison.Ordinal)+2;
                    var expected = SvgToBrailleConverter.Convert(art.Find(reference,"item").Single(),"",applyPackMapping:false)!;
                    var signatures = expected.Select(line => line.Trim()).Where(line => line.Length>=8 && line.Any(c => c is '▀' or '▄')).Distinct().ToArray();
                    GameConsole.SendMouseMove(game.Pid,col,row);
                    Assert.True(GameConsole.WaitForRowsMatch(game.Pid,0,GameConsole.ScanRowCount,GameConsole.TitleReadWidth,
                        screen => signatures.Count(signature => screen.Any(line => line.Contains(signature,StringComparison.Ordinal)))>=Math.Min(3,signatures.Length),5000),
                        "Named utility art did not appear: "+reference);
                    string safeName=reference.Split(',')[0].Replace(' ','-').ToLowerInvariant();
                    var actual = GameConsole.ReadRows(game.Pid,0,GameConsole.ScanRowCount,GameConsole.TitleReadWidth);
                    File.WriteAllLines(Path.Combine(artifactRoot,"game-"+safeName+".txt"),actual);
                    // ConPTY hosts expose a readable game buffer but no GDI client rectangle.
                    // Screenshots remain an explicit, strict opt-in on a visible classic-console host.
                    if (Environment.GetEnvironmentVariable("NAVIDND_CAPTURE_GAME_ART") == "1")
                        GameConsole.CaptureScreenshot(game.Pid,Path.Combine(artifactRoot,"game-"+safeName+".png"));
                    output.WriteLine(reference+": matched native half-block picture in the real console.");
                    GameConsole.SendMouseMove(game.Pid,2,2);
                }
            }
            finally { ArtPictures.Current = previous; }
        }
        finally { File.Delete(fixturePath); }
    }
}
