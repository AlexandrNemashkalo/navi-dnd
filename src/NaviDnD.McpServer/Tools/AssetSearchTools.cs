using System.ComponentModel;
using ModelContextProtocol.Server;

namespace NaviDnD.McpServer;

[McpServerToolType]
public sealed class AssetSearchTools(McpServerConfig config)
{
    public sealed record SearchHit(string Query, string Matches);
    public sealed record SearchResults(SearchHit[] Monsters, SearchHit[] Icons);

    [McpServerTool]
    [Description("Search monsters and icons in one batch. For independent creature/image lookups, prefer one find_assets call over separate find_monster/find_icon calls. Submit all known queries together. Returns query and matches for each request, with the same search rules and match formats as find_monster/find_icon. Empty matches means nothing was found.")]
    public async Task<SearchResults> FindAssets(
        [Description("English monster names, e.g. [\"skeleton\"].")] string[]? monsterQueries = null,
        [Description("One query per icon; comma-separated alternatives within each query, e.g. [\"skeleton\",\"book,scroll\",\"tablet\",\"scroll\"].")] string[]? iconQueries = null)
    {
        monsterQueries ??= [];
        iconQueries ??= [];
        if (monsterQueries.Length + iconQueries.Length > 32)
            throw new ArgumentException("At most 32 searches per batch.");
        if (monsterQueries.Concat(iconQueries).Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Search queries must not be empty.");
        var monsters = new MonsterTools(config);
        var icons = new IconTools(config);
        var monsterTasks = monsterQueries.Select(query => Task.Run(() => new SearchHit(query, monsters.FindMonster(query)))).ToArray();
        var iconTasks = iconQueries.Select(query => Task.Run(() => new SearchHit(query, icons.FindIcon(query)))).ToArray();
        await Task.WhenAll(monsterTasks.Concat(iconTasks));
        return new SearchResults(monsterTasks.Select(task => task.Result).ToArray(), iconTasks.Select(task => task.Result).ToArray());
    }
}
