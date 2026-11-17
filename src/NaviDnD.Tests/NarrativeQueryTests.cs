using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

public class NarrativeQueryTests : IDisposable
{
    [Theory]
    [InlineData("#1", 1)]
    [InlineData("#999", 0)]
    [InlineData("#bad", 0)]
    [InlineData("#1;Other", 2)]
    public void NamedMapArraysAcceptExplicitOriginalIds(string filter, int count)
    {
        state.Map.Objects = [new() { Name = "Other", Position = [1,1] },
            new() { Name = "Pedestal", Position = [2,1], AiInfo = ["Original inscription CEDAR-47"] }];
        using var result = Query("map.objects:" + filter);
        var objects = result.RootElement.GetProperty("map").GetProperty("objects");
        Assert.Equal(count, objects.GetArrayLength());
        if (count > 0)
        {
            var pedestal = Assert.Single(objects.EnumerateArray(), item => item.GetProperty("id").GetInt32() == 1);
            Assert.Contains("CEDAR-47", pedestal.GetProperty("aiInfo").ToString());
        }
    }

    private readonly string directory = Path.Combine(Path.GetTempPath(), "navidnd-memory-query-" + Guid.NewGuid().ToString("N"));
    private readonly WorldState state = new();
    private string StatePath => Path.Combine(directory, "state.json");

    public NarrativeQueryTests()
    {
        Directory.CreateDirectory(directory);
        state.Narrative = new()
        {
            Npcs = Enumerable.Range(0, 1000).Select(i => new NpcRecord
            {
                Name = $"Resident {i}", Notes = "Ordinary life", Memory = [],
            }).ToList(),
            PlotThreads = Enumerable.Range(0, 1000).Select(i => new PlotThread
            {
                Name = $"Journey {i}", Description = "Old road", Steps = [],
            }).ToList(),
        };
        state.Narrative.Npcs[8].Deleted = true;
        state.Narrative.Npcs[43].Name = "Мира";
        state.Narrative.Npcs[43].Memory = ["The silver key opens the ferry gate"];
        state.Narrative.Npcs[43].Portrait = "private-portrait";
        state.Narrative.Npcs[43].SpeechVoice = new() { Speaker = "private-speaker" };
        state.Narrative.PlotThreads[4].Deleted = true;
        state.Narrative.PlotThreads[891].Name = "Переправа";
        state.Narrative.PlotThreads[891].Steps = ["Мира gave the hero a silver key"];
        state.Narrative.PlotThreads[891].DmNotes = "A hidden room waits beyond the sluice";
        state.Map.Doors = [new() { From = [1, 1], To = [2, 1] }];
    }

    private JsonDocument Query(params string[] keys)
    {
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        return JsonDocument.Parse(new WorldStateTools(new(StatePath, LogPath: null)).GetWorldState(keys));
    }

    [Fact]
    public void SearchesOldMemoriesAndStepsWithoutLosingPatchIds()
    {
        using var answer = Query("narrative.npcs:~SILVER KEY", "narrative.plotThreads:~silver key");
        var npcs = answer.RootElement.GetProperty("narrative").GetProperty("npcs");
        Assert.Equal(43, Assert.Single(npcs.EnumerateArray()).GetProperty("id").GetInt32());
        Assert.False(npcs[0].TryGetProperty("portrait", out _));
        var thread = Assert.Single(answer.RootElement.GetProperty("narrative").GetProperty("plotThreads").EnumerateArray());
        Assert.Equal(891, thread.GetProperty("id").GetInt32());

        var storage = new Storage();
        storage.ApplySavedState(JsonSerializer.Serialize(state));
        storage.ApplyUpdateWorldState(JsonSerializer.Serialize(new
        {
            narrative = new { plotThreads = new[] { new { id = thread.GetProperty("id").GetInt32(), steps = "The gate is open" } } },
        }));
        Assert.Equal("The gate is open", storage.WorldState.Narrative!.PlotThreads[891].Steps!.Last());
        Assert.Empty(storage.WorldState.Narrative.PlotThreads[890].Steps!);
    }

    [Theory]
    [InlineData("key silver")]
    [InlineData("Мира ferry")]
    [InlineData("silver\tgate")]
    public void TopicSearchCombinesWordsAcrossTheSameRecord(string topic)
    {
        state.Narrative!.Npcs[44].Memory = ["The silver coin pays the boatman"];
        state.Narrative.Npcs[45].Memory = ["The iron key opens a cellar gate"];
        using var answer = Query("narrative.npcs:~" + topic);
        var npc = Assert.Single(answer.RootElement.GetProperty("narrative").GetProperty("npcs").EnumerateArray());
        Assert.Equal(43, npc.GetProperty("id").GetInt32());
        Assert.Equal("The silver key opens the ferry gate", npc.GetProperty("memory")[0].GetString());
    }

    [Fact]
    public void RepeatedFieldsUnionNameIdAndContentFilters()
    {
        using var answer = Query(" narrative.npcs:Мира ", "NARRATIVE.NPCS:#999", "narrative.npcs:~silver key");
        Assert.Equal(new[] { 43, 999 }, answer.RootElement.GetProperty("narrative").GetProperty("npcs")
            .EnumerateArray().Select(e => e.GetProperty("id").GetInt32()));
        using var secret = Query("narrative.plotThreads:~sluice");
        Assert.Equal(891, Assert.Single(secret.RootElement.GetProperty("narrative").GetProperty("plotThreads").EnumerateArray()).GetProperty("id").GetInt32());
    }

    [Theory]
    [InlineData("#not-an-id")]
    [InlineData("#1000")]
    [InlineData("~")]
    [InlineData(" ; ")]
    [InlineData("no-such-person")]
    public void InvalidOrEmptyFiltersNeverExpandToTheWholeCampaign(string filter)
    {
        using var answer = Query("narrative.npcs:" + filter);
        Assert.Empty(answer.RootElement.GetProperty("narrative").GetProperty("npcs").EnumerateArray());
    }

    [Fact]
    public void ExplicitWholeFieldIsOrderIndependentAndSearchDoesNotSilentlyTruncate()
    {
        foreach (var keys in new[] { new[] { "narrative.npcs:#43", "narrative.npcs" }, new[] { "narrative.npcs", "narrative.npcs:#43" } })
        {
            using var answer = Query(keys);
            Assert.Equal(999, answer.RootElement.GetProperty("narrative").GetProperty("npcs").GetArrayLength());
        }
        using var broad = Query("narrative.npcs:~Ordinary life");
        Assert.Equal(999, broad.RootElement.GetProperty("narrative").GetProperty("npcs").GetArrayLength());
    }

    [Fact]
    public void DeletedRecordsAndPresentationMetadataAreNotRetrieved()
    {
        using var deleted = Query("narrative.npcs:#8", "narrative.plotThreads:#4");
        Assert.Empty(deleted.RootElement.GetProperty("narrative").GetProperty("npcs").EnumerateArray());
        Assert.Empty(deleted.RootElement.GetProperty("narrative").GetProperty("plotThreads").EnumerateArray());
        using var metadata = Query("narrative.npcs:~private-portrait;~private-speaker");
        Assert.Empty(metadata.RootElement.GetProperty("narrative").GetProperty("npcs").EnumerateArray());
    }

    [Fact]
    public void EmptyDoorFilterDoesNotRetrieveEveryDoor()
    {
        using var answer = Query("map.doors: ; ");
        Assert.Empty(answer.RootElement.GetProperty("map").GetProperty("doors").EnumerateArray());
    }

    public void Dispose()
    {
        File.Delete(StatePath);
        Directory.Delete(directory);
    }
}
