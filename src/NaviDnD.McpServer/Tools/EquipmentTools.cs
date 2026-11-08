using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using NaviDnD.Helpers;

namespace NaviDnD.McpServer;

// Equipment directory DnD5e (NaviDnD.Helpers.EquipmentDatabase, GameData/DnD5e_equipment_BD.dtn +
// DnD5e_magic_equipment_BD.dtn) - similar to SpellTools.FindSpell:, the AI searches by ENGLISH name when
// generating inventory/loot, so as not to confuse similar items (for example Dart 1d4 vs Javelin 1d6 - both
// “throwing”, easy to confuse from memory). Unlike spells, there is no separate reference card in the game -
// the AI writes hero.inventory[].description itself once, so the tool returns not only the canonical Russian
// name, but also the mechanics themselves (damage/AC/properties/weight/price), translation into a short Russian
// string description - to the AI, as usual.
[McpServerToolType]
public sealed class EquipmentTools(McpServerConfig config)
{
    private static readonly object _logLock = new();

    [McpServerTool]
    [Description("Find ordinary or magical D&D 5e equipment by ENGLISH name. For a catalog lookup, use the exact item rather than inventing similar mechanics from memory. query is an English name or fragment (javelin, flame tongue). Returns one match per line in relevance order: game-language name (copy exactly into inventory[].name) and mechanics (damage/type, properties, AC, weight, price; magic items also rarity, attunement, effect). Summarize those mechanics in a short description in the game language. Empty result means no catalog entry; create the item yourself without repeating the search.")]
    public string FindItem(string query)
    {
        Log($"find_item → \"{query}\"");
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return "";

            var lines = CatalogSearch.Items(query).Select(Summarize).ToList();

            string result = string.Join("\n", lines);
            Log($"find_item ← {lines.Count} results");
            return result;
        }
        catch (Exception ex)
        {
            Log($"find_item ← (ERROR) {ex.Message}");
            return "";
        }
    }

    private static string Summarize(EquipmentInfo i)
    {
        var parts = new List<string>();
        if (i.DamageVal.Length > 0) parts.Add($"{i.DamageVal} {i.DamageType}".Trim());
        if (i.Props.Count > 0) parts.Add(string.Join(", ", i.Props));
        if (i.Ac.Length > 0) parts.Add($"AC {i.Ac}");
        if (i.Weight.Length > 0) parts.Add($"weight {i.Weight}");
        if (i.Cost.Length > 0) parts.Add($"price {i.Cost}");
        if (i.Rarity.Length > 0) parts.Add($"rarity: {i.Rarity}");
        if (i.Category == "magical")
            parts.Add(i.Attunement.Length > 0 ? $"attunement: {i.Attunement}" : "attunement: not required");
        if (i.Text.Length > 0) parts.Add(i.Text);

        string type = i.TypeAdditions.Length > 0 ? $"{i.Type} {i.TypeAdditions}" : i.Type;
        return $"{i.LocalizedName}{(L.WorldIsEnglish ? "" : $" ({i.NameEn})")} [{i.Category}, {type}]: {string.Join("; ", parts)}";
    }


    private void Log(string message)
    {
        if (config.LogPath is null) return;
        lock (_logLock)
            File.AppendAllText(config.LogPath, $"  MCP {message}\n", new UTF8Encoding(false));
    }
}
