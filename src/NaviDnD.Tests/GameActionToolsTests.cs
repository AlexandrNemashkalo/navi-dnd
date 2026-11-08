using System.Text.Json;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

// Unit tests MCP- of the door_action tool without accessing the AI - they check the adjacency geometry itself.
// Real bug: the player stood “one cell” from the door (Chebyshev-distance 1 to the nearest end From/To) and
// door_action still opened it - the code allowed dist<=1, although the door is the border BETWEEN From and To,
// and not a separate cell: the hero must stand exactly in one of them.
public class GameActionToolsTests : IDisposable
{
    private readonly List<string> _files = [];

    // State files and everything the tool puts next to them (navidnd_test_<guid>*) - after each test.
    public void Dispose()
    {
        foreach (string path in _files)
            foreach (string file in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "*"))
                try { File.Delete(file); } catch (IOException) { }
    }

    private GameActionTools CreateTools(string heroPositionJson, string doorsJson)
    {
        string json = """
            {"hero":{"symbol":"HRO","name":"Тестер","hp":"10/10","position":HERO_POSITION},
             "map":{"cols":20,"rows":15,"doors":DOORS}}
            """
            .Replace("HERO_POSITION", heroPositionJson)
            .Replace("DOORS", doorsJson);
        string tempPath = Path.Combine(Path.GetTempPath(), $"navidnd_test_{Guid.NewGuid():N}.json");
        _files.Add(tempPath);
        File.WriteAllText(tempPath, json);
        return new GameActionTools(new McpServerConfig(tempPath, LogPath: null));
    }

    private const string OneDoor = """[{"from":[5,5],"to":[6,5],"isDoor":true,"color":[139,90,43],"isDoorOpen":false}]""";

    [Theory]
    [InlineData("open")]
    [InlineData("break")]
    [InlineData("hide")]
    public void WorldExitCannotBeChangedByDoorAction(string action)
    {
        var tools = CreateTools("[3,3]", """[{"from":[3,3],"to":[4,3],"isDoor":true,"isWorldExit":true,"isDoorOpen":false}]""");
        var result = Parse(tools.DoorAction(0, action));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.False(result.RootElement.TryGetProperty("patch", out _));
    }

    [Fact]
    public void DoorAction_HeroStandingOnFrom_Succeeds()
    {
        var tools = CreateTools("[5,5]", OneDoor);
        var result = Parse(tools.DoorAction(0, "open"));
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void DoorAction_HeroStandingOnTo_Succeeds()
    {
        var tools = CreateTools("[6,5]", OneDoor);
        var result = Parse(tools.DoorAction(0, "close"));
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
    }

    // Real bug: the hero is at [7,5] - one cell FURTHER than the end of the door To:[6,5] (Chebyshev-distance
    // to the nearest end = 1). There is a whole cell [6,5] between the hero and the door; he did not reach it.
    [Fact]
    public void DoorAction_HeroOneCellPastDoor_Fails()
    {
        var tools = CreateTools("[7,5]", OneDoor);
        var result = Parse(tools.DoorAction(0, "open"));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("too far away", result.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void DoorAction_HeroFarAway_Fails()
    {
        var tools = CreateTools("[15,10]", OneDoor);
        var result = Parse(tools.DoorAction(0, "open"));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
    }

    // hide/reveal - non-physical interaction (DM marks secret / reveals what was found), adjacency is not
    // required.
    [Fact]
    public void DoorAction_HideAndReveal_DoNotRequireAdjacency()
    {
        var tools = CreateTools("[15,10]", OneDoor);

        var hideResult = Parse(tools.DoorAction(0, "hide"));
        Assert.True(hideResult.RootElement.GetProperty("success").GetBoolean());

        var revealResult = Parse(tools.DoorAction(0, "reveal"));
        Assert.True(revealResult.RootElement.GetProperty("success").GetBoolean());
    }

    // break - physical action (the hero chops/knocks down a door), requires the same adjacency as open/close.
    [Fact]
    public void DoorAction_Break_RequiresAdjacency()
    {
        var farTools = CreateTools("[15,10]", OneDoor);
        Assert.False(Parse(farTools.DoorAction(0, "break")).RootElement.GetProperty("success").GetBoolean());

        var nearTools = CreateTools("[5,5]", OneDoor);
        Assert.True(Parse(nearTools.DoorAction(0, "break")).RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void DoorAction_Passage_NotAPhysicalDoor_Refuses()
    {
        const string passage = """[{"from":[5,5],"to":[6,5],"isDoor":false}]""";
        var tools = CreateTools("[5,5]", passage);
        var result = Parse(tools.DoorAction(0, "open"));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void DoorAction_UnknownDoorId_ReturnsError()
    {
        var tools = CreateTools("[5,5]", OneDoor);
        var result = Parse(tools.DoorAction(5, "open"));
        Assert.True(result.RootElement.TryGetProperty("error", out _));
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestRemovesOnlyTimedAndEligibleEffects(bool isLong)
    {
        string path = Path.Combine(Path.GetTempPath(), $"navidnd_test_{Guid.NewGuid():N}.json");
        _files.Add(path);
        File.WriteAllText(path, """
            {"time":{"totalRounds":5,"partOfDay":"evening","day":1},
             "hero":{"symbol":"HRO","hp":"5/10","speedMax":30,"speedLeft":0,
              "actions":[{"name":"Реакция","value":0,"maxValue":1}],
              "effects":[{"name":"Щит","expiresAtRound":6},
                         {"name":"Усталость","untilLongRest":true},
                         {"name":"Проклятие","description":"Пока не снято заклинанием"},
                         {"name":"Болезнь","description":"До исцеления"}]}}
            """);
        var tools = new GameActionTools(new McpServerConfig(path, LogPath: null));
        var storage = new Storage();
        storage.ApplySavedState(File.ReadAllText(path));
        storage.ApplyUpdateWorldState(isLong ? tools.LongRest() : tools.ShortRest());
        var hero = storage.WorldState.Hero!;
        Assert.DoesNotContain(hero.Effects!, e => e.Name == "Щит");
        Assert.Equal(!isLong, hero.Effects!.Any(e => e.Name == "Усталость"));
        Assert.Contains(hero.Effects!, e => e.Name == "Проклятие");
        Assert.Contains(hero.Effects!, e => e.Name == "Болезнь");
        Assert.Equal(30, hero.SpeedLeft);
        Assert.Equal(1, hero.Actions![0].Value);
        Assert.Equal(isLong ? "morning" : "night", storage.WorldState.Time.PartOfDay);
        Assert.Equal(5, int.Parse(hero.Hp.Split('/')[0])); // healing and resources - separate wizard patch
    }

}
