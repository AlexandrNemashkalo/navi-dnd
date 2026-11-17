using System.Collections;
using System.Reflection;
using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

public class JournalSelectionIdentityTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(JournalDisplay).GetField(name, Private)!;
    private static Dictionary<JournalDisplay.Section, int> Indices(JournalDisplay journal, string name) =>
        (Dictionary<JournalDisplay.Section, int>)Field(name).GetValue(journal)!;
    private static List<Guid?> Ids(JournalDisplay journal) => ((IEnumerable)Field("_entries").GetValue(journal)!)
        .Cast<object>().Select(e => (Guid?)e.GetType().GetProperty("StoryId")!.GetValue(e)).ToList();
    private static void Rebuild(JournalDisplay journal) => typeof(JournalDisplay).GetMethod("RebuildEntries", Private)!.Invoke(journal, null);
    private static void Select(JournalDisplay journal, Guid selected, Guid pinned)
    {
        Indices(journal, "_selected")[JournalDisplay.Section.Quests] = Ids(journal).IndexOf(selected);
        Indices(journal, "_pinned")[JournalDisplay.Section.Quests] = Ids(journal).IndexOf(pinned);
    }

    [Fact]
    public void DuplicateNamesKeepSelectedAndPinnedStoriesAfterStatusPatchesAndListCompaction()
    {
        var storage = new Storage();
        storage.WorldState.Narrative = new() { PlotThreads = Enumerable.Range(0, 6).Select(i => new PlotThread
            { Name = "Repeated title", Status = PlotThreadStatuses.Active, Description = $"Story {i}" }).ToList() };
        var journal = new JournalDisplay(storage.WorldState, new DisplayConfig());
        Field("_rows").SetValue(journal, 2);
        Rebuild(journal);
        Guid selected = storage.WorldState.Narrative.PlotThreads[0].RuntimeId;
        Guid pinned = storage.WorldState.Narrative.PlotThreads[1].RuntimeId;
        Select(journal, selected, pinned);
        Field("_detailPage").SetValue(journal, 3);
        storage.ApplyUpdateWorldState("""
            {"narrative":{"plotThreads":[{"id":0,"status":"completed"},{"id":1,"status":"offered"}]}}
            """);
        Rebuild(journal);
        Assert.Equal(selected, Ids(journal)[Indices(journal, "_selected")[JournalDisplay.Section.Quests]]);
        Assert.Equal(pinned, Ids(journal)[Indices(journal, "_pinned")[JournalDisplay.Section.Quests]]);
        Assert.Equal(2, Indices(journal, "_page")[JournalDisplay.Section.Quests]);
        Assert.Equal(3, Field("_detailPage").GetValue(journal));

        // Delete another original record, physically compacting PlotThreads; selection is not an array index.
        storage.ApplyUpdateWorldState("""{"narrative":{"plotThreads":[{"id":2,"deleted":true}]}}""");
        Rebuild(journal);
        Assert.Equal(selected, Ids(journal)[Indices(journal, "_selected")[JournalDisplay.Section.Quests]]);
        Assert.Equal(pinned, Ids(journal)[Indices(journal, "_pinned")[JournalDisplay.Section.Quests]]);

        // Deleting the selected story returns to the surviving pinned story and resets its detail page.
        storage.ApplyUpdateWorldState("""{"narrative":{"plotThreads":[{"id":0,"deleted":true}]}}""");
        Rebuild(journal);
        Assert.Equal(pinned, Ids(journal)[Indices(journal, "_selected")[JournalDisplay.Section.Quests]]);
        Assert.Equal(0, Field("_detailPage").GetValue(journal));
    }

    [Fact]
    public void TabSwitchAndCurrentGoalRemovalDoNotBindSelectionToAnotherSection()
    {
        var storage = new Storage();
        storage.WorldState.Narrative = new() { CurrentArc = "First goal", PlotThreads =
            [new() { Name = "Story", Status = PlotThreadStatuses.Active }], Npcs = [new() { Name = "Mira", Met = true }] };
        var journal = new JournalDisplay(storage.WorldState, new DisplayConfig());
        Field("_rows").SetValue(journal, 3);
        Rebuild(journal);
        Guid story = storage.WorldState.Narrative.PlotThreads[0].RuntimeId;
        Select(journal, story, story);
        journal.SetSectionSilently(JournalDisplay.Section.People);
        Rebuild(journal);
        storage.ApplyUpdateWorldState("""{"narrative":{"currentArc":"","plotThreads":[{"id":0,"status":"completed"}]}}""");
        journal.SetSectionSilently(JournalDisplay.Section.Quests);
        Rebuild(journal);
        Assert.Equal(story, Ids(journal)[Indices(journal, "_selected")[JournalDisplay.Section.Quests]]);
        Assert.Equal(0, Indices(journal, "_selected")[JournalDisplay.Section.Quests]);
        storage.ApplyUpdateWorldState("""{"narrative":{"plotThreads":[{"id":0,"hidden":true}]}}""");
        Field("_detailPage").SetValue(journal, 2);
        Rebuild(journal);
        Assert.Empty(Ids(journal));
        Assert.Equal(-1, Indices(journal, "_selected")[JournalDisplay.Section.Quests]);
        Assert.Equal(-1, Indices(journal, "_pinned")[JournalDisplay.Section.Quests]);
        Assert.Equal(0, Field("_detailPage").GetValue(journal));
    }

    [Fact]
    public void RebuildingAnUnselectedStoryListChoosesAValidFirstEntry()
    {
        var storage = new Storage();
        storage.WorldState.Narrative = new() { PlotThreads = [new() { Name = "First", Status = PlotThreadStatuses.Active }] };
        var journal = new JournalDisplay(storage.WorldState, new DisplayConfig());
        Field("_rows").SetValue(journal, 3);
        Rebuild(journal);
        Rebuild(journal);
        Assert.Equal(0, Indices(journal, "_selected")[JournalDisplay.Section.Quests]);
        Assert.Equal(0, Indices(journal, "_pinned")[JournalDisplay.Section.Quests]);
        Assert.Equal(0, Indices(journal, "_page")[JournalDisplay.Section.Quests]);
    }

    [Fact]
    public void CurrentGoalHasSeparateIdentityAndNotesKeepTheirPositionalSelection()
    {
        var storage = new Storage();
        storage.WorldState.Narrative = new() { CurrentArc = "First goal", PlotThreads =
            [new() { Name = "First goal", Status = PlotThreadStatuses.Active }] };
        var journal = new JournalDisplay(storage.WorldState, new DisplayConfig());
        Field("_rows").SetValue(journal, 3);
        Rebuild(journal);
        Select(journal, Guid.Empty, Guid.Empty);
        storage.WorldState.Narrative.CurrentArc = "A revised goal";
        Rebuild(journal);
        Assert.Equal(Guid.Empty, Ids(journal)[Indices(journal, "_selected")[JournalDisplay.Section.Quests]]);

        // The notes handlers intentionally set row zero after insertion; story remapping must not override it.
        journal.SetSectionSilently(JournalDisplay.Section.Notes);
        Indices(journal, "_selected")[JournalDisplay.Section.Notes] = 0;
        Indices(journal, "_pinned")[JournalDisplay.Section.Notes] = 0;
        Indices(journal, "_page")[JournalDisplay.Section.Notes] = 0;
        Rebuild(journal);
        Assert.Equal(0, Indices(journal, "_selected")[JournalDisplay.Section.Notes]);
        Assert.Equal(0, Indices(journal, "_pinned")[JournalDisplay.Section.Notes]);
        Assert.Equal(0, Indices(journal, "_page")[JournalDisplay.Section.Notes]);
        journal.SetSectionSilently(JournalDisplay.Section.Quests);
        storage.WorldState.Narrative.CurrentArc = "";
        Field("_detailPage").SetValue(journal, 2);
        Rebuild(journal);
        Assert.Equal(storage.WorldState.Narrative.PlotThreads[0].RuntimeId,
            Ids(journal)[Indices(journal, "_selected")[JournalDisplay.Section.Quests]]);
        Assert.Equal(0, Field("_detailPage").GetValue(journal));
    }

    [Fact]
    public void RuntimeIdentityCannotBePatchedAndNeverLeavesTheProcess()
    {
        var storage = new Storage();
        storage.ApplyUpdateWorldState("""{"narrative":{"plotThreads":[{"name":"Original","status":"active"}]}}""");
        var original = storage.WorldState.Narrative!.PlotThreads[0];
        Guid identity = original.RuntimeId;
        storage.ApplyUpdateWorldState(JsonSerializer.Serialize(new { narrative = new { plotThreads = new[]
            { new { id = 0, name = "Renamed", runtimeId = Guid.Empty } } } }));
        Assert.NotSame(original, storage.WorldState.Narrative.PlotThreads[0]);
        Assert.Equal(identity, storage.WorldState.Narrative.PlotThreads[0].RuntimeId);
        storage.ApplyUpdateWorldState("""{"narrative":{"plotThreads":[{"name":"New","runtimeId":"00000000-0000-0000-0000-000000000000"}]}}""");
        Assert.NotEqual(identity, storage.WorldState.Narrative.PlotThreads[1].RuntimeId);
        Assert.NotEqual(Guid.Empty, storage.WorldState.Narrative.PlotThreads[1].RuntimeId);
        string save = JsonSerializer.Serialize(storage.WorldState);
        Assert.DoesNotContain("RuntimeId", save);
        Assert.DoesNotContain(identity.ToString(), save);
        string context = new AiContextBuilder(storage.WorldState).Serialize("narrative.plotThreads");
        Assert.DoesNotContain("runtimeId", context, StringComparison.OrdinalIgnoreCase);
        var loaded = new Storage();
        loaded.ApplySavedState(save);
        Assert.NotEqual(identity, loaded.WorldState.Narrative!.PlotThreads[0].RuntimeId);
        string path = Path.Combine(Path.GetTempPath(), $"journal-identity-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, save);
            string mcp = new WorldStateTools(new McpServerConfig(path, LogPath: null)).GetWorldState(["narrative.plotThreads"]);
            Assert.DoesNotContain("runtimeId", mcp, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(identity.ToString(), mcp);
        }
        finally { File.Delete(path); }
    }
}
