using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

public class PlotThreadStatusTests
{
    [Theory]
    [InlineData(" СЛУХ ", "rumor", "Rumor")]
    [InlineData("Rumour", "rumor", "Rumor")]
    [InlineData("Предложено", "offered", "Offered")]
    [InlineData("АКТИВЕН", "active", "Active")]
    [InlineData("Раскрыта", "revealed", "Revealed")]
    [InlineData("Завершён", "completed", "Completed")]
    [InlineData("Завершен", "completed", "Completed")]
    [InlineData("Выполнена", "completed", "Completed")]
    [InlineData("done", "completed", "Completed")]
    [InlineData("finished", "completed", "Completed")]
    [InlineData("Провалено", "failed", "Failed")]
    [InlineData("Offered", "offered", "Offered")]
    [InlineData("Active", "active", "Active")]
    [InlineData("Revealed", "revealed", "Revealed")]
    [InlineData("Completed", "completed", "Completed")]
    [InlineData("Failed", "failed", "Failed")]
    public void KnownAliasesProduceStableKeysAndEnglishLabels(string alias, string key, string label)
    {
        Assert.Equal(key, PlotThreadStatuses.Normalize(alias));
        Assert.Equal(label, PlotThreadStatuses.Label(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("  Ожидает ответа совета  ")]
    [InlineData("completed after the festival")]
    [InlineData("Не завершено")]
    [InlineData("Выполнить позже")]
    public void CustomAndAbsentStatusesArePreservedExactly(string? value)
    {
        Assert.Equal(value, PlotThreadStatuses.Normalize(value));
        Assert.Equal(value, PlotThreadStatuses.Label(value));
    }

    [Fact]
    public void PatchesNormalizeBeforeHeroExistsAndOmittedStatusKeepsPreviousValue()
    {
        var storage = new Storage();
        storage.WorldState.Hero = null;
        storage.ApplyUpdateWorldState("""
            {"narrative":{"plotThreads":[{"name":"The bridge","status":"Активен"}]}}
            """);
        Assert.Equal(PlotThreadStatuses.Active, Assert.Single(storage.WorldState.Narrative!.PlotThreads).Status);
        storage.ApplyUpdateWorldState("""
            {"narrative":{"plotThreads":[{"id":0,"description":"Found the key","steps":["Found the key"]}]}}
            """);
        var thread = Assert.Single(storage.WorldState.Narrative.PlotThreads);
        Assert.Equal(PlotThreadStatuses.Active, thread.Status);
        Assert.Contains("Found the key", thread.Steps!);
        storage.ApplyUpdateWorldState("""
            {"narrative":{"plotThreads":[{"id":0,"status":"Завершено"}]}}
            """);
        Assert.Equal(PlotThreadStatuses.Completed, storage.WorldState.Narrative.PlotThreads[0].Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  Awaiting the council  ")]
    public void ExplicitNullAndCustomPatchSurviveSaveAndContext(string? status)
    {
        var storage = new Storage();
        storage.ApplySavedState("""
            {"narrative":{"plotThreads":[{"name":"The bridge","status":"active"}]}}
            """);
        storage.ApplyUpdateWorldState(JsonSerializer.Serialize(new
            { narrative = new { plotThreads = new[] { new { id = 0, status } } } }));
        Assert.Equal(status, storage.WorldState.Narrative!.PlotThreads[0].Status);
        var reloaded = new Storage();
        reloaded.ApplySavedState(JsonSerializer.Serialize(storage.WorldState));
        Assert.Equal(status, reloaded.WorldState.Narrative!.PlotThreads[0].Status);
        using var context = JsonDocument.Parse(new AiContextBuilder(reloaded.WorldState).Serialize("narrative.plotThreads"));
        var thread = context.RootElement.GetProperty("narrative").GetProperty("plotThreads")[0];
        Assert.Equal(status, thread.TryGetProperty("status", out var value) ? value.GetString() : null);
    }

    [Fact]
    public void SavedStateAndMcpExposeCanonicalStatusWithOriginalThreadId()
    {
        var storage = new Storage();
        storage.ApplySavedState("""
            {"narrative":{"plotThreads":[{"name":"Old task","status":"Завершено"},{"name":"The bridge","status":"Предложено"}]}}
            """);
        string path = Path.Combine(Path.GetTempPath(), $"navidnd_status_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(storage.WorldState,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            var tools = new WorldStateTools(new McpServerConfig(path, LogPath: null));
            using var response = JsonDocument.Parse(tools.GetWorldState(["narrative.plotThreads:#1"]));
            var thread = Assert.Single(response.RootElement.GetProperty("narrative").GetProperty("plotThreads").EnumerateArray());
            Assert.Equal(1, thread.GetProperty("id").GetInt32());
            Assert.Equal(PlotThreadStatuses.Offered, thread.GetProperty("status").GetString());
        }
        finally { File.Delete(path); }
    }
}
