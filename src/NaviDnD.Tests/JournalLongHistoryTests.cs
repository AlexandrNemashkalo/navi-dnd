using System.Collections;
using System.Reflection;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;
using Xunit.Abstractions;

namespace NaviDnD.Tests;

public class JournalLongHistoryTests(ITestOutputHelper output)
{
    [Fact]
    public void DetailPagingReusesLayoutButResizeSelectionAndNewFactsRefreshIt()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var first = new PlotThread { Name = "First", Status = PlotThreadStatuses.Active,
            Steps = Enumerable.Repeat("A long remembered event along the old northern road.", 1000).ToList() };
        var state = new WorldState { Narrative = new() { PlotThreads = [first,
            new() { Name = "Second", Status = PlotThreadStatuses.Active, Steps = ["Other history"] }] } };
        var journal = new JournalDisplay(state, new DisplayConfig());
        void Rebuild() => typeof(JournalDisplay).GetMethod("RebuildEntries", flags)!.Invoke(journal, null);
        List<(string text, List<int> color)> Lines(int selected, int width) =>
            (List<(string text, List<int> color)>)typeof(JournalDisplay).GetMethod("BuildDetailLines", flags)!
                .Invoke(journal, [selected, width])!;
        Rebuild();
        var wide = Lines(0, 80);
        Assert.Same(wide, Lines(0, 80));
        var narrow = Lines(0, 20);
        Assert.NotSame(wide, narrow);
        Assert.True(narrow.Count > wide.Count);
        Assert.Same(narrow, Lines(0, 20));
        var other = Lines(1, 80);
        Assert.Contains(other, line => line.text.Contains("Other history"));
        Assert.DoesNotContain(other, line => line.text.Contains("remembered event"));
        first.Steps!.Add("New discovery");
        Rebuild();
        var refreshed = Lines(0, 80);
        Assert.NotSame(wide, refreshed);
        Assert.Contains(refreshed, line => line.text.Contains("New discovery"));
        Assert.Equal(1001, refreshed.Count(line => line.text.StartsWith("· ", StringComparison.Ordinal)));
        Assert.Single(Lines(-1, 80));
    }

    [Theory]
    [InlineData(JournalDisplay.Section.Quests)]
    [InlineData(JournalDisplay.Section.People)]
    public void LargeJournalOnlyFormatsTheInspectedHistoryAndKeepsAllItsFacts(JournalDisplay.Section section)
    {
        List<string> Notes() => new[] { "Oldest fact" }.Concat(Enumerable.Repeat(new string('x', 160), 998)).Append("Latest fact").ToList();
        var state = new WorldState { Narrative = new()
        {
            PlotThreads = Enumerable.Range(0,100).Select(i => new PlotThread { Name = $"Story {i}",
                Status = PlotThreadStatuses.Completed, Steps = Notes() }).ToList(),
            Npcs = Enumerable.Range(0,100).Select(i => new NpcRecord { Name = $"Person {i}", Met = true,
                Attitude = "neutral", Memory = Notes() }).ToList()
        } };
        var journal = new JournalDisplay(state, new DisplayConfig());
        var build = typeof(JournalDisplay).GetMethod("BuildEntries", BindingFlags.Instance | BindingFlags.NonPublic)!;
        build.Invoke(new JournalDisplay(new WorldState(), new DisplayConfig()), [section]);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var entries = ((IEnumerable)build.Invoke(journal, [section])!).Cast<object>().ToList();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{section}: {allocated:N0} bytes allocated to build the list.");
        Assert.Equal(100, entries.Count);
        Assert.True(allocated < 2_000_000, $"Opening the list allocated {allocated:N0} bytes for unread histories.");
        var detailsProperty = entries[0].GetType().GetProperty("Details")!;
        var selected = (List<(string text, List<int> color)>)detailsProperty.GetValue(entries[0])!;
        var facts = selected.Where(line => line.text.StartsWith("· ", StringComparison.Ordinal)).ToList();
        Assert.Equal(1000, facts.Count);
        Assert.Equal("· Latest fact", facts[0].text);
        Assert.Equal("· Oldest fact", facts[^1].text);
        Assert.Same(selected, detailsProperty.GetValue(entries[0]));
        Assert.Equal(1000, state.Narrative.PlotThreads[0].Steps!.Count);
        Assert.Equal(1000, state.Narrative.Npcs[0].Memory!.Count);
    }
}
