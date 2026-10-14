using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using NaviDnD;

namespace NaviDnD.McpServer;

[McpServerToolType]
public sealed class SelectTargetTools(McpServerConfig config)
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly object _logLock = new();

    private string RequestPath =>
        Path.Combine(Path.GetDirectoryName(config.WorldStatePath)!, "select_target_request.json");

    [McpServerTool]
    [Description("Let the player select targets/locations with the mouse on the map. ALWAYS use for area effects (sphere/cube/cone/line) with shape and sizeFt, even if the player named a location; they must see and select the area. For a single target, call only when the intended creature is ambiguous. The UI highlights legal cells and previews areas; waits up to 50 seconds.\nhistory[]: entries before selection, like use_reaction; the last entry asks what to select in the game language. mode:\"creature\" selects a hero-visible creature (including the hero); \"point\" selects an explored cell in line of sight. rangeFt: maximum distance from hero, omitted means unlimited. count: number of targets, default 1. allowRepeat: whether a target can repeat.\nshape: sphere (radius sizeFt) or cube (side sizeFt) around the selected point; cone (length sizeFt) or line (length sizeFt, width 5 ft) from hero toward the selected cell. Omit shape without an area; sizeFt is in feet.\nReturns targets:[{id,symbol,name,position,distanceFt}]; areas also return areaCells (all [col,row]), inArea (creatures including hero), and objectsInArea. id is the patch index in map.entities/map.objects, absent for hero.\nCancellation returns cancelled:true; no answer in 50 seconds returns timedOut:true. Do not execute the action in either case; briefly explain.")]
    public string SelectTarget(
        [Description("History shown before selection: text (optionally SSML <speak>), optional author and patch; use the game language.")] RequestHistoryEntry[] history,
        [Description("\"creature\" or \"point\".")] string mode,
        [Description("Maximum distance from the hero, in feet.")] int? rangeFt = null,
        [Description("Number of targets to select.")] int count = 1,
        [Description("Whether a target may be selected more than once.")] bool allowRepeat = false,
        [Description("\"sphere\", \"cube\", \"cone\", or \"line\".")] string? shape = null,
        [Description("Area size in feet.")] int? sizeFt = null)
    {
        var request = new TargetSelectionRequest
        {
            History = history?.ToList() ?? [],
            Mode = mode == "point" ? "point" : "creature",
            RangeFt = rangeFt,
            Count = Math.Max(1, count),
            AllowRepeat = allowRepeat,
            Shape = string.IsNullOrWhiteSpace(shape) ? null : shape.Trim().ToLowerInvariant(),
            SizeFt = sizeFt,
        };

        Log($"select_target → mode={request.Mode} range={rangeFt} count={request.Count} shape={request.Shape} size={sizeFt}");
        WriteRequest(request);

        var deadline = DateTime.UtcNow.AddSeconds(50);
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(200);
            var current = TryReadRequest();
            if (current is { Answered: true, Result: not null })
            {
                TryDelete();
                string json = JsonSerializer.Serialize(current.Result, _json);
                Log($"select_target ← {json}");
                return json;
            }
        }

        TryDelete();
        Log("select_target ← timedOut");
        return JsonSerializer.Serialize(new { timedOut = true }, _json);
    }

    private void WriteRequest(TargetSelectionRequest request)
    {
        string json = JsonSerializer.Serialize(request, _json);
        RetryOnIo(() => File.WriteAllText(RequestPath, json, new UTF8Encoding(false)));
    }

    private TargetSelectionRequest? TryReadRequest()
    {
        try
        {
            if (!File.Exists(RequestPath)) return null;
            string json = RetryOnIo(() =>
            {
                using var stream = new FileStream(RequestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            });
            return JsonSerializer.Deserialize<TargetSelectionRequest>(json, _json);
        }
        catch { return null; }
    }

    private void TryDelete()
    {
        try { if (File.Exists(RequestPath)) File.Delete(RequestPath); }
        catch { }
    }

    private static T RetryOnIo<T>(Func<T> action, int attempts = 5, int delayMs = 30)
    {
        for (int i = 0; i < attempts; i++)
        {
            try { return action(); }
            catch (IOException) when (i < attempts - 1) { Thread.Sleep(delayMs); }
        }
        throw new InvalidOperationException("IO retry exhausted");
    }

    private static void RetryOnIo(Action action, int attempts = 5, int delayMs = 30)
        => RetryOnIo<object?>(() => { action(); return null; }, attempts, delayMs);

    private void Log(string message)
    {
        if (config.LogPath is null) return;
        lock (_logLock)
            File.AppendAllText(config.LogPath, $"  MCP {message}\n", new UTF8Encoding(false));
    }
}
