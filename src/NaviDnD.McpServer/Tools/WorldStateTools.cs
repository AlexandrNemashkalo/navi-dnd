using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NaviDnD;          // Storage, WorldState
using NaviDnD.Clients;  // AiContextBuilder

namespace NaviDnD.McpServer;

/// <summary>
/// Single MCP tool that serves requested WorldState fields to Claude CLI.
/// Reads worldState.json once per call and serializes only requested keys.
/// </summary>
[McpServerToolType]
public sealed class WorldStateTools(McpServerConfig config)
{
    // No "combat"/"time": CombatState/GameTime in its entirety (all their fields without exception) are already
    // unconditionally displayed at the beginning of the context of each call (AiContextBuilder.MinimalState) -
    // requesting them through this tool physically cannot give anything new.
    private static readonly HashSet<string> _validKeys =
    [
        "hero.stats", "hero.skills", "hero.abilities", "hero.spells",
        "hero.inventory", "hero.resources", "hero.effects",
        "map.cols", "map.rows", "map.rooms", "map.doors", "map.area",
        "map.entities", "map.objects", "map.furniture",
        "narrative.plotThreads", "narrative.npcs",
        "scheduledEvents",
    ];

    private static readonly object _logLock = new();

    [McpServerTool]
    [Description("Retrieve world-state fields ONLY when missing from supplied context. Do not repeat supplied HP/AC/speed/position, effects, equipment, combat or time. keys must never be empty.\nFilter actual localized names with \"field:term1;term2\" (case-insensitive OR substrings). Repeated fields combine their filters; a bare field explicitly requests the whole array. Empty filters return no entries.\nFor named arrays, #N selects the original record ID. For narrative.npcs and narrative.plotThreads, ~text requires all whitespace-separated search terms within the same record, in any order across name, memories, steps and GM notes; combine these with name terms using semicolons. Search old facts with specific terms instead of retrieving the entire campaign. All matches retain original IDs and are returned without truncation; refine broad searches.\nKeys: hero.stats, hero.skills, hero.abilities, hero.spells, hero.inventory, hero.resources, hero.effects, map.cols, map.rows, map.rooms, map.doors, map.area, map.entities, map.objects, map.furniture, narrative.plotThreads, narrative.npcs, scheduledEvents.\nExamples: [\"hero.inventory:Javelin;Sword\"], [\"narrative.npcs:#12;~silver key\"], [\"map.doors:0;2\"] (doors filter by ID, not name).")]
    public string GetWorldState(
        [Description("State keys to retrieve, optionally filtered: \"field:term1;term2\".")]
        string[] keys)
    {
        Log($"get_world_state → [{string.Join(", ", keys)}]");

        var (cleanKeys, filters) = ParseKeys(keys);

        var requested = cleanKeys
            .Where(k => _validKeys.Contains(k))
            .ToArray();

        if (requested.Length == 0)
        {
            string err = $"{{\"error\": \"No valid keys provided. Valid keys: {string.Join(", ", _validKeys)}\"}}";
            Log($"get_world_state ← (error) {err}");
            return err;
        }

        var ws = LoadWorldState();
        // Narrative search is applied after WithIds serialization: slicing the source lists would change patch IDs.
        var directFilters = filters.Where(pair => !IsNarrative(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (directFilters.TryGetValue("map.doors", out var doorTerms) && !doorTerms.Any(term => int.TryParse(term, out _)))
            directFilters["map.doors"] = ["-1"]; // WithIdsByIndex otherwise treats an invalid/empty filter as unfiltered.
        string result = new AiContextBuilder(ws).Serialize(requested, directFilters.Count > 0 ? directFilters : null);
        if (requested.Any(IsNarrative))
        {
            var root = JsonNode.Parse(result)!.AsObject();
            foreach (var field in requested.Where(IsNarrative))
            {
                filters.TryGetValue(field, out var terms);
                string name = field[(field.IndexOf('.') + 1)..];
                if (root["narrative"]?[name] is not JsonArray entries) continue;
                for (int i = entries.Count - 1; i >= 0; i--)
                    if (entries[i] is not JsonObject entry || entry["deleted"]?.GetValue<bool>() == true
                        || (terms is not null && !terms.Any(term => MatchesNarrative(entry, term)))) entries.RemoveAt(i);
            }
            result = root.ToJsonString();
        }
        Log($"get_world_state ← ({result.Length} chars): {result}");
        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses "field:term1;term2" keys into (cleanKey, filters) pairs.
    /// </summary>
    private static (List<string> cleanKeys, Dictionary<string, string[]> filters) ParseKeys(string[] keys)
    {
        var cleanKeys = new List<string>(keys.Length);
        var filters = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var wholeFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var supplied in keys)
        {
            if (string.IsNullOrWhiteSpace(supplied)) continue;
            string key = supplied.Trim();
            int colon = key.IndexOf(':');
            string rawField = (colon > 0 ? key[..colon] : key).Trim();
            string fieldKey = _validKeys.FirstOrDefault(valid => valid.Equals(rawField, StringComparison.OrdinalIgnoreCase)) ?? rawField;
            if (!cleanKeys.Contains(fieldKey)) cleanKeys.Add(fieldKey);
            if (colon <= 0)
            {
                wholeFields.Add(fieldKey);
                filters.Remove(fieldKey);
                continue;
            }
            if (wholeFields.Contains(fieldKey)) continue;
            string[] terms = key[(colon + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            filters[fieldKey] = filters.GetValueOrDefault(fieldKey, []).Concat(terms).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        return (cleanKeys, filters);
    }

    private static bool IsNarrative(string field) => field is "narrative.npcs" or "narrative.plotThreads";

    private static bool MatchesNarrative(JsonObject entry, string term)
    {
        if (term.StartsWith('#'))
            return int.TryParse(term.AsSpan(1), out int id) && entry["id"]?.GetValue<int>() == id;
        if (term.StartsWith('~'))
        {
            var words = term[1..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 0 && words.All(word => ContainsText(entry, word));
        }
        return entry["name"]?.GetValue<string>()?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool ContainsText(JsonNode? node, string term) => node switch
    {
        JsonValue value => value.TryGetValue<string>(out var text) && text.Contains(term, StringComparison.OrdinalIgnoreCase),
        JsonArray array => array.Any(child => ContainsText(child, term)),
        JsonObject obj => obj.Any(property => ContainsText(property.Value, term)),
        _ => false,
    };

    private WorldState LoadWorldState()
    {
        string json = RetryOnIo(() =>
        {
            using var stream = new FileStream(config.WorldStatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        });
        var storage = new Storage();
        storage.ApplySavedState(json);
        return storage.WorldState;
    }

    private static T RetryOnIo<T>(Func<T> action, int attempts = 5, int delayMs = 30)
    {
        for (int i = 0; i < attempts; i++)
        {
            try { return action(); }
            catch (IOException) when (i < attempts - 1) { Thread.Sleep(delayMs); }
        }
        throw new UnreachableException();
    }

    private void Log(string message)
    {
        if (config.LogPath is null) return;
        lock (_logLock)
            File.AppendAllText(config.LogPath, $"  MCP {message}\n", new UTF8Encoding(false));
    }
}
