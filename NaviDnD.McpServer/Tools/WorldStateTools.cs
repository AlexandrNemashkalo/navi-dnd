using System.ComponentModel;
using System.Diagnostics;
using System.Text;
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
    [Description("Retrieve world-state fields ONLY when missing from ## Current state. Do not repeat requests for supplied HP/AC/speed/position, complete hero effects, equipment, name indexes, combat, or time. keys must never be empty.\nEach key may filter by actual state names: \"field:term1;term2\" returns array elements whose names contain any term, case-insensitive. Always filter when the required names are known; never retrieve an entire array for specific entries. Use the exact localized names from the index, not their English translations.\nKeys: hero.stats, hero.skills, hero.abilities, hero.spells, hero.inventory, hero.resources, hero.effects, map.cols, map.rows, map.rooms, map.doors, map.area, map.entities, map.objects, narrative.plotThreads, narrative.npcs, scheduledEvents.\nExamples: [\"hero.inventory:Javelin;Sword\"] for an English inventory; [\"map.entities:Orc\",\"hero.abilities\"] for matching creatures and all abilities; [\"map.doors:0;2\"] for door IDs 0 and 2 (doors filter by ID, not name).")]
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
        string result = new AiContextBuilder(ws).Serialize(requested, filters.Count > 0 ? filters : null);
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

        foreach (var key in keys)
        {
            int colon = key.IndexOf(':');
            if (colon > 0)
            {
                string fieldKey = key[..colon];
                string[] terms = key[(colon + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                cleanKeys.Add(fieldKey);
                if (terms.Length > 0)
                    filters[fieldKey] = terms;
            }
            else
            {
                cleanKeys.Add(key);
            }
        }

        return (cleanKeys, filters);
    }

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
