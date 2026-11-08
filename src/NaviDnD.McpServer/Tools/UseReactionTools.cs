using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using NaviDnD;

namespace NaviDnD.McpServer;

[McpServerToolType]
public sealed class UseReactionTools(McpServerConfig config)
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly object _logLock = new();

    private string AskPlayerRequestPath =>
        Path.Combine(Path.GetDirectoryName(config.WorldStatePath)!, "ask_player_request.json");

    [McpServerTool]
    [Description("Ask about a D&D 5e reaction ONLY when the hero has an available Reaction and a real opportunity just arose (opportunity attack, Shield spell, similar trigger). Wait for the answer, up to 50 seconds. Never ask what the hero does next; the player supplies their own actions. No placeholder/test questions or calls without a genuine reaction choice.\nhistory[]: entries shown before the question, each with text (1–2 sentences, mechanics in [...], optionally SSML <speak>), optional author and patch. The last entry is the question in the game language.\noptions: 2–4 choices, at most 2 words each. If more/longer choices are needed, omit options for free text.\nReturns {answer:\"...\"}, or {answer:null,timedOut:true} after 50 seconds.")]
    public string UseReaction(
        [Description("History shown before the question: text (optionally SSML <speak>), optional author and patch; use the game language.")] RequestHistoryEntry[] history,
        [Description("Player response options; omit for free-text input.")] string[]? options)
    {
        // Real log: the tool was called with an empty history/ question - the dialogue would still open to the
        // player and wait 50 seconds for an answer to nothing. Reject immediately without opening UI.
        if (history is not { Length: > 0 } || string.IsNullOrWhiteSpace(history[^1].Text))
        {
            Log("use_reaction → (REJECTED) empty history or last entry has no question text");
            return JsonSerializer.Serialize(new
            {
                error = "history must be non-empty and its last entry's text must be a real question. " +
                        "Do not call use_reaction without an actual question for the player."
            }, _json);
        }

        var request = new AskPlayerRequest
        {
            History = history?.ToList() ?? [],
            Options = options,
        };

        string lastText = history is { Length: > 0 } ? history[^1].Text : "";
        string optDesc = options is { Length: > 0 } ? string.Join(" | ", options) : "free-text";
        Log($"use_reaction → \"{lastText}\" [{optDesc}]");

        WriteRequest(request);

        var deadline = DateTime.UtcNow.AddSeconds(50);
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(200);
            var current = TryReadRequest();
            if (current?.Answered == true)
            {
                TryDelete();
                Log($"use_reaction ← answer=\"{current.Answer}\"");
                return JsonSerializer.Serialize(new { answer = current.Answer }, _json);
            }
        }

        TryDelete();
        Log("use_reaction ← timedOut");
        return JsonSerializer.Serialize(new { answer = (string?)null, timedOut = true }, _json);
    }

    private void WriteRequest(AskPlayerRequest request)
    {
        string json = JsonSerializer.Serialize(request, _json);
        RetryOnIo(() => File.WriteAllText(AskPlayerRequestPath, json, new UTF8Encoding(false)));
    }

    private AskPlayerRequest? TryReadRequest()
    {
        try
        {
            if (!File.Exists(AskPlayerRequestPath)) return null;
            string json = RetryOnIo(() =>
            {
                using var stream = new FileStream(AskPlayerRequestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            });
            return JsonSerializer.Deserialize<AskPlayerRequest>(json, _json);
        }
        catch { return null; }
    }

    private void TryDelete()
    {
        try { if (File.Exists(AskPlayerRequestPath)) File.Delete(AskPlayerRequestPath); }
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
            File.AppendAllText(config.LogPath, $"  MCP {message}\n", new System.Text.UTF8Encoding(false));
    }
}
