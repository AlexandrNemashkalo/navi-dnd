using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using NaviDnD.Helpers;

namespace NaviDnD.McpServer;

// Справочник монстров DnD5e (NaviDnD.Helpers.MonsterDatabase, GameData/DnD5e_monsters_BD.dtn) — по
// аналогии с SpellTools.FindSpell: ИИ ищет по АНГЛИЙСКОМУ названию при расстановке существ на карте
// (StartNewGame), берёт русское имя буквально как map.entities[].monsterKey. Полные статы (КД/ХП/
// атаки и т.д.) картa достаёт сама из той же базы по monsterKey — этот тул их не возвращает, только
// имя, чтобы не тащить весь statblock через ИИ туда-обратно.
[McpServerToolType]
public sealed class MonsterTools(McpServerConfig config)
{

    [McpServerTool]
    [Description("""
        Ищет монстра D&D 5e в справочнике по АНГЛИЙСКОМУ названию — НЕ выдумывай статы сам, сначала
        найди каноничного монстра здесь. query = английское название или его часть ("goblin",
        "skeleton warrior").
        Возвращает через запятую русские названия совпадений по убыванию релевантности — возьмите
        лучшее и используйте БУКВАЛЬНО как map.entities[].monsterKey (полные статы движок достанет
        сам по этому имени, отдельно указывать их не нужно). Пустая строка — монстра нет в
        справочнике: тогда придумайте существо и его статы сами, monsterKey не указывайте.
        """)]
    public string FindMonster(string query)
    {
        Log($"find_monster → \"{query}\"");
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return "";

            var normalizedQuery = NormalizeForCompare(query);
            var queryWords = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (queryWords.Length == 0)
                return "";

            var scored = new List<(MonsterInfo monster, int score)>();
            foreach (var monster in MonsterDatabase.AllMonsters)
            {
                if (string.IsNullOrEmpty(monster.NameEn)) continue;
                string normalizedName = NormalizeForCompare(monster.NameEn);
                var nameWords = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                int score = ScoreMatch(normalizedName, nameWords, queryWords, normalizedQuery);
                if (score <= 0) continue;
                scored.Add((monster, score));
            }

            var names = scored
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.monster.Name, StringComparer.Ordinal)
                .Take(15)
                .Select(x => x.monster.Name)
                .ToList();

            string result = string.Join(", ", names);
            Log($"find_monster ← {names.Count} results: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Log($"find_monster ← (ERROR) {ex.Message}");
            return "";
        }
    }

    private static string NormalizeForCompare(string s) =>
        s.ToLowerInvariant().Replace('-', ' ').Replace('\'', ' ').Replace(',', ' ');

    private static int ScoreMatch(string normalizedName, string[] nameWords, string[] queryWords, string queryNormalized)
    {
        if (normalizedName == queryNormalized) return 100;
        if (queryWords.All(nameWords.Contains)) return 80;
        if (normalizedName.StartsWith(queryNormalized, StringComparison.Ordinal)) return 60;
        if (queryWords.All(t => normalizedName.Contains(t, StringComparison.Ordinal))) return 40;
        return 0;
    }

    private void Log(string message)
    {
        SearchToolLog.Write(config.LogPath, message);
    }
}
