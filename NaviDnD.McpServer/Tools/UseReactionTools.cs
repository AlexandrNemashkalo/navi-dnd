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
    [Description("""
        Проверка реакции D&D 5e: у героя есть свободная Реакция, и только что появилась
        возможность её применить (Атака по возможности, заклинание «Щит» и похожие триггеры
        реакции) — спроси, хочет ли он её применить, и жди ответа (до 50 секунд).
        Использовать ТОЛЬКО для этого. НИКОГДА не спрашивай «что делаешь?» — игрок пишет своё
        следующее действие сам. Вопрос — только настоящий, по сути конкретной ситуации; не
        заглушка, не тестовый текст. Нет настоящей причины спросить — не вызывай этот тул вообще.
        Поля:
        - history[]: записи, показываемые игроку перед вопросом. Каждая: text (1-2 предложения, механика в [...]), text может быть SSML <speak> для озвучки; опционально author, patch. Последняя запись = сам вопрос.
        - options: 2-4 варианта ответа, каждый не длиннее 2 слов. Больше 4 вариантов или длиннее — не указывать вовсе (свободный текстовый ввод).
        Возвращает { "answer": "..." } или { "answer": null, "timedOut": true }, если игрок не ответил за 50 секунд.
        """)]
    public string UseReaction(
        [Description("Записи диалога перед вопросом. У каждой text, text может быть SSML <speak> для озвучки; опционально author, patch.")] RequestHistoryEntry[] history,
        [Description("Варианты ответа для игрока. Не указывать для свободного текстового ввода.")] string[]? options)
    {
        // Реальный лог: тул вызвали с пустой history/вопросом — диалог всё равно открылся
        // бы игроку и ждал 50с ответа ни на что. Отклоняем немедленно, не открывая UI.
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
