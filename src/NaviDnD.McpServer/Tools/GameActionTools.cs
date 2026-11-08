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

// Mechanical patch assistants: round/rest/doors - deterministic accounting, which previously the AI had to
// accurately reproduce according to the textual rules of the prompt. The source of at least two real bugs:
// speedMax being forgotten when removing the effect (§ CONDITIONS) and an open door without checking that the
// hero is nearby (§ MAP AND OBJECTS). Each tool only RETURNS a piece of the patch, does not write anything
// itself - McpServer is a separate process that reads worldState.json from disk, and the game keeps WorldState
// in memory and patches it from the AI's JSON- response without re-reading the file; direct recording here
// would desynchronize the processes.
[McpServerToolType]
public sealed class GameActionTools(McpServerConfig config)
{
    // Without DefaultIgnoreCondition.WhenWritingNull intentionally: "break" should return color:null explicitly
    // (the engine distinguishes between "field missing" and "field = null" - see Storage.ApplyMapUpdate),
    // rather than losing it due to global ignoring of null- fields.
    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly string[] _partsOfDay = PartsOfDay.Cycle;

    private static readonly object _logLock = new();

    [McpServerTool]
    [Description("Advance the round: totalRounds+1 and reset actions. Optional triggeringActionId identifies the resource exhausted this turn: reset it to maxValue−1, others to maxValue. Omit for a full reset at a combat round boundary. Returns a time+hero patch to merge; add combat.currentTurn yourself when needed.\nIn combat, call only at the boundary after the LAST combatant in initiative order has finished, never mid-turn or merely because one resource reached zero while others remain. If the hero is last (lowest score), their turn must be fully spent or explicitly ended. Otherwise advance after the last enemy's turn, placing the returned patch at the round transition.")]
    public string AdvanceRound(
        [Description("ID of the action resource exhausted this turn; omit for a full reset.")]
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
    [Description("Long rest. Call only after deciding it is allowed, with no active damaging onRound effect. Advances partOfDay by 2, increments day across night, sets totalRounds to 1, restores actions to maxValue and speedLeft to speedMax, and removes expiresAtRound and untilLongRest effects. Returns a time+hero patch; merge HP/resource recovery into the same hero. Do NOT call advance_round afterward; actions were already reset.")]
    public string LongRest() => Rest(isLong: true);

    [McpServerTool]
    [Description("Short rest. Call only after deciding it is allowed. Advances partOfDay by 1, increments day across night, sets totalRounds to 1, restores actions to maxValue and speedLeft to speedMax, and removes expiresAtRound effects while retaining untilLongRest effects. Returns a time+hero patch; merge recovery into the same hero. Do NOT call advance_round afterward.")]
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

            // Rest is actually a new round for the hero: we reset the actions/speed with the same patch, so
            // that the AI does not have to separately call advance_round (and risk overwriting totalRounds:1,
            // which is also calculated here, with a second, inconsistent value).
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
    [Description("Door action: open/close/break requires the hero to stand exactly on From or To; hide/reveal has no position requirement. Returns {success:true,patch:{...}} to merge, or {success:false,reason:\"...\"}. Narrate a refusal; do not bypass it by resolving the action yourself.")]
    public string DoorAction(
        [Description("Door ID from map.doors.")] int doorId,
        [Description("Action: open|close|break|hide|reveal.")] string action)
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
            if (door.IsWorldExit == true)
                return Fail(logName, "Door actions cannot modify a world-map exit.");

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
                    return Fail(logName, $"map.doors id:{doorId} is a passage, not a door; this action is unavailable.");

                var hero = ws.Hero ?? throw new InvalidOperationException("hero missing");
                var pos = hero.Position ?? throw new InvalidOperationException("hero.position missing");
                // The door is the border BETWEEN From and To, not a separate cell: the hero must stand exactly
                // in one of them, and not next to it (Chebyshev ≤1 to From/To would pass the cell BEHIND the
                // door - the hero is “one cell” away from it, and not right next to it).
                int dist = Math.Min(ChebyshevDistance(pos, door.From), ChebyshevDistance(pos, door.To));
                if (dist > 0)
                    return Fail(logName,
                        $"Door id:{doorId} is too far away (hero at [{pos[0]},{pos[1]}], door between " +
                        $"[{door.From[0]},{door.From[1]}] and [{door.To[0]},{door.To[1]}]); stand adjacent on From or To.");
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

    // ── IO helpers (see CalculateMovementTools for the same pattern) ─────────

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
            File.AppendAllText(config.LogPath, $"  MCP {message}\n", new UTF8Encoding(false));
    }
}
