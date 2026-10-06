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
    [Description("""
        Выбор цели или места мышью на карте. Любой эффект по области (сфера/куб/конус/линия) — вызывай
        всегда, с shape и sizeFt, даже если место названо словами: игрок должен увидеть и выбрать зону.
        Одиночная цель — только если неясно, кого именно (несколько подходящих существ); однозначна — не вызывай.
        Игрок видит подсветку допустимых клеток и превью области; ждёт ответа до 50 секунд.
        Поля:
        - history[]: записи перед выбором, как у use_reaction. Последняя запись = что выбрать («Куда направить Огненный шар?»).
        - mode: "creature" — выбрать существо (только видимые герою; сам герой тоже можно), "point" — выбрать клетку (исследованную, в прямой видимости).
        - rangeFt: максимальная дистанция от героя в футах. Не указывать — без ограничения.
        - count: сколько целей выбрать (по умолчанию 1). allowRepeat: можно ли выбрать одну цель несколько раз.
        - shape: форма области вокруг выбранной точки — "sphere" (радиус sizeFt), "cube" (сторона sizeFt), или от героя в сторону выбранной клетки — "cone" (длина sizeFt), "line" (длина sizeFt, ширина 5 фт). Без области — не указывать.
        - sizeFt: размер области в футах.
        Возвращает { "targets": [{ "id", "symbol", "name", "position", "distanceFt" }] }; для области ещё
        "areaCells" (все клетки [col,row]), "inArea" (существа, включая героя), "objectsInArea" (объекты карты).
        id — индекс в map.entities / map.objects для патча (у героя нет).
        Игрок отменил — { "cancelled": true }; не ответил за 50 секунд — { "timedOut": true }: действие не выполняется, опиши это коротко.
        """)]
    public string SelectTarget(
        [Description("Записи диалога перед выбором. У каждой text, text может быть SSML <speak> для озвучки; опционально author, patch.")] RequestHistoryEntry[] history,
        [Description("\"creature\" или \"point\".")] string mode,
        [Description("Максимальная дистанция от героя в футах.")] int? rangeFt = null,
        [Description("Сколько целей выбрать.")] int count = 1,
        [Description("Можно ли выбрать одну цель несколько раз.")] bool allowRepeat = false,
        [Description("\"sphere\", \"cube\", \"cone\" или \"line\".")] string? shape = null,
        [Description("Размер области в футах.")] int? sizeFt = null)
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
