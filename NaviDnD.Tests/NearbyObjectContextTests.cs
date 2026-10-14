using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class NearbyObjectContextTests
{
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
