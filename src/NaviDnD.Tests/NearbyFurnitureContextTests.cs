using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class NearbyFurnitureContextTests
{
    [Fact]
    public void FurnitureUsesOriginalIdsAndKeepsRecentFactsWithoutUnseenOrDistantNotes()
    {
        var state = new WorldState { Hero = new() { Position = [3,3] } };
        state.Map.Furniture = [
            new() { Kind = "table", Positions = [[4,3]], Deleted = true, AiInfo = ["deleted-only"] },
            new() { Kind = "table", Name = "Repair bench", State = "searched", Positions = [[4,3],[5,3]],
                AiInfo = ["old-only", new string('x', 1000), "The tools belong to the smith.", "The drawer was searched.", "The letter was taken; the drawer is empty."] },
            new() { Kind = "table", Positions = [[8,3]], AiInfo = ["distant-only"] },
            new() { Kind = "table", Positions = [[3,4]], AiInfo = ["unseen-only"] },
            new() { Kind = "table", Positions = [[3,4],[8,3]], AiInfo = ["different-cells-only"] }];
        string before = JsonSerializer.Serialize(state);
        string context = new AiContextBuilder(state, [(3,3),(4,3),(8,3)]).MinimalState("Inspect the repair bench.");
        Assert.Contains("map.furniture:", context);
        Assert.Contains("\"id\":1", context);
        Assert.Contains("\"state\":\"searched\"", context);
        Assert.Contains("The letter was taken; the drawer is empty.", context);
        Assert.Contains("\"aiInfoOmitted\":1", context);
        Assert.Contains("truncated; retrieve full record", context);
        Assert.Contains("get_world_state map.furniture:#ID", context);
        foreach (string absent in new[] { "old-only", "deleted-only", "distant-only", "unseen-only", "different-cells-only" })
            Assert.DoesNotContain(absent, context);
        Assert.Equal(before, JsonSerializer.Serialize(state));
        string full = new AiContextBuilder(state).Serialize("map.furniture");
        Assert.Contains("old-only", full);
        Assert.Contains(new string('x', 1000), full);
    }
}
