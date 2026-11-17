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

    private GameActionTools CreateTools(string heroPositionJson, string doorsJson, string entitiesJson = "[]")
    {
        string json = """
            {"hero":{"symbol":"HRO","name":"Тестер","hp":"10/10","position":HERO_POSITION},
             "map":{"cols":20,"rows":15,"doors":DOORS,"entities":ENTITIES}}
            """
            .Replace("HERO_POSITION", heroPositionJson)
            .Replace("DOORS", doorsJson).Replace("ENTITIES", entitiesJson);
        string tempPath = Path.Combine(Path.GetTempPath(), $"navidnd_test_{Guid.NewGuid():N}.json");
        _files.Add(tempPath);
        File.WriteAllText(tempPath, json);
        return new GameActionTools(new McpServerConfig(tempPath, LogPath: null));
    }

    private const string OneDoor = """[{"from":[5,5],"to":[6,5],"isDoor":true,"color":[139,90,43],"isDoorOpen":false}]""";

    [Fact]
    public void NpcAtDoorCanOpenItWithoutMovingTheDistantHero()
    {
        var tools = CreateTools("[15,10]", OneDoor, """[{"name":"Keeper","hp":"10/10","position":[5,5]}]""");
        using var result = Parse(tools.DoorAction(0, "open", 0));
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
        var storage = new Storage();
        storage.ApplySavedState("""{"hero":{"position":[15,10]},"map":{"doors":[{"from":[5,5],"to":[6,5],"isDoor":true,"isDoorOpen":false}]}}""");
        storage.ApplyUpdateWorldState(result.RootElement.GetProperty("patch").GetRawText());
        Assert.True(storage.WorldState.Map.Doors![0].IsDoorOpen);
        Assert.Equal(new[] { 15, 10 }, storage.WorldState.Hero!.Position);
        Assert.False(Parse(tools.DoorAction(0, "open")).RootElement.GetProperty("success").GetBoolean());
    }

    [Theory]
    [InlineData("[{\"position\":[7,5],\"hp\":\"10/10\"}]", 0)]
    [InlineData("[{\"position\":[5,5],\"hp\":\"10/10\",\"deleted\":true}]", 0)]
    [InlineData("[{\"position\":[5,5],\"hp\":\"0/10\"}]", 0)]
    [InlineData("[]", 0)]
    [InlineData("[]", -2)]
    public void InvalidOrDistantNpcCannotBorrowTheHerosPosition(string entities, int id)
    {
        var tools = CreateTools("[5,5]", OneDoor, entities);
        using var result = Parse(tools.DoorAction(0, "open", id));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.False(result.RootElement.TryGetProperty("patch", out _));
    }

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
    [InlineData(true, 5, "night")]
    [InlineData(false, 4, "evening")]
    public async Task DetourThenRestKeepsFinalTimeConsistentWithHistory(bool isLong, int expectedDay, string expectedPart)
    {
        string path = Path.Combine(Path.GetTempPath(), $"navidnd_test_{Guid.NewGuid():N}.json");
        _files.Add(path);
        string saved = """{"time":{"day":3,"partOfDay":"evening","totalRounds":1},"hero":{"name":"Traveler","hp":"12/12","speedMax":30}}""";
        File.WriteAllText(path, saved);
        var tools = new GameActionTools(new McpServerConfig(path, LogPath: null));
        string rest = isLong ? tools.LongRest(elapsedDays: 1) : tools.ShortRest(elapsedDays: 1);
        Assert.Equal(saved, File.ReadAllText(path)); // tools return patches; the player applies them in order
        Assert.True(Parse(tools.LongRest(-1)).RootElement.TryGetProperty("error", out _));
        var storage = new Storage();
        storage.ApplySavedState(saved);
        string response = "{\"history\":[{\"text\":\"Detour.\",\"patch\":{\"time\":{\"day\":4,\"partOfDay\":\"evening\"}}},{\"text\":\"Rest complete.\",\"patch\":" + rest + "}]}";
        var type = typeof(AppConfig).Assembly.GetType("NaviDnD.Clients.SequentialHistoryPlayer")!;
        var player = Activator.CreateInstance(type, storage.WorldState, storage, new JsonSerializerOptions(), null, null, null, null)!;
        await (Task)type.GetMethod("PlayAsync")!.Invoke(player, [response])!;
        Assert.Equal(expectedDay, storage.WorldState.Time.Day);
        Assert.Equal(expectedPart, storage.WorldState.Time.PartOfDay);
        Assert.Equal(30, storage.WorldState.Hero!.SpeedLeft);
    }

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
        Assert.Equal(isLong ? "night" : "evening", storage.WorldState.Time.PartOfDay);
        Assert.Equal(5, int.Parse(hero.Hp.Split('/')[0])); // healing and resources - separate wizard patch
    }

    [Theory]
    [InlineData(100, "morning", 1, 700, "morning", 1, 0)]
    [InlineData(3500, "evening", 1, 500, "night", 2, 3600)]
    public void ShortRestPreservesFutureDeadlinesAndDeliversDueConsequences(int round, string part, int day,
        int expectedRound, string expectedPart, int expectedDay, int offset)
    {
        string path = Path.Combine(Path.GetTempPath(), $"navidnd_test_{Guid.NewGuid():N}.json");
        _files.Add(path);
        string state = JsonSerializer.Serialize(new
        {
            time = new { totalRounds = round, partOfDay = part, day },
            hero = new { name = "Sleeper", position = new[] { 1, 1 }, effects = new object[] {
                new { name = "Day-long protection", expiresAtRound = round + 14400 },
                new { name = "Delayed consequence", expiresAtRound = round + 10, onExpire = new { effect = "A bell rings" } } },
                inventory = new[] { new { name = "Lantern", effects = new[] { new { name = "Light", expiresAtRound = round + 14400 } } } } },
            map = new { entities = new[] { new { name = "Watcher", hp = "10/10", position = new[] { 2, 1 }, effects = new[] {
                new { name = "Delayed waking", expiresAtRound = round + 10, onExpire = new { effect = "Wake up" } } } } },
                objects = new[] { new { name = "Seal", position = new[] { 3, 1 }, effects = new[] { new { name = "Protection", expiresAtRound = round + 14400 } } } } },
            scheduledEvents = new[] { new { name = "Courier", fireAtRound = round + 20, effect = "Arrives" },
                new { name = "Tomorrow", fireAtRound = round + 14400, effect = "Later" } }
        });
        File.WriteAllText(path, state);
        var storage = new Storage();
        storage.ApplySavedState(state);
        storage.ApplyUpdateWorldState(new GameActionTools(new McpServerConfig(path, LogPath: null)).ShortRest());
        var ws = storage.WorldState;
        Assert.Equal((expectedRound, expectedPart, expectedDay), (ws.Time.TotalRounds, ws.Time.PartOfDay, ws.Time.Day));
        Assert.Equal(round + 14400 - offset, ws.Hero!.Effects![0].ExpiresAtRound);
        Assert.Equal(round + 14400 - offset, ws.Hero.Inventory[0].Effects![0].ExpiresAtRound);
        Assert.Equal(round + 14400 - offset, ws.Map.Objects![0].Effects![0].ExpiresAtRound);
        Assert.Equal(round + 14400 - offset, ws.ScheduledEvents![1].FireAtRound);
        var due = storage.TakeRoundBundle()!;
        Assert.Equal(2, due.ExpiredEffects.Count);
        Assert.Equal("Courier", Assert.Single(due.FiredScheduledEvents).Item2.Name);
        Assert.Null(storage.TakeRoundBundle());
    }

}
