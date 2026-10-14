using NaviDnD.Helpers;
using NaviDnD.Data.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NaviDnD.Clients;

// Processes a full AI response JSON sequentially element by element:
// typewriter text → movePath animation → patch applied → next element.
internal sealed class SequentialHistoryPlayer(
    WorldState settings,
    Storage storage,
    JsonSerializerOptions serializeOptions,
    Action<string>? onChunk,
    Action<string>? onNewMessage,
    Action? onRedraw,
    Action? onPoll = null,
    AnimationLoop? animations = null)
{
    public SequentialHistoryPlayer(WorldState settings, Storage storage, JsonSerializerOptions serializeOptions,
        Action<string>? onChunk, Action<string>? onNewMessage, Action? onRedraw, Action? onPoll)
        : this(settings, storage, serializeOptions, onChunk, onNewMessage, onRedraw, onPoll, null) { }

    private readonly AnimationLoop _animations = animations ?? new();
    private const int CharDelayMs = 40;    // readable cadence, independent of rendering cost
    private const int MoveStepDelayMs = 500;

    // Returns the history entries that were displayed, stripped of newlines.
    // Caller is responsible for persisting them to settings.History after FinalizeStreaming.
    public async Task<List<DialogMessage>> PlayAsync(string json)
    {
        var rootNode = ParseMergingDuplicateRootKeys(json);
        if (rootNode == null) return [];

        var historyNode = rootNode["history"]?.DeepClone();
        rootNode.Remove("history");
        string rootJson = rootNode.ToJsonString(serializeOptions);
        if (rootJson != "{}") storage.ApplyUpdateWorldState(rootJson);

        if (historyNode is not JsonArray historyArr) return [];

        var accumulatedHistory = new List<DialogMessage>();

        foreach (var entryNode in historyArr)
        {
            if (entryNode is not JsonObject entry) continue;

            string rawText = entry["text"]?.GetValue<string>() ?? "";
            string text = Speech.DisplayText(rawText);
            string? author = entry["author"]?.GetValue<string>();
            string? speechText = Speech.ValidateSsml(rawText);
            string normalizedText = text.Replace("\n", "");

            // The prompt prohibits repeating in the final history[] what was already shown to the player
            // through history[] from roll_dice/use_reaction (those records end up in settings.History
            // immediately when the tool is called - DialogDisplay.StreamHistoryEntries, long before that
            // PlayAsync) - the model sometimes still violates this rule. We silently skip a verbatim repetition
            // of the last already shown record, rather than showing the player one replica twice in a row.
            bool isDuplicateOfLast = normalizedText.Length > 0
                && settings.History is { Count: > 0 } h
                && h[^1].Text == normalizedText;

            if (!isDuplicateOfLast)
            {
                await PlayText(text, author, speechText);

                if (!string.IsNullOrEmpty(text))
                    accumulatedHistory.Add(new DialogMessage { Text = normalizedText, Author = author!, SpeechText = speechText });
            }

            if (entry["patch"] is JsonObject patch) await AnimateAndApplyPatch(patch);
            // Hits written in the recording mechanics (“[… WL2 11→7]”), but forgotten in the patch.
            if (storage.ReconcileMechanics(normalizedText)) onRedraw?.Invoke();
        }

        return accumulatedHistory;
    }

    // AI sometimes sends the same root key twice (for example, two separate "hero":{...} instead of combining
    // into one) contrary to the explicit prohibition in the prompt - the usual JsonNode.Parse falls on this
    // (unlike Storage.ApplyUpdateWorldState, which reads through JsonDocument. EnumerateObject and calmly
    // processes the duplicates in turn). We merge duplicates of the top-level keys before parsing history, so
    // that one typo in the model does not ruin the entire player’s answer.
    private static JsonObject? ParseMergingDuplicateRootKeys(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

        var merged = new JsonObject();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var value = JsonNode.Parse(prop.Value.GetRawText());
            if (merged.TryGetPropertyValue(prop.Name, out var existingNode) &&
                existingNode is JsonObject existing && value is JsonObject incoming)
            {
                foreach (var kv in incoming.ToList())
                    existing[kv.Key] = kv.Value?.DeepClone();
            }
            else
            {
                merged[prop.Name] = value;
            }
        }
        return merged;
    }

    private async Task PlayText(string text, string? author, string? speechText)
    {
        if (string.IsNullOrEmpty(text)) return;
        onNewMessage?.Invoke(author ?? "DM");
        if (onChunk == null) return;
        using var speechMessage = Speech.BeginMessage(text, author, speechText);
        if (!text.Any(c => c is not ('\n' or '\r'))) return;
        await _animations.PlayAsync(text.Where(c => c is not ('\n' or '\r')).Select(c =>
            new AnimationLoop.Frame(TimeSpan.FromMilliseconds(CharDelayMs), () =>
            {
                onChunk(c.ToString());
                Sound.PlayTyping(c);
            })), onPoll, catchUp: false);

    }

    private async Task AnimateAndApplyPatch(JsonObject patch)
    {
        if (patch["map"]?["entities"] is JsonArray entities)
            foreach (var entityNode in entities.OfType<JsonObject>())
            {
                await AnimateEntityMovePath(entityNode);
                entityNode.Remove("movePath");
            }

        storage.ApplyUpdateWorldState(patch.ToJsonString(serializeOptions));
        onRedraw?.Invoke();
    }

    private async Task AnimateEntityMovePath(JsonObject entityNode)
    {
        if (entityNode["movePath"] is not JsonArray movePath) return;
        if (entityNode["id"] is not JsonValue idVal || !idVal.TryGetValue<int>(out int id)) return;
        if (settings.Map.Entities == null || id < 0 || id >= settings.Map.Entities.Count) return;

        var entity = settings.Map.Entities[id];
        foreach (var stepNode in movePath)
        {
            if (stepNode is not JsonArray step) continue;
            var pos = step.Select(n => n?.GetValue<int>() ?? 0).ToList();
            if (pos.Count >= 2)
            {
                entity.Position = pos;
                onRedraw?.Invoke();
                await _animations.DelayAsync(TimeSpan.FromMilliseconds(MoveStepDelayMs), onPoll);
            }
        }
    }
}
