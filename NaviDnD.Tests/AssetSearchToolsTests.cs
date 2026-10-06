using NaviDnD.McpServer;

namespace NaviDnD.Tests;

public class AssetSearchToolsTests
{
    [Fact]
    public async Task BatchMatchesIndividualSearchesAndPreservesOrder()
    {
        string log = Path.Combine(Path.GetTempPath(), "navidnd-search-" + Guid.NewGuid() + ".log");
        try
        {
            var config = new McpServerConfig("unused", log);
            string[] queries = ["skeleton", "book,scroll", "tablet", "scroll"];
            var batch = await new AssetSearchTools(config).FindAssets(["skeleton"], queries);
            Assert.Equal(new MonsterTools(config).FindMonster("skeleton"), batch.Monsters.Single().Matches);
            Assert.NotEmpty(batch.Monsters.Single().Matches);
            Assert.Equal(queries, batch.Icons.Select(hit => hit.Query));
            var icons = new IconTools(config);
            foreach (var hit in batch.Icons)
            {
                Assert.NotEmpty(hit.Matches);
                Assert.Equal(icons.FindIcon(hit.Query), hit.Matches);
            }
            Assert.DoesNotContain("ERROR", File.ReadAllText(log));
        }
        finally { if (File.Exists(log)) File.Delete(log); }
    }

    [Fact]
    public async Task MissingMatchesRemainAssociatedWithTheirQueries()
    {
        var tools = new AssetSearchTools(new McpServerConfig("unused", null));
        var result = await tools.FindAssets(["zzznonexistentmonsterzzz"], ["zzznonexistenticonzzz", "torch"]);
        Assert.Empty(result.Monsters.Single().Matches);
        Assert.Empty(result.Icons[0].Matches);
        Assert.NotEmpty(result.Icons[1].Matches);
        Assert.Empty((await tools.FindAssets()).Icons);
        await Assert.ThrowsAsync<ArgumentException>(() => tools.FindAssets(iconQueries: [" "]));
        await Assert.ThrowsAsync<ArgumentException>(() => tools.FindAssets(iconQueries: Enumerable.Repeat("torch", 33).ToArray()));
    }
}
