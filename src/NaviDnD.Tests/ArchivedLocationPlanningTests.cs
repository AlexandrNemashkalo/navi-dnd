using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

public class ArchivedLocationPlanningTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "navidnd_archived_plan_" + Guid.NewGuid().ToString("N"));
    private readonly string worldId = "archived-" + Guid.NewGuid().ToString("N");
    private string StatePath => Path.Combine(directory, "state.json");
    private string WorldsDirectory => Path.Combine(directory, "Worlds");
    private string WorldDirectory => Path.Combine(WorldsDirectory, worldId);
    private string WorldPath => Path.Combine(WorldDirectory, "world.json");
    private string LocationsDirectory => Path.Combine(WorldDirectory, "locations");
    private string LocationPath => Path.Combine(LocationsDirectory, "harbor.json");
    private string FlagPath => Path.Combine(directory, Storage.LocationPlannedFlag);

    public static IEnumerable<object[]> ArchiveCases() =>
        from snapshot in new[] { "missing", "corrupt", "valid" }
        from oldName in new[] { false, true }
        from explicitCoordinates in new[] { false, true }
        select new object[] { snapshot, oldName, explicitCoordinates };

    [Theory, MemberData(nameof(ArchiveCases))]
    public void PlanCannotReplaceAnArchivedCurrentPlace(string snapshot, bool oldName, bool explicitCoordinates)
    {
        Directory.CreateDirectory(LocationsDirectory);
        var world = new WorldMap
        {
            Id = worldId, Width = 8, Height = 4,
            Biomes = Enumerable.Repeat("........", 4).ToList(),
            Roads = Enumerable.Repeat("00000000", 4).ToList(),
            Rivers = Enumerable.Repeat("00000000", 4).ToList(),
            Owners = Enumerable.Repeat("........", 4).ToList(),
            Chronicle = new() { Places = [new()
            {
                Name = "New Harbor", FormerNames = ["Old Harbor"], Type = WorldPlaceTypes.Village,
                X = 1, Y = 1, Location = "harbor.json", Description = "The repaired landing remains open."
            }] }
        };
        File.WriteAllText(WorldPath, JsonSerializer.Serialize(world));
        string? archive = snapshot switch
        {
            "corrupt" => "{interrupted saved map",
            "valid" => """{"objects":[{"name":"Opened cache","aiInfo":["The unique token was taken"]}]}""",
            _ => null
        };
        if (archive != null) File.WriteAllText(LocationPath, archive);
        var state = new WorldState
        {
            Hero = new() { Name = "Explorer", Symbol = "HRO", Hp = "7/10", Position = [1, 1],
                Inventory = [new() { Name = "Unique token", Quantity = 1 }] },
            World = new() { Id = worldId, Place = oldName ? "Old Harbor" : "New Harbor", X = 1, Y = 1 }
        };
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        Assert.NotNull(WorldLibrary.LoadFrom(WorldPath));
        string stateBefore = File.ReadAllText(StatePath), worldBefore = File.ReadAllText(WorldPath);
        string plan = explicitCoordinates
            ? """{"blocks":[{"x":0,"y":0,"terrain":"forest","purpose":"Replacement map"}],"worldX":6,"worldY":2}"""
            : """{"blocks":[{"x":0,"y":0,"terrain":"forest","purpose":"Replacement map"}]}""";

        var tools = new MapGenTools(new McpServerConfig(StatePath, LogPath: null));
        string result = tools.PlanLocation(plan);

        using var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
        Assert.Equal(stateBefore, File.ReadAllText(StatePath));
        Assert.Equal(worldBefore, File.ReadAllText(WorldPath));
        Assert.False(File.Exists(FlagPath));
        if (archive == null) Assert.False(File.Exists(LocationPath));
        else Assert.Equal(archive, File.ReadAllText(LocationPath));
    }

    public void Dispose()
    {
        foreach (string path in new[] { StatePath, FlagPath, LocationPath, WorldPath })
            if (File.Exists(path)) File.Delete(path);
        foreach (string path in new[] { LocationsDirectory, WorldDirectory, WorldsDirectory, directory })
            if (Directory.Exists(path)) Directory.Delete(path);
    }
}
