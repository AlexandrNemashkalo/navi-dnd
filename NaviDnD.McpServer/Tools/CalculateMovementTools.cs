using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using ModelContextProtocol.Server;
using NaviDnD;

namespace NaviDnD.McpServer;

[McpServerToolType]
public sealed class CalculateMovementTools(McpServerConfig config)
{
    private static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object _logLock = new();

    [McpServerTool]
    [Description("""
        Считает путь движения для одной сущности по приоритетному списку intent. Возвращает первый
        достижимый intent с полным путём и триггерами вдоль него.

        Схема запроса:
          entityId: int                   — индекс в map.entities
          reachFt?: int                   — досягаемость героя в футах для определения атаки по возможности (по умолчанию 5)
          fromPosition?: [col, row]       — переопределяет текущую позицию сущности (для повторного вызова после частичного пути)
          speedFt?: int                   — переопределяет SpeedMax сущности (передай remainingSpeedFt из предыдущего вызова для повторного вызова)
          skipOpportunityAttack?: bool    — true на втором вызове после того как атака по возможности уже разрешена (по умолчанию false)
          intents: [
            { intent: "approach",          target: "hero" }
            { intent: "maintain_distance", target: "hero", minFt: int, maxFt: int }
            { intent: "flee",              from: "hero" | "[col,row]" }
            { intent: "stay" }
          ]

        Всегда включай { intent: "stay" } последним элементом — он всегда достижим.

        Ответ:
          usedIntent: string           — какой intent из списка использован
          steps: [[col,row], ...]      — полный путь, включая стартовую позицию
          remainingSpeedFt: int        — остаток скорости после этого хода (передай как speedFt для повторного вызова)
          triggers: [{ atStep, type, pausePosition?, objectName?, effect? }, ...]
            types: "opportunity_attack" | "onStep" | "area_enter"
        """)]
    public string CalculateMovement(
        [Description("MovementRequest в виде JSON-строки")]
        string requestJson)
    {
        Log($"calculate_movement → {requestJson}");
        try
        {
            var req = JsonSerializer.Deserialize<MovementRequest>(requestJson, _readOptions)
                      ?? throw new ArgumentException("Invalid request JSON");

            var ws     = LoadWorldState();
            var result = MovementCalculator.Calculate(ws, req);
            string resultJson = JsonSerializer.Serialize(result, _writeOptions);

            Log($"calculate_movement ← entity:{req.EntityId} intent:{result.UsedIntent} steps:{result.Steps.Count} remaining:{result.RemainingSpeedFt}ft triggers:{result.Triggers.Count} ({resultJson.Length} chars): {resultJson}");
            return resultJson;
        }
        catch (Exception ex)
        {
            Log($"calculate_movement ← (ERROR) {ex.Message}");
            return Error(ex.Message);
        }
    }

    // ── IO helpers ──────────────────────────────────────────────────────────

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

    private static string Error(string message) => $"{{\"error\":\"{message}\"}}";

    private void Log(string message)
    {
        if (config.LogPath is null) return;
        lock (_logLock)
            File.AppendAllText(config.LogPath, $"  MCP сalculate_movement {message}\n", new UTF8Encoding(false));
    }
}
