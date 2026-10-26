using System.Text.Json;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

// Unit tests for searching for icons (find_icon) without a AI - work against the real icons/ folder on disk
// (static set of files game-icons.net, 4173 pcs., does not change during the session).
[Collection(IconPackCollection.Name)]
public class IconToolsTests
{
    private static IconTools CreateTools() => new(new McpServerConfig(WorldStatePath: "unused", LogPath: null));

    [Fact]
    public void FindIcon_ExactNameMatch_RanksFirst()
    {
        var tools = CreateTools();
        var slugs = Split(tools.FindIcon("torch"));

        Assert.Equal("delapouite/torch", slugs[0]);
    }

    [Fact]
    public void FindIcon_MultiWordQuery_RequiresAllWordsPresent()
    {
        var tools = CreateTools();
        var slugs = Split(tools.FindIcon("primitive torch"));

        Assert.Equal("delapouite/primitive-torch", slugs[0]);
    }

    [Fact]
    public void FindIcon_NoMatch_ReturnsEmptyResult()
    {
        var tools = CreateTools();
        var result = tools.FindIcon("zzznonexistenticonzzz");

        Assert.Equal("", result);
    }

    [Fact]
    public void FindIcon_FirstTermMisses_FallsBackToSecondTerm()
    {
        var tools = CreateTools();
        var slugs = Split(tools.FindIcon("zzznonexistentzzz,torch"));

        Assert.Equal("delapouite/torch", slugs[0]);
    }

    [Fact]
    public void FindIcon_FirstTermHits_DoesNotTrySecondTerm()
    {
        var tools = CreateTools();
        var slugs = Split(tools.FindIcon("torch,sword"));

        Assert.All(slugs, slug => Assert.Contains("torch", slug));
    }

    [Fact]
    public void FindIcon_MatchesAreCapped()
    {
        var tools = CreateTools();
        var slugs = Split(tools.FindIcon("sword"));

        Assert.True(slugs.Length <= 20);
    }

    [Fact]
    public void FindIcon_EmptyQuery_ReturnsError()
    {
        var tools = CreateTools();
        var result = JsonDocument.Parse(tools.FindIcon("   "));

        Assert.True(result.RootElement.TryGetProperty("error", out _));
    }

    private static string[] Split(string result) =>
        result.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
