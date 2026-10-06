using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using NaviDnD;

namespace NaviDnD.McpServer;

[McpServerToolType]
public sealed class RollDiceTools(McpServerConfig config)
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly object _logLock = new();

    private string RollRequestPath =>
        Path.Combine(Path.GetDirectoryName(config.WorldStatePath)!, "roll_request.json");

    [McpServerTool]
    [Description("""
        Запрашивает у игрока бросок d20 (атака, спасбросок, проверка навыка, инициатива, спасбросок
        от смерти). Вызывай ТОЛЬКО когда по правилам D&D 5e действительно нужен бросок кубика для
        разрешения действия. НЕ используй для бросков врагов — моделируй их сам.
        Поля:
        - history[]: записи перед UI кубика. Каждая: text (1-2 предложения, механика в [...]), text может быть SSML <speak> для озвучки; опционально author, patch. Последняя запись — контекст броска (например "Бросай на инициативу!"), ОБЯЗАТЕЛЬНА, не пустая.
        - difficulty: DC/КД для показа (не указывать чтобы скрыть). advantage/disadvantage: bool. modifiers: [{"name","value"}].
         Возвращает { roll, total }. roll = чистое значение d20. Модификаторы ты применяешь сам после получения результата.
        """)]
    public string RollDice(
        [Description("Записи диалога перед UI кубика. У каждой text, text может быть SSML <speak> для озвучки; опционально author, patch.")] RequestHistoryEntry[] history,
        [Description("Класс Сложности (DC) для показа игроку. Не указывать если неизвестен или скрыт.")] int? difficulty,
        [Description("Бросок с преимуществом (два кубика, берём больший).")] bool advantage,
        [Description("Бросок с помехой (два кубика, берём меньший).")] bool disadvantage,
        [Description("Модификаторы броска. Показываются игроку для прозрачности.")] RollModifier[]? modifiers)
    {
        if (history is not { Length: > 0 } || string.IsNullOrWhiteSpace(history[^1].Text))
        {
            Log("roll_dice → (REJECTED) empty history or last entry has no roll context");
            return JsonSerializer.Serialize(new
            {
                error = "history must be non-empty and its last entry's text must give a real roll context. " +
                        "Do not call roll_dice without an actual reason to roll."
            }, _json);
        }

        // Every legitimate D&D 5e d20 roll carries at least an ability/proficiency modifier or a DC
        // to beat (death saves are the one flat exception, and those normally show DC 10). A roll
        // with neither is not a real mechanic — most often a self-buff activation (e.g. Rage) that
        // needs no roll at all and should just be narrated + patched directly.
        if (modifiers is not { Length: > 0 } && !difficulty.HasValue)
        {
            Log("roll_dice → (REJECTED) no modifiers and no difficulty — not a real d20 check");
            return JsonSerializer.Serialize(new
            {
                error = "no modifiers and no difficulty given — this doesn't look like a real D&D 5e " +
                        "d20 check (attack/save/check/initiative all carry a modifier; death saves show " +
                        "DC 10). Re-examine whether this action actually needs a roll at all."
            }, _json);
        }

        var request = new RollRequest
        {
            History = history?.ToList() ?? [],
            Difficulty = difficulty,
            Advantage = advantage,
            Disadvantage = disadvantage,
            Modifiers = modifiers?.ToList() ?? [],
        };

        string lastText = history is { Length: > 0 } ? history[^1].Text : "";
        string modDesc = modifiers is { Length: > 0 }
            ? string.Join(", ", modifiers.Select(m => $"{m.Name}={m.Value:+0;-0}"))
            : "none";
        string rollType = advantage ? "advantage" : disadvantage ? "disadvantage" : "normal";
        string dcPart = difficulty.HasValue ? $" DC={difficulty}" : "";
        Log($"roll_dice → \"{lastText}\"{dcPart} [{rollType}] mods=[{modDesc}]");

        WriteRequest(request);

        var deadline = DateTime.UtcNow.AddSeconds(50);
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(200);
            var current = TryReadRequest();
            if (current?.Answered == true)
            {
                TryDelete();
                int roll = current.Answer ?? 1;
                int modSum = modifiers?.Sum(m => m.Value) ?? 0;
                var result = new { roll, roll1 = current.Roll1, roll2 = current.Roll2, modifiers = modSum, total = roll + modSum, critical = current.Critical };
                Log($"roll_dice ← roll={roll} total={roll + modSum}" + (current.Roll2.HasValue ? $" (pair: {current.Roll1}/{current.Roll2})" : "") + (current.Critical != null ? $" [{current.Critical}]" : ""));
                return JsonSerializer.Serialize(result, _json);
            }
        }

        TryDelete();
        int timeoutModSum = modifiers?.Sum(m => m.Value) ?? 0;
        Log($"roll_dice ← timedOut roll=1 total={1 + timeoutModSum}");
        return JsonSerializer.Serialize(new
        {
            roll = 1,
            roll1 = (int?)1,
            roll2 = (int?)null,
            modifiers = timeoutModSum,
            total = 1 + timeoutModSum,
            timedOut = true
        }, _json);
    }

    private void WriteRequest(RollRequest request)
    {
        string json = JsonSerializer.Serialize(request, _json);
        RetryOnIo(() => File.WriteAllText(RollRequestPath, json, new UTF8Encoding(false)));
    }

    private RollRequest? TryReadRequest()
    {
        try
        {
            if (!File.Exists(RollRequestPath)) return null;
            string json = RetryOnIo(() =>
            {
                using var stream = new FileStream(RollRequestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            });
            return JsonSerializer.Deserialize<RollRequest>(json, _json);
        }
        catch { return null; }
    }

    private void TryDelete()
    {
        try { if (File.Exists(RollRequestPath)) File.Delete(RollRequestPath); }
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
