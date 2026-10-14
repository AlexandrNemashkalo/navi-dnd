using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using NaviDnD.Helpers;

namespace NaviDnD.McpServer;

// game-icons.net icon library on disk: icons/000000/ffffff/1x1/<author>/<name>.svg - see
// NaviDnD/Helpers/SvgToBrailleConverter.cs, which resolves the SAME slug "author/name" into a file for
// rendering in braille art. Search and cache of slugs - CatalogSearch (the game also allows "icon" in the
// wizard's response).
[McpServerToolType]
public sealed class IconTools(McpServerConfig config)
{
    [McpServerTool]
    [Description("Search game-icons.net slugs for image. query is comma-separated alternatives from specific to general (longsword,sword), tried in order until a match. Make one call, never repeat it. Use 1–2 ordinary English nouns describing the item type, not a poetic name (sword, not Blade of Kings). This general icon collection is not D&D-specific and may lack exotic items. Returns comma-separated author/name slugs; select the best match. Empty result: omit image, never guess a slug.")]
    public string FindIcon([Description("Comma-separated terms, specific to general, e.g. \"longsword,sword\".")] string query)
    {
        Log($"find_icon → \"{query}\"");
        try
        {
            if (query.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length == 0)
                return Error("empty query");
            var (term, slugs) = CatalogSearch.IconsFor(query);
            if (term == null)
            {
                Log($"find_icon ← no term matched in \"{query}\"");
                return "";
            }
            string hit = string.Join(",", slugs);
            Log($"find_icon ← matchedTerm:\"{term}\" ({slugs.Count} results): {hit}");
            return hit;
        }
        catch (Exception ex)
        {
            Log($"find_icon ← (ERROR) {ex.Message}");
            return Error(ex.Message);
        }
    }

    private static string Error(string message) => $"{{\"error\":\"{message}\"}}";

    private void Log(string message)
    {
        SearchToolLog.Write(config.LogPath, message);
    }
}
