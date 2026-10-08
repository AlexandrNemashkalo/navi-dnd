using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using ModelContextProtocol.Server;
using NaviDnD;
using NaviDnD.Data.Models;

namespace NaviDnD.McpServer;

// Механические патч-помощники: раунд/отдых/двери — детерминированная бухгалтерия, которую раньше
// ИИ приходилось безошибочно воспроизводить по текстовым правилам промпта. Источник как минимум
// двух реальных багов: забытый speedMax при снятии эффекта (§ СОСТОЯНИЯ) и открытая дверь без
// проверки, что герой рядом (§ КАРТА И ОБЪЕКТЫ). Каждый инструмент только ВОЗВРАЩАЕТ кусок патча,
// ничего не пишет сам — McpServer это отдельный процесс, читающий worldState.json с диска, а игра
// держит WorldState в памяти и патчит его из JSON-ответа ИИ, не перечитывая файл; прямая запись
// тут рассинхронила бы процессы.
[McpServerToolType]
public sealed class GameActionTools(McpServerConfig config)
{
    // Без DefaultIgnoreCondition.WhenWritingNull намеренно: "break" должен вернуть color:null
    // явно (движок различает "поле отсутствует" и "поле = null" — см. Storage.ApplyMapUpdate),
    // а не потерять его из-за глобального игнора null-полей.
    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly string[] _partsOfDay = PartsOfDay.Cycle;

    private static readonly object _logLock = new();

    [McpServerTool]
    [Description("""
        Продвигает раунд: totalRounds+1, сброс actions. triggeringActionId = id ресурса, который
        дошёл до 0 в этом ходу (→maxValue-1, остальные→maxValue); не указывать — полный сброс
        (передача хода в бою). Возвращает патч ("time"+"hero") для слияния — combat.currentTurn
        добавь сам, если нужно.
        В бою (combat.active==true): вызывай ТОЛЬКО когда ход героя полностью закончен (потрачены
        Действие/Бон.действие/скорость, либо герой сам явно закончил ход) И герой ПОСЛЕДНИЙ по
        combat.initiative (наименьший score) — тогда раунд закрывает герой. Иначе раунд закрывает
        тот враг, что последний по combat.initiative, в финальном entry его хода — никогда посреди
        хода и никогда просто потому, что один ресурс дошёл до 0, а другие ещё есть.
        """)]
    public string AdvanceRound(
        [Description("id ресурса, который дошёл до 0 в этом ходу, либо не указывать для полного сброса")]
        int? triggeringActionId)
    {
        Log($"advance_round → triggeringActionId:{triggeringActionId?.ToString() ?? "null"}");
        try
        {
            var ws = LoadWorldState();
            var hero = ws.Hero ?? throw new InvalidOperationException("hero missing");
            var actions = hero.Actions ?? throw new InvalidOperationException("hero.actions missing");

            var actionPatches = new List<object>();
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i].Deleted == true) continue;
                int value = triggeringActionId == i ? actions[i].MaxValue - 1 : actions[i].MaxValue;
                actionPatches.Add(new { id = i, value });
            }

            var result = new
            {
                time = new { totalRounds = ws.Time.TotalRounds + 1 },
                hero = new { actions = actionPatches, speedLeft = hero.SpeedMax },
            };
            string resultJson = JsonSerializer.Serialize(result, _writeOptions);
            Log($"advance_round ← ({resultJson.Length} chars): {resultJson}");
            return resultJson;
        }
        catch (Exception ex)
        {
            Log($"advance_round ← (ERROR) {ex.Message}");
            return Error(ex.Message);
        }
    }

    [McpServerTool]
    [Description("""
        Долгий отдых (вызывай только если уже решил, что он разрешён — нет активного эффекта
        onRound с уроном): partOfDay+2, day+1 при переходе через night, totalRounds→1, сбрасывает
        actions до maxValue и speedLeft до speedMax, удаляет эффекты с expiresAtRound+untilLongRest.
        Возвращает патч ("time"+"hero") — восстановление ХП/ресурсов слей в тот же "hero". НЕ
        вызывай следом advance_round — экономика действий уже сброшена этим тулом.
        """)]
    public string LongRest() => Rest(isLong: true);

    [McpServerTool]
    [Description("""
        Короткий отдых (вызывай только если уже решил, что он разрешён): partOfDay+1, day+1 при
        переходе через night, totalRounds→1, сбрасывает actions до maxValue и speedLeft до speedMax,
        удаляет эффекты с expiresAtRound (untilLongRest сохраняются). Возвращает патч
        ("time"+"hero") — восстановление ресурсов слей в тот же "hero". НЕ вызывай следом
        advance_round.
        """)]
    public string ShortRest() => Rest(isLong: false);

    private string Rest(bool isLong)
    {
        string kind = isLong ? "long_rest" : "short_rest";
        Log($"{kind} →");
        try
        {
            var ws = LoadWorldState();
            var hero = ws.Hero ?? throw new InvalidOperationException("hero missing");

            int steps = isLong ? 2 : 1;
            int currentIndex = Array.IndexOf(_partsOfDay, PartsOfDay.Normalize(ws.Time.PartOfDay));
            if (currentIndex < 0) currentIndex = 0;
            int newIndex = (currentIndex + steps) % _partsOfDay.Length;
            int dayIncrement = (currentIndex + steps) / _partsOfDay.Length;

            var effects = hero.Effects ?? [];
            var deletions = new List<object>();
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e.Deleted == true) continue;
                bool remove = e.ExpiresAtRound.HasValue || (isLong && e.UntilLongRest == true);
                if (remove) deletions.Add(new { id = i, deleted = true });
            }

            // Отдых — фактически новый раунд для героя: сбрасываем действия/скорость тем же
            // патчем, чтобы ИИ не пришлось отдельно звать advance_round (и рисковать затереть
            // totalRounds:1, который здесь же вычислен, вторым, несогласованным значением).
            var actionPatches = new List<object>();
            var actions = hero.Actions ?? [];
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i].Deleted == true) continue;
                actionPatches.Add(new { id = i, value = actions[i].MaxValue });
            }

            var heroPatch = new Dictionary<string, object?>
            {
                ["actions"] = actionPatches,
                ["speedLeft"] = hero.SpeedMax,
            };
            if (deletions.Count > 0) heroPatch["effects"] = deletions;

            var result = new Dictionary<string, object?>
            {
                ["time"] = new { totalRounds = 1, partOfDay = _partsOfDay[newIndex], day = ws.Time.Day + dayIncrement },
                ["hero"] = heroPatch,
            };

            string resultJson = JsonSerializer.Serialize(result, _writeOptions);
            Log($"{kind} ← ({resultJson.Length} chars): {resultJson}");
            return resultJson;
        }
        catch (Exception ex)
        {
            Log($"{kind} ← (ERROR) {ex.Message}");
            return Error(ex.Message);
        }
    }

    [McpServerTool]
    [Description("""
        Действие с дверью: open/close/break (герой должен стоять ровно на From или To) или
        hide/reveal (без требования к позиции). Возвращает {"success":true,"patch":{...}} для
        слияния, либо {"success":false,"reason":"..."} — опиши отказ, не резолви сам.
        """)]
    public string DoorAction(
        [Description("id двери из map.doors")] int doorId,
        [Description("вариант действия: open|close|break|hide|reveal")] string action)
    {
        string logName = $"door_action({action})";
        Log($"{logName} → id:{doorId}");
        try
        {
            var ws = LoadWorldState();
            var doors = ws.Map.Doors ?? throw new InvalidOperationException("map.doors missing");
            if (doorId < 0 || doorId >= doors.Count)
                throw new ArgumentException($"door id {doorId} out of range (0..{doors.Count - 1})");
            var door = doors[doorId];

            (object fields, bool requiresAdjacency) = action.ToLowerInvariant() switch
            {
                "open"   => ((object)new { id = doorId, isDoorOpen = true }, true),
                "close"  => (new { id = doorId, isDoorOpen = false }, true),
                "break"  => (new { id = doorId, isDoor = false, color = (object?)null }, true),
                "hide"   => (new { id = doorId, hidden = true }, false),
                "reveal" => (new { id = doorId, hidden = false }, false),
                _ => throw new ArgumentException($"unknown action '{action}' — expected open/close/break/hide/reveal"),
            };

            if (requiresAdjacency)
            {
                bool isPhysicalDoor = door.IsDoor ?? (door.Color?.Count == 3);
                if (!isPhysicalDoor)
                    return Fail(logName, $"map.doors id:{doorId} — это проход, не дверь, действие недоступно.");

                var hero = ws.Hero ?? throw new InvalidOperationException("hero missing");
                var pos = hero.Position ?? throw new InvalidOperationException("hero.position missing");
                // Дверь — это граница МЕЖДУ From и To, не отдельная клетка: герой должен стоять
                // ровно в одной из них, а не рядом (Chebyshev ≤1 до From/To пропускал бы клетку
                // ЗА дверью — герой "через одну клетку" от неё, а не вплотную).
                int dist = Math.Min(ChebyshevDistance(pos, door.From), ChebyshevDistance(pos, door.To));
                if (dist > 0)
                    return Fail(logName,
                        $"Дверь id:{doorId} слишком далеко (герой на [{pos[0]},{pos[1]}], дверь между " +
                        $"[{door.From[0]},{door.From[1]}] и [{door.To[0]},{door.To[1]}]) — нужно встать вплотную (на From или To).");
            }

            var result = new { success = true, patch = new { map = new { doors = new[] { fields } } } };
            string resultJson = JsonSerializer.Serialize(result, _writeOptions);
            Log($"{logName} ← ({resultJson.Length} chars): {resultJson}");
            return resultJson;
        }
        catch (Exception ex)
        {
            Log($"{logName} ← (ERROR) {ex.Message}");
            return Error(ex.Message);
        }
    }

    private string Fail(string action, string reason)
    {
        string resultJson = JsonSerializer.Serialize(new { success = false, reason }, _writeOptions);
        Log($"{action} ← (refused) {reason}");
        return resultJson;
    }

    private static int ChebyshevDistance(List<int> a, List<int> b) =>
        Math.Max(Math.Abs(a[0] - b[0]), Math.Abs(a[1] - b[1]));

    // ── IO helpers (см. CalculateMovementTools для того же паттерна) ─────────

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

    private static string Error(string message) => $"{{\"error\":\"{message}\"}}";

    private void Log(string message)
    {
        if (config.LogPath is null) return;
        lock (_logLock)
            File.AppendAllText(config.LogPath, $"  MCP {message}\n", new UTF8Encoding(false));
    }
}
