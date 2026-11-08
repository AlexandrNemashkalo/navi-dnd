using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using NaviDnD.Helpers;

namespace NaviDnD.McpServer;

// Directory of spells DnD5e (NaviDnD.Helpers.SpellDatabase, GameData/DnD5e_spells_BD.dtn) - before this thread,
// the AI itself came up with a Russian name and description of the spell, which is why the hero’s name
// (hero.spells[].name)) often did not coincide with the canonical Russian name in the database, and the spell
// card (MouseUiHelper.SetSelectedSpell) did not find data on the name - showed only a circle, without
// school/time/distance/text. Search - by ENGLISH name (AI usually reliably remembers it from training, unlike
// the exact Russian translation), the result is a Russian name, which should be used as is.
[McpServerToolType]
public sealed class SpellTools(McpServerConfig config)
{
    private static readonly object _logLock = new();

    [McpServerTool]
    [Description("Find a D&D 5e spell by ENGLISH name or fragment (fireball, cure wounds); retrieve the canonical spell before inventing mechanics. Optional level is a strict spell-level filter (0 = cantrip); omit if uncertain. Returns comma-separated game-language names in relevance order; copy the best EXACTLY into hero.spells[].name. Empty result means nothing matched (at that level if filtered); only then may you create a homebrew spell and description.")]
    public string FindSpell(
        string query,
        [Description("Spell level (0 = cantrip); optional strict filter.")] int? level = null)
    {
        Log($"find_spell → \"{query}\" level={level?.ToString() ?? "?"}");
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return "";

            var names = CatalogSearch.Spells(query, level).Select(s => s.Localized.Name).ToList();

            string result = string.Join(", ", names);
            Log($"find_spell ← {names.Count} results: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Log($"find_spell ← (ERROR) {ex.Message}");
            return "";
        }
    }


    private void Log(string message)
    {
        if (config.LogPath is null) return;
        lock (_logLock)
            File.AppendAllText(config.LogPath, $"  MCP {message}\n", new UTF8Encoding(false));
    }
}
