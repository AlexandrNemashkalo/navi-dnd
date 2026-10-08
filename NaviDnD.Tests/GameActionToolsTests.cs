using System.Text.Json;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

// Юнит-тесты MCP-инструмента door_action без обращения к нейронке — проверяют саму геометрию
// смежности. Реальный баг: игрок стоял "через одну клетку" от двери (Chebyshev-дистанция 1 до
// ближайшего конца From/To) и door_action всё равно её открыл — код разрешал dist<=1, хотя дверь
// это граница МЕЖДУ From и To, а не отдельная клетка: герой должен стоять ровно в одной из них.
public class GameActionToolsTests : IDisposable
{
    private readonly List<string> _files = [];

    // Файлы состояния и всё, что инструмент кладёт рядом с ними (navidnd_test_<guid>*), — после каждого теста.
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

    // Реальный баг: герой на [7,5] — на одну клетку ДАЛЬШЕ конца двери To:[6,5] (Chebyshev-дистанция
    // до ближайшего конца = 1). Между героем и дверью — целая клетка [6,5], он до неё не дошёл.
    [Fact]
    public void DoorAction_HeroOneCellPastDoor_Fails()
    {
        var tools = CreateTools("[7,5]", OneDoor);
        var result = Parse(tools.DoorAction(0, "open"));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("далеко", result.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void DoorAction_HeroFarAway_Fails()
    {
        var tools = CreateTools("[15,10]", OneDoor);
        var result = Parse(tools.DoorAction(0, "open"));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
    }

    // hide/reveal — не физическое взаимодействие (ДМ помечает секрет / раскрывает найденное),
    // смежность не требуется.
    [Fact]
    public void DoorAction_HideAndReveal_DoNotRequireAdjacency()
    {
        var tools = CreateTools("[15,10]", OneDoor);

        var hideResult = Parse(tools.DoorAction(0, "hide"));
        Assert.True(hideResult.RootElement.GetProperty("success").GetBoolean());

        var revealResult = Parse(tools.DoorAction(0, "reveal"));
        Assert.True(revealResult.RootElement.GetProperty("success").GetBoolean());
    }

    // break — физическое действие (герой рубит/выбивает дверь), требует такой же смежности, как
    // open/close.
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
}
