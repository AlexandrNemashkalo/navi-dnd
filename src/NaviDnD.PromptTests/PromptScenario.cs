using System.Text.Json;
using System.Text.Json.Nodes;
using NaviDnD.Clients;

namespace NaviDnD.PromptTests;

// Calls the REAL Claude (ClaudeCliAiProvider) with a real game system prompt
// (NaviDnD/Prompts/{ruleSet}/{action}/systemPrompt.md) on a real scenario WorldState - this is the “system
// prompt test”: we check that for a given scenario the prompt produces a response that the engine can actually
// apply (Storage.ApplyUpdateWorldState does not throw an exception) AND contains the changes expected for the
// scenario (see explicit assertions in specific tests). By flag, NAVIDND_RECORD_PROMPTS=1 writes a “live”
// response as a canonical mock for NaviDnD.UiTests - this way UI tests remain fast and deterministic, but the
// mock for them comes from a real run of the prompt, and is not made up by hand. IMPORTANT (see
// NaviDnD.PromptTests as a separate project - NOT included in the general run of the solution): - Each call is
// a real request to. Claude: is paid and slow (seconds per call). - The answer is non-deterministic - check the
// STRUCTURE and key INVARIANTS (for example, "HP decreased", "object deleted"), and not the exact text of the
// narrative - AppConfig.ProjectRoot (uses Storage.SavePath/DraftSavePath) resolves RELATIVE TO THE CURRENT
// assembly - for this project it NaviDnD.PromptTests/Storage/, and not the combat NaviDnD/Storage/, so these
// tests never touch the actual saving of the user - Before committing, the recorded response.json should be
// read with your eyes, and not trusted blindly - the non-deterministic output of the model may once turn out to
// be incorrect in meaning (not according to JSON).).
public static class PromptScenario
{
    private static readonly string RealNaviDndDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NaviDnD"));

    private static readonly string UiTestsFixturesDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NaviDnD.UiTests", "Fixtures"));

    // The path to the sources of this project (not bin/Debug/...)) does not require setting copy-to-output for
    // Fixtures/, the same trick that UiTestsFixturesDir uses for the neighboring project.
    private static readonly string LocalFixturesDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string ReadSystemPrompt(string ruleSet, string action) =>
        File.ReadAllText(Path.Combine(RealNaviDndDir, "Prompts", ruleSet, action, "systemPrompt.md"));

    // Loads a ready-made script from the NaviDnD.UiTests fixture (the same files that the E2E UI tests use) -
    // so the script for checking the prompt and the script for the UI- test are guaranteed to match.
    public static Storage LoadUiTestFixture(string fixtureFileName)
    {
        var storage = new Storage();
        storage.LoadFrom(Path.Combine(UiTestsFixturesDir, fixtureFileName));
        return storage;
    }

    // Scenarios specific to checking the prompt (NaviDnD.UiTests) is not needed - for example, several
    // identical enemies to check for target ambiguity (ask_player).
    public static Storage LoadLocalFixture(string fixtureFileName)
    {
        var storage = new Storage();
        storage.LoadFrom(Path.Combine(LocalFixturesDir, fixtureFileName));
        return storage;
    }

    // draftActions - the same as ClaudeCliAiProvider._draftActions (CreateNewGame/FixHeroForNewGame/
    // StartNewGame read/write via MCP draft, not a combat file).
    private static readonly HashSet<string> DraftActions = ["CreateNewGame", "FixHeroForNewGame", "StartNewGame"];

    // Verification AI: NAVIDND_PROMPT_PROVIDER=codex - Codex CLI (NAVIDND_CODEX_MODEL - model,
    // NAVIDND_CODEX_PATH - path), otherwise Claude CLI. The same prompts and MCP-server - so you can see where
    // the other model deviates from the format.
    public static AppConfig TestConfig()
    {
        var config = new AppConfig();
        if (Environment.GetEnvironmentVariable("NAVIDND_PROMPT_PROVIDER") is { Length: > 0 } p) config.AiProvider = p.Trim().ToLowerInvariant();
        if (Environment.GetEnvironmentVariable("NAVIDND_CODEX_MODEL") is { Length: > 0 } m) config.CodexModel = m.Trim();
        if (Environment.GetEnvironmentVariable("NAVIDND_CODEX_PATH") is { Length: > 0 } c) config.CodexCliPath = c.Trim();
        if (Environment.GetEnvironmentVariable("NAVIDND_CODEX_REASONING") is { } effort && effort is "low" or "medium" or "high")
            config.CodexReasoningEffort = effort;
        return config;
    }

    // Saves the script to the path that the MCP-server will read (get_world_state), and calls the REAL
    // provider. actionPath is passed to select the allowedTools/MCP-config (see
    // ClaudeCliAiProvider.PrepareWorkDir) - only the name of the last segment matters (=action).
    public static async Task<string> CallRealAiAsync(
        string ruleSet, string action, Storage storage, string userMessage, string? model = null)
    {
        if (DraftActions.Contains(action)) storage.SaveDraft(); else storage.Save();

        var config = TestConfig();
        IAiProvider provider = new SwitchableAiProvider(config, new AiLogger());
        string actionPath = Path.Combine(RealNaviDndDir, "Prompts", ruleSet, action);
        string promptText = ReadSystemPrompt(ruleSet, action);

        return await provider.Complete([promptText], userMessage, actionPath, model ?? config.ClaudeModel);
    }

    // Like CallRealAiAsync, but additionally raises the background “autoresponder” to roll_dice/ask_player (the
    // same roll_request.json/ask_player_request.json files that poll GameAiClient.SpinAsync in the real game -
    // here without UI, we respond immediately with a placeholder). Necessary for scenarios where the prompt
    // MUST call one of these MCP-tools (attack → throw, ambiguous target → question) - without an answering
    // machine, the call would hang for the real timeout of the MCP-tool (50s).
    public static async Task<AutoAnswerResult> CallRealAiWithAutoAnswersAsync(
        string ruleSet, string action, Storage storage, string userMessage,
        int rollValue = 15, string? askPlayerAnswer = null, string? model = null, (int col, int row)? targetPoint = null)
    {
        if (DraftActions.Contains(action)) storage.SaveDraft(); else storage.Save();

        string storageDir = Path.GetDirectoryName(Storage.SavePath)!;
        string rollPath = Path.Combine(storageDir, "roll_request.json");
        string askPath = Path.Combine(storageDir, "ask_player_request.json");
        string selectPath = Path.Combine(storageDir, "select_target_request.json");
        // In case of debris from a previous crashed run, have your own, non-combat folder, but it’s better not to guess.
        TryDelete(rollPath);
        TryDelete(askPath);
        TryDelete(selectPath);

        var tracker = new AutoAnswerResult("", false, false);
        var trackerBox = new AutoAnswerBox(tracker);

        using var cts = new CancellationTokenSource();
        var pollTask = AutoAnswerLoopAsync(rollPath, askPath, selectPath, storage, rollValue, askPlayerAnswer, trackerBox, cts.Token, targetPoint);
        var logger = new AiLogger();
        int logStart = File.Exists(logger.LogPath) ? File.ReadAllText(logger.LogPath).Length : 0;
        try
        {
            string response = await CallRealAiAsync(ruleSet, action, storage, userMessage, model);
            string trace = File.Exists(logger.LogPath) ? File.ReadAllText(logger.LogPath)[logStart..] : "";
            return trackerBox.Value with { RawResponse = response, ToolTrace = trace };
        }
        finally
        {
            cts.Cancel();
            try { await pollTask; } catch (OperationCanceledException) { }
            TryDelete(rollPath);
            TryDelete(askPath);
            TryDelete(selectPath);
        }
    }

    private sealed class AutoAnswerBox(AutoAnswerResult initial)
    {
        public AutoAnswerResult Value = initial;
    }

    private static async Task AutoAnswerLoopAsync(
        string rollPath, string askPath, string selectPath, Storage storage,
        int rollValue, string? askPlayerAnswer, AutoAnswerBox box, CancellationToken ct, (int col, int row)? targetPoint)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (File.Exists(rollPath))
                {
                    try
                    {
                        string json = await File.ReadAllTextAsync(rollPath, ct);
                        var req = JsonSerializer.Deserialize<RollRequest>(json, JsonOptions);
                        if (req != null && !req.Answered)
                        {
                            ApplyToolHistory(storage, req.History);
                            storage.Save();
                            req.Answered = true;
                            req.Roll1 = rollValue;
                            req.Answer = rollValue;
                            req.Critical = rollValue == 20 ? "critical_success" : rollValue == 1 ? "critical_failure" : null;
                            await File.WriteAllTextAsync(rollPath, JsonSerializer.Serialize(req, JsonOptions), ct);
                            box.Value = box.Value with { RollDiceTriggered = true };
                        }
                    }
                    catch { /*the file could be in the process of being written by the MCP- server - let's try on the next tick*/ }
                }

                if (File.Exists(askPath))
                {
                    try
                    {
                        string json = await File.ReadAllTextAsync(askPath, ct);
                        var req = JsonSerializer.Deserialize<AskPlayerRequest>(json, JsonOptions);
                        if (req != null && !req.Answered)
                        {
                            ApplyToolHistory(storage, req.History);
                            storage.Save();
                            req.Answered = true;
                            req.Answer = askPlayerAnswer ?? (req.Options is { Length: > 0 } ? req.Options[0] : "Продолжаю");
                            await File.WriteAllTextAsync(askPath, JsonSerializer.Serialize(req, JsonOptions), ct);
                            box.Value = box.Value with { AskPlayerTriggered = true };
                        }
                    }
                    catch { }
                }

                // select_target: select the first valid cell not occupied by the hero (without UI - we consider everything visible).
                if (File.Exists(selectPath))
                {
                    try
                    {
                        string json = await File.ReadAllTextAsync(selectPath, ct);
                        var req = JsonSerializer.Deserialize<TargetSelectionRequest>(json, JsonOptions);
                        if (req != null && !req.Answered)
                        {
                            ApplyToolHistory(storage, req.History);
                            storage.Save();
                            var ws = storage.WorldState;
                            var cell = ChooseTarget(ws, req, targetPoint);
                            req.Answered = true;
                            req.Result = cell is { } p
                                ? BuildTargetResult(ws, req, p)
                                : new TargetSelectionResult { Cancelled = true };
                            await File.WriteAllTextAsync(selectPath, JsonSerializer.Serialize(req, JsonOptions), ct);
                            box.Value = box.Value with { SelectTargetTriggered = true };
                        }
                    }
                    catch { }
                }

                await Task.Delay(150, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    internal static (int col,int row)? ChooseTarget(WorldState ws,TargetSelectionRequest request,(int col,int row)? desired=null)
    {
        var heroPos=ws.Hero?.Position;
        return TargetGeometry.SelectableCells(ws,request,(_,_)=>true,(_,_)=>true)
            .Where(c=>request.Mode=="point" || heroPos is not {Count:>=2} || c!=(heroPos[0],heroPos[1]))
            .OrderBy(c=>desired.HasValue?Math.Abs(c.col-desired.Value.col)+Math.Abs(c.row-desired.Value.row):0)
            .ThenBy(c=>c.col).ThenBy(c=>c.row)
            .Cast<(int col,int row)?>().FirstOrDefault();
    }

    internal static TargetSelectionResult BuildTargetResult(WorldState ws, TargetSelectionRequest request, (int col, int row) p)
    {
        var target = TargetGeometry.Describe(ws, p.col, p.row);
        if (request.Shape != null)
        {
            var area = TargetGeometry.AreaCells(ws, p.col, p.row, request.Shape, request.SizeFt);
            target.AreaCells = area.OrderBy(c => c.col).ThenBy(c => c.row).Select(c => new List<int> { c.col, c.row }).ToList();
            target.InArea = TargetGeometry.CreaturesIn(ws, area);
            target.ObjectsInArea = TargetGeometry.ObjectsIn(ws, area);
        }
        return new TargetSelectionResult { Targets = [target] };
    }

    // Same order as streamed UI history: patches precede the answer to the tool.
    internal static void ApplyToolHistory(Storage storage, IEnumerable<RequestHistoryEntry> history)
    {
        foreach (var entry in history)
        {
            if (entry.Patch is { ValueKind: JsonValueKind.Object } patch)
                storage.ApplyUpdateWorldState(patch.GetRawText());
            if (string.IsNullOrEmpty(entry.Text)) continue;
            string text = entry.Text.Replace("\r", "").Replace("\n", "");
            if (text.Length == 0) continue;
            storage.ApplyUpdateWorldState(JsonSerializer.Serialize(new {
                history = new[] { new { author = entry.Author ?? "DM", text } }
            }, JsonOptions));
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // Structural check + parsing for further assertions: applies the answer to the SAME Storage script (as a
    // real engine would do) - both the root patch and nested history[].patch (see Prompts/.../systemPrompt.md §
    // COMBAT). Throws an exception if the AI returned an invalid/inapplicable diff. Returns the texts of
    // history- records - it is convenient to use them to check that the mechanics actually ended up in “[...]”,
    // as required by the prompt.
    public static PromptResult ApplyAndParse(Storage storage, string response)
    {
        string json = ExtractJson(response);
        var root = ParseMergingDuplicateRootKeys(json)
            ?? throw new InvalidOperationException($"Ответ ИИ не распарсился как JSON-объект:\n{response}");

        var historyNode = root["history"]?.DeepClone();
        root.Remove("history");
        string rootJson = root.ToJsonString();
        if (rootJson != "{}") storage.ApplyUpdateWorldState(rootJson);

        var texts = new List<string>();
        if (historyNode is JsonArray historyArr)
        {
            foreach (var entryNode in historyArr)
            {
                if (entryNode is not JsonObject entry) continue;
                if (entry["text"]?.GetValue<string>() is { Length: > 0 } text) texts.Add(text);
                if (entry["patch"] is JsonObject patch)
                {
                    // Like SequentialHistoryPlayer.AnimateAndApplyPatch in a real engine: movePath is a service
                    // field for animation only, "position" (the last element) has already arrived separately -
                    // remove it before ApplyUpdateWorldState, otherwise Storage.ValidateKeys will log "Unknown
                    // field 'movePath'" (not an error, but extra noise, which in real engine with the same
                    // scheme does not happen).
                    if (patch["map"]?["entities"] is JsonArray entities)
                        foreach (var entityNode in entities.OfType<JsonObject>())
                            entityNode.Remove("movePath");
                    storage.ApplyUpdateWorldState(patch.ToJsonString());
                }
            }
        }

        return new PromptResult(response, texts, root);
    }

    public static void RecordAsMock(string response, string mockResponsesFolder, string action)
    {
        if (Environment.GetEnvironmentVariable("NAVIDND_RECORD_PROMPTS") != "1") return;
        string targetDir = Path.Combine(UiTestsFixturesDir, "MockResponses", mockResponsesFolder, action);
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "response.json"), response);
    }

    // Copy of SequentialHistoryPlayer.ParseMergingDuplicateRootKeys (private there, the real engine tolerates
    // duplicate root keys like two "hero": instead of one combined - see her comment) - the test should also
    // not fail on this model typo, and not reject an answer that would be applied normally in a real game.
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

    // Copy of GameAiClient.ExtractJson (private there) - cuts out the first balanced {...} from the model's
    // response text (in case of framing text/markdown, which ClaudeCliAiProvider usually already removes
    // itself, but extra protection won't hurt here).
    private static string ExtractJson(string text)
    {
        int start = text.IndexOf('{');
        if (start < 0) return text;

        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) return text[start..(i + 1)]; }
        }

        return text[start..] + new string('}', depth);
    }
}

// Texts of all history- response records (for checking narrative/mechanics) + raw root JsonObject (for
// point-by-point checks of non-standard fields, if necessary).
public record PromptResult(string RawResponse, List<string> HistoryTexts, JsonObject Root)
{
    public bool AnyTextContains(string substring) =>
        HistoryTexts.Any(t => t.Contains(substring, StringComparison.OrdinalIgnoreCase));
}

public record AutoAnswerResult(string RawResponse, bool RollDiceTriggered, bool AskPlayerTriggered, bool SelectTargetTriggered = false, string ToolTrace = "");
