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
    [Description("Long rest (8 hours). Call only after deciding it is allowed, with no damaging onRound effect or unresolved interruption during the interval. Advances the local clock, rebases all active deadlines across day parts, restores actions/speed, expires only due effects and hero untilLongRest effects. Future effects survive. Returns a complete time/hero/map/scheduledEvents patch; preserve it and merge HP/resource recovery. Due hero/NPC/object onExpire and events remain for RoundBundle; resolve due inventory item onExpire yourself. Do NOT call advance_round afterward.")]
    public string LongRest(
        [Description("Whole days already elapsed in this response before resting but not yet applied to saved time (for example a travel detour). Do not include days already charged by the engine.")] int elapsedDays = 0) => Rest(isLong: true, elapsedDays);

    [McpServerTool]
    [Description("Short rest (1 hour). Call only after deciding it is allowed, with no damaging onRound effect or unresolved interruption during the interval. Advances the local clock, rebases all active deadlines across day parts, restores actions/speed and expires only due effects; future and untilLongRest effects survive. Returns a complete time/hero/map/scheduledEvents patch; preserve it and merge recovery. Due hero/NPC/object onExpire and events remain for RoundBundle; resolve due inventory item onExpire yourself. Do NOT call advance_round afterward.")]
    public string ShortRest(
        [Description("Whole days already elapsed in this response before resting but not yet applied to saved time. Do not include days already charged by the engine.")] int elapsedDays = 0) => Rest(isLong: false, elapsedDays);

    private string Rest(bool isLong, int elapsedDays)
    {
        string kind = isLong ? "long_rest" : "short_rest";
        Log($"{kind} →");
        try
        {
            if (elapsedDays < 0) throw new ArgumentOutOfRangeException(nameof(elapsedDays), "Elapsed days cannot be negative.");
            var ws = LoadWorldState();
            var hero = ws.Hero ?? throw new InvalidOperationException("hero missing");

            int currentIndex = Array.IndexOf(_partsOfDay, PartsOfDay.Normalize(ws.Time.PartOfDay));
            if (currentIndex < 0) currentIndex = 0;
            // The existing clock counts six-second rounds within a six-hour day part.
            // Keep that format, moving deadlines into the same new local coordinate system.
            int endRound = checked(ws.Time.TotalRounds + elapsedDays * 14400 + (isLong ? 4800 : 600));
            int steps = (endRound - 1) / 3600;
            int nextRound = (endRound - 1) % 3600 + 1;
            int offset = checked(steps * 3600);
            int newIndex = (currentIndex + steps) % _partsOfDay.Length;
            int dayIncrement = (currentIndex + steps) / _partsOfDay.Length;

            List<object> Effects(List<StatusEffect>? effects, bool restingOwner)
            {
                var patches = new List<object>();
                for (int i = 0; i < (effects?.Count ?? 0); i++)
                {
                    var effect = effects![i];
                    if (effect.Deleted == true) continue;
                    bool due = effect.ExpiresAtRound is int deadline && deadline < endRound
                        || restingOwner && isLong && effect.UntilLongRest == true;
                    if (due)
                    {
                        if (effect.OnExpire == null) patches.Add(new { id = i, deleted = true });
                        // Keep the callback and its owner until the normal expiry handler resolves it.
                        else patches.Add(new { id = i, expiresAtRound = 0, onExpire = effect.OnExpire });
                    }
                    else if (effect.ExpiresAtRound is int expires && offset != 0)
                        patches.Add(new { id = i, expiresAtRound = Math.Max(0, expires - offset) });
                }
                return patches;
            }

            // Reset the hero's resources together with the clock; no second advance_round is needed.
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
            var heroEffects = Effects(hero.Effects, true);
            if (heroEffects.Count > 0) heroPatch["effects"] = heroEffects;
            var inventory = hero.Inventory.Select((item, id) => (item, id))
                .Where(x => x.item.Deleted != true).Select(x => new { id = x.id, effects = Effects(x.item.Effects, true) })
                .Where(x => x.effects.Count > 0).ToList();
            if (inventory.Count > 0) heroPatch["inventory"] = inventory;

            List<object> Owners<T>(List<T>? owners) where T : CellEntity => (owners ?? []).Select((owner, id) => (owner, id))
                .Where(x => x.owner.Deleted != true).Select(x => new { id = x.id, effects = Effects(x.owner.Effects, false) })
                .Where(x => x.effects.Count > 0).Cast<object>().ToList();
            var mapPatch = new Dictionary<string, object>();
            var entities = Owners(ws.Map.Entities);
            var objects = Owners(ws.Map.Objects);
            if (entities.Count > 0) mapPatch["entities"] = entities;
            if (objects.Count > 0) mapPatch["objects"] = objects;

            var result = new Dictionary<string, object?>
            {
                ["time"] = new { totalRounds = nextRound, partOfDay = _partsOfDay[newIndex], day = checked(ws.Time.Day + dayIncrement) },
                ["hero"] = heroPatch,
            };
            if (mapPatch.Count > 0) result["map"] = mapPatch;
            if (offset != 0 && ws.ScheduledEvents is { Count: > 0 } events)
                result["scheduledEvents"] = events.Select((ev, id) => (ev, id)).Where(x => x.ev.Deleted != true)
                    .Select(x => new { id = x.id, fireAtRound = Math.Max(1, x.ev.FireAtRound - offset) }).ToList();

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
    [Description("Door action: open/close/break requires the actor to stand exactly on From or To in saved state. entityId=-1 (default) acts as the hero; an original map.entities ID acts as that NPC. Select the actual actor, never substitute a nearby creature for the hero. hide/reveal has no position requirement. Returns {success:true,patch:{...}} to merge, or {success:false,reason:\"...\"}. Narrate a refusal; do not bypass it by resolving the action yourself.")]
    public string DoorAction(
        [Description("Door ID from map.doors.")] int doorId,
        [Description("Action: open|close|break|hide|reveal.")] string action,
        [Description("Acting NPC's original map.entities ID, or -1 for the hero.")] int entityId = -1)
    {
        string logName = $"door_action({action})";
        Log($"{logName} → id:{doorId}, actor:{entityId}");
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

                List<int>? pos;
                string actor;
                if (entityId == -1)
                {
                    pos = ws.Hero?.Position;
                    actor = "hero";
                }
                else
                {
                    var entities = ws.Map.Entities;
                    if (entityId < 0 || entities == null || entityId >= entities.Count || entities[entityId].Deleted == true)
                        return Fail(logName, $"Actor map.entities id:{entityId} is unavailable.");
                    var entity = entities[entityId];
                    if (int.TryParse(entity.Hp.Split('/')[0], out int hp) && hp <= 0)
                        return Fail(logName, $"Actor map.entities id:{entityId} has no HP and cannot act.");
                    pos = entity.Position;
                    actor = $"map.entities id:{entityId}";
                }
                if (pos is not { Count: >= 2 }) return Fail(logName, $"{actor} position missing.");
                // The door is the border BETWEEN From and To, not a separate cell: the actor must stand exactly
                // in one of them, and not next to it (Chebyshev ≤1 to From/To would pass the cell BEHIND the
                // door - the actor is “one cell” away from it, and not right next to it).
                int dist = Math.Min(ChebyshevDistance(pos, door.From), ChebyshevDistance(pos, door.To));
                if (dist > 0)
                    return Fail(logName,
                        $"Door id:{doorId} is too far away ({actor} at [{pos[0]},{pos[1]}], door between " +
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
