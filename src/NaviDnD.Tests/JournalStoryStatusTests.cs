using System.Collections;
using System.Reflection;
using NaviDnD.Data;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class JournalStoryStatusTests
{
    [Fact]
    public void JournalDistinguishesCommitmentsFromRumorsAndPreservesCustomStatus()
    {
        var storage = new Storage();
        storage.ApplyUpdateWorldState("""
            {"narrative":{"plotThreads":[
              {"name":"Finished","status":"завершена"},
              {"name":"Rumor","status":"rumor"},
              {"name":"Discovery","status":"revealed"},
              {"name":"Secret","status":"active","hidden":true},
              {"name":"Offer","status":"offered"},
              {"name":"Custom","status":"Выполнить позже"},
              {"name":"Goal","status":"active"},
              {"name":"Missing"},
              {"name":"Deleted","status":"active","deleted":true},
              {"name":"Failed","status":"failed"},
              {"name":"Refused","status":"declined"}]}}
            """);
        var display = new JournalDisplay(storage.WorldState, new DisplayConfig());
        var entries = ((IEnumerable)typeof(JournalDisplay)
            .GetMethod("BuildEntries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(display, [JournalDisplay.Section.Quests])!).Cast<object>().ToList();
        string Read(object entry, string property) => (string)entry.GetType().GetProperty(property)!.GetValue(entry)!;
        Assert.Equal(new[] { "Goal", "Offer", "Rumor", "Discovery", "Custom", "Missing", "Finished", "Failed", "Refused" },
            entries.Select(e => Read(e, "Title")));
        Assert.Equal(new[] { "▸", "◇", "?", "◆", "·", "·", "✓", "✗", "-" }, entries.Select(e => Read(e, "Marker")));
        var refused = entries.Single(e => Read(e, "Title") == "Refused");
        var refusalDetails = (List<(string text, List<int> color)>)refused.GetType().GetProperty("Details")!.GetValue(refused)!;
        Assert.Contains(refusalDetails, line => line.text.EndsWith(L.T("Declined"), StringComparison.Ordinal));
        var custom = entries.Single(e => Read(e, "Title") == "Custom");
        var details = (List<(string text, List<int> color)>)custom.GetType().GetProperty("Details")!.GetValue(custom)!;
        Assert.Contains(details, line => line.text.EndsWith("Выполнить позже", StringComparison.Ordinal));
        Assert.True(string.IsNullOrEmpty(storage.WorldState.Narrative.CurrentArc));
    }
}
