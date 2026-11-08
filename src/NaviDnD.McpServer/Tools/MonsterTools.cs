using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using NaviDnD.Helpers;

namespace NaviDnD.McpServer;

// Directory of monsters DnD5e (NaviDnD.Helpers.MonsterDatabase, GameData/DnD5e_monsters_BD.dtn) - by analogy
// with SpellTools.FindSpell:, the AI searches for the ENGLISH name when placing creatures on the map
// (StartNewGame), takes the Russian name literally as map.entities[].monsterKey. Full stats (AC / HP / attacks,
// etc.) the map itself gets from the same database for monsterKey - this tool does not return them, only the
// name, so as not to drag the entire statblock back and forth through the AI.
[McpServerToolType]
public sealed class MonsterTools(McpServerConfig config)
{

    [McpServerTool]
    [Description("Find a D&D 5e monster by ENGLISH name or fragment (goblin, skeleton warrior). Use canonical catalog stats rather than inventing them. Returns comma-separated game-language names in relevance order. Copy the best match EXACTLY into map.entities[].monsterKey; the engine retrieves full stats, so do not duplicate them. Empty result means no catalog monster: create the creature/stats yourself and omit monsterKey.")]
    public string FindMonster(string query)
    {
        Log($"find_monster → \"{query}\"");
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return "";

            var names = CatalogSearch.Monsters(query).Select(m => m.LocalizedName).ToList();

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


    private void Log(string message)
    {
        SearchToolLog.Write(config.LogPath, message);
    }
}
