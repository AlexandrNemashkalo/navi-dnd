using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class NearbyObjectContextTests
{
    [Fact]
    public void LongObjectHistoryKeepsRecentConsequencesAndOffersFullRecall()
    {
        var ws = new WorldState { Hero = new() { Symbol = "HRO", Position = [3,3] } };
        var notes = Enumerable.Range(0, 40).Select(i => $"Old account {i}: " + new string('x', 1000)).ToList();
        notes.Add("The bridge was repaired; the communal timber has been consumed.");
        ws.Map.Objects = [new() { Name = "Bridge", Symbol = "BRG", Position = [4,3], AiInfo = notes }];
        string before = System.Text.Json.JsonSerializer.Serialize(ws);
        string context = new AiContextBuilder(ws, [(3,3), (4,3)]).MinimalState("Inspect the bridge.");
        Assert.Contains(notes[^1], context);
        Assert.DoesNotContain("Old account 0:", context);
        Assert.Contains("\"aiInfoOmitted\":37", context);
        Assert.Contains("truncated; retrieve full record", context);
        Assert.Contains("get_world_state map.objects:#ID", context);
        Assert.True(context.IndexOf(notes[^1], StringComparison.Ordinal) < context.IndexOf("Old account 39:", StringComparison.Ordinal));
        Assert.True(context.Length < 5000);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(ws));
        string full = new AiContextBuilder(ws).Serialize(["map.objects"]);
        Assert.Contains("Old account 0:", full);
        Assert.Contains(notes[^1], full);
    }

    [Fact]
    public void NearbyObjectIncludesNotesAndOriginalIndexWithoutDistantOrUnseenNotes()
    {
        var ws = new WorldState { Hero = new() { Symbol = "HRO", Name = "Герой", Position = [3,3], Hp = "10/10" } };
        ws.Map.Objects = [new() { Symbol = "(D)", Name = "Удалён", Position = [3,3], Deleted = true, AiInfo = ["deleted-only"] },
            new() { Symbol = "(C)", Name = "Чаша", Position = [4,3], AiInfo = ["Каменная чаша", "near-only"] },
            new() { Symbol = "(F)", Name = "Алтарь", Position = [8,3], AiInfo = ["far-only"] },
            new() { Symbol = "(U)", Name = "Сундук", Position = [3,4], AiInfo = ["unseen-only"] }];
        string context = new AiContextBuilder(ws, [(3,3), (4,3), (8,3)]).MinimalState("наполняю чашу водой");
        Assert.Contains("near-only", context);
        Assert.Contains("Каменная чаша", context);
        Assert.Contains("\"id\":1", context);
        Assert.DoesNotContain("deleted-only", context);
        Assert.DoesNotContain("far-only", context);
        Assert.DoesNotContain("unseen-only", context);
    }
}
