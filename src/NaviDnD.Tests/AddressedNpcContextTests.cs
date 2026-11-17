using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class AddressedNpcContextTests
{
    [Theory]
    [InlineData("Al", "I walk around the village.", false)]
    [InlineData("Al", "Alina, where is the mill?", false)]
    [InlineData("Al", "Al, where is the mill?", true)]
    [InlineData("Лев", "Осматриваю левую стену.", false)]
    [InlineData("Лев", "Лев, расскажи про мост.", true)]
    [InlineData("Mara Venn", "I ask MARA VENN about the invoice.", true)]
    public void ShortNamesMustBeMentionedAsNames(string name, string action, bool addressed)
    {
        var ws = new WorldState { Hero = new() { Symbol = "HRO", Position = [3,3] } };
        ws.Map.Entities = [new() { Name = name, Position = [4,3], AiInfo = ["addressed-only-fact"] }];
        string context = new AiContextBuilder(ws, [(3,3),(4,3)]).MinimalState(action);
        Assert.Equal(addressed, context.Contains("addressed-only-fact", StringComparison.Ordinal));
    }

    [Fact]
    public void AddressedVisibleNpcRetainsOriginalFactsAndIndexWithoutUnrelatedSecrets()
    {
        var ws = new WorldState { Hero = new() { Symbol = "HRO", Position = [3,3] } };
        ws.Map.Entities = [
            new() { Name = "Oren", Position = [4,3], Deleted = true, AiInfo = ["deleted-secret"] },
            new() { Name = "Oren", Position = [4,3], AiInfo = ["Deliver the invoice to the NEXT village."] },
            new() { Name = "Mara", Position = [3,4], AiInfo = ["unaddressed-secret"] },
            new() { Name = "Oren", Position = [5,3], Hidden = true, AiInfo = ["hidden-secret"] },
            new() { Name = "Oren", Position = [3,5], AiInfo = ["unseen-secret"] }];
        string before = System.Text.Json.JsonSerializer.Serialize(ws);
        string context = new AiContextBuilder(ws, [(3,3),(4,3),(3,4),(5,3)]).MinimalState("Oren, where should I deliver it?");
        Assert.Contains("NEXT village", context);
        Assert.Contains("\"id\":1", context);
        Assert.Contains("get_world_state map.entities:#ID", context);
        Assert.DoesNotContain("deleted-secret", context);
        Assert.DoesNotContain("unaddressed-secret", context);
        Assert.DoesNotContain("hidden-secret", context);
        Assert.DoesNotContain("unseen-secret", context);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(ws));
    }
}
