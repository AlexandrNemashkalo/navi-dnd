using System.ComponentModel;
using ModelContextProtocol.Server;

namespace NaviDnD.McpServer;

[McpServerToolType]
public sealed class AssetSearchTools(McpServerConfig config)
{
    public sealed record SearchHit(string Query, string Matches);
    public sealed record SearchResults(SearchHit[] Monsters, SearchHit[] Icons);

    [McpServerTool]
    [Description("Ищет монстров и иконки одним пакетом: для независимого подбора существ и их image предпочитай один find_assets нескольким find_monster/find_icon. Передай все известные запросы сразу. Возвращает query и matches для каждого запроса; правила поиска и формат matches совпадают с find_monster/find_icon. Пустое matches означает отсутствие совпадений.")]
    public async Task<SearchResults> FindAssets(
        [Description("Английские названия монстров, например [\"skeleton\"].")] string[]? monsterQueries = null,
        [Description("Отдельный запрос для каждой иконки; варианты внутри запроса через запятую, например [\"skeleton\",\"book,scroll\",\"tablet\",\"scroll\"].")] string[]? iconQueries = null)
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
