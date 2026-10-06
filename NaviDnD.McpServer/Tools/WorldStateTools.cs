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
    // Нет "combat"/"time": CombatState/GameTime целиком (все их поля без исключения) уже безусловно
    // выводятся в начале контекста каждого вызова (AiContextBuilder.MinimalState) — запрашивать их
    // через этот тул физически не может дать ничего нового.
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
    [Description("""
        Возвращает поля состояния мира.
        Запрашивай ТОЛЬКО то, чего реально не хватает в контексте выше (## Текущее состояние) — не
        то, что там уже есть (HP/AC/скорость/позиция героя, его effects целиком с description,
        экипировка, индексы имён, combat, time). keys НИКОГДА не должен быть пустым массивом —
        вызов без единого реального ключа не вернёт ничего полезного.
        Каждый ключ может включать опциональный фильтр по имени: "field:term1;term2" — вернёт
        только элементы массива, чьё имя содержит любой из терминов (без учёта регистра).
        Всегда используй фильтр, если знаешь имя — никогда не запрашивай весь массив, если нужны
        конкретные элементы.
        Ключи: hero.stats, hero.skills, hero.abilities, hero.spells, hero.inventory, hero.resources, hero.effects,
               map.cols, map.rows, map.rooms, map.doors, map.area, map.entities, map.objects,
               narrative.plotThreads, narrative.npcs, scheduledEvents.
        Примеры:
          ["hero.inventory:Дротик;Меч"] — предметы, чьё имя содержит "Дротик" или "Меч"
          ["map.entities:Орк", "hero.abilities"] — отфильтрованные entities + все abilities
          ["map.doors:0;2"] — двери с индексом (id) 0 и 2 (двери фильтруются по id, не по имени)
        """)]
    public string GetWorldState(
        [Description("Ключи полей для получения, опционально с фильтром: \"field:term1;term2\"")]
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
        storage.ApplyUpdateWorldState(json);
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
