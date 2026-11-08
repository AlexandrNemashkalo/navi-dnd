using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

public class MapPlanToolsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"navidnd_plan_{Guid.NewGuid():N}");
    private string StatePath => Path.Combine(_directory, "state.json");

    public MapPlanToolsTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StatePath, """{"hero":{"symbol":"HRO","name":"Тестер","hp":"10/10","position":[1,1]}}""");
    }

    public void Dispose()
    {
        foreach (string file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }

    [Fact]
    public void PlanToolBuildsIslandAndSavedGeometryKeepsApproach()
    {
        var tools = new MapGenTools(new McpServerConfig(StatePath, LogPath: null));
        string result = tools.PlanLocation("""{"blocks":[{"x":0,"y":0,"terrain":"building","building":"temple","moat":true,"purpose":"Храм за рвом"}],"startInside":true}""");
        Assert.DoesNotContain("\"error\"", result);
        var storage = new Storage();
        storage.ApplySavedState(File.ReadAllText(StatePath));
        var chunk = Assert.Single(storage.WorldState.Map.Chunks!);
        Assert.Equal("temple", chunk.Style);
        Assert.Equal(1, chunk.Terrain!.Sum(row => row.Count(c => c == '~')));
        Assert.Contains(chunk.Terrain, row => row.Contains('W'));
        var hero = storage.WorldState.Hero!;
        Assert.Contains(storage.WorldState.Map.Rooms!, room => room.Positions.Any(p => p.SequenceEqual(hero.Position)));
        Assert.False(storage.WorldState.Map.TerrainAt(hero.Position[0], hero.Position[1])!.BlocksMove);
        Assert.True(File.Exists(Path.Combine(_directory, Storage.LocationPlannedFlag)));
        using var saved = JsonDocument.Parse(File.ReadAllText(StatePath));
        Assert.False(saved.RootElement.GetProperty("map").GetProperty("chunks")[0].TryGetProperty("moat", out _));
    }

    [Theory]
    [InlineData("road", '=')]
    [InlineData("square", 'V')]
    [InlineData("river", 'W')]
    [InlineData("palisade", '#')]
    public void PlanToolAcceptsVillageLayout(string layout, char feature)
    {
        var tools = new MapGenTools(new McpServerConfig(StatePath, LogPath: null));
        string result = tools.PlanLocation($$"""{"blocks":[{"x":0,"y":0,"terrain":"village","layout":"{{layout}}"}]}""");
        Assert.DoesNotContain("\"error\"", result);
        var storage = new Storage(); storage.ApplySavedState(File.ReadAllText(StatePath));
        var chunk = Assert.Single(storage.WorldState.Map.Chunks!);
        Assert.Contains(chunk.Terrain!, row => row.Contains(feature));
    }

    [Theory]
    [InlineData("""{"blocks":[{"x":0,"y":0,"terrain":"forest","moat":true}]}""")]
    [InlineData("""{"blocks":[{"x":0,"y":0,"terrain":"building","layout":"river"}]}""")]
    [InlineData("""{"blocks":[{"x":0,"y":0,"terrain":"village","layout":"unknown"}]}""")]
    public void InvalidPlanLeavesSavedStateUntouched(string plan)
    {
        string before = File.ReadAllText(StatePath);
        var tools = new MapGenTools(new McpServerConfig(StatePath, LogPath: null));
        string result = tools.PlanLocation(plan);
        Assert.Contains("\"error\"", result);
        Assert.Equal(before, File.ReadAllText(StatePath));
        Assert.False(File.Exists(Path.Combine(_directory, Storage.LocationPlannedFlag)));
    }
}
