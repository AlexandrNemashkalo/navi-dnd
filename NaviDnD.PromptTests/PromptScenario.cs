using System.Text.Json;
using System.Text.Json.Nodes;
using NaviDnD.Clients;

namespace NaviDnD.PromptTests;

// Вызывает РЕАЛЬНОГО Claude (ClaudeCliAiProvider) с настоящим системным промптом игры
// (NaviDnD/Prompts/{ruleSet}/{action}/systemPrompt.md) на настоящем сценарии WorldState — это и
// есть "тест системного промпта": проверяем, что для данного сценария промпт производит ответ,
// который движок реально может применить (Storage.ApplyUpdateWorldState не бросает исключение) И
// содержит ожидаемые для сценария изменения (см. явные ассерты в конкретных тестах). По флагу
// NAVIDND_RECORD_PROMPTS=1 записывает "живой" ответ как канонический мок для NaviDnD.UiTests — так
// UI-тесты остаются быстрыми и детерминированными, но мок для них происходит от реального прогона
// промпта, а не выдуман руками.
//
// ВАЖНО (см. NaviDnD.PromptTests как отдельный проект — НЕ входит в общий прогон решения):
// - Каждый вызов — реальный запрос к Claude: платно и медленно (секунды на вызов).
// - Ответ недетерминирован — проверяй СТРУКТУРУ и ключевые ИНВАРИАНТЫ (например "HP уменьшилось",
//   "объект удалён"), а не точный текст нарратива.
// - AppConfig.ProjectRoot (использует Storage.SavePath/DraftSavePath) резолвится ОТНОСИТЕЛЬНО ТЕКУЩЕЙ
//   сборки — для этого проекта это NaviDnD.PromptTests/Storage/, а не боевой NaviDnD/Storage/,
//   так что реальное сохранение пользователя эти тесты никогда не трогают.
// - Перед коммитом записанный response.json стоит прочитать глазами, а не доверять вслепую —
//   недетерминированный вывод модели может один раз получиться некорректным по смыслу (не по JSON).
public static class PromptScenario
{
    private static readonly string RealNaviDndDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NaviDnD"));

    private static readonly string UiTestsFixturesDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NaviDnD.UiTests", "Fixtures"));

    // Путь к исходникам этого проекта (а не bin/Debug/...) — не требует настройки copy-to-output
    // для Fixtures/, тот же приём, что UiTestsFixturesDir использует для соседнего проекта.
    private static readonly string LocalFixturesDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string ReadSystemPrompt(string ruleSet, string action) =>
        File.ReadAllText(Path.Combine(RealNaviDndDir, "Prompts", ruleSet, action, "systemPrompt.md"));

    // Загружает готовый сценарий из фикстуры NaviDnD.UiTests (те же файлы, что используют E2E UI-
    // тесты) — так сценарий для проверки промпта и сценарий для UI-теста гарантированно совпадают.
    public static Storage LoadUiTestFixture(string fixtureFileName)
    {
        var storage = new Storage();
        storage.LoadFrom(Path.Combine(UiTestsFixturesDir, fixtureFileName));
        return storage;
    }

    // Сценарии, специфичные для проверки промпта (не нужны NaviDnD.UiTests) — например, несколько
    // одинаковых врагов для проверки неоднозначности цели (ask_player).
    public static Storage LoadLocalFixture(string fixtureFileName)
    {
        var storage = new Storage();
        storage.LoadFrom(Path.Combine(LocalFixturesDir, fixtureFileName));
        return storage;
    }

    // draftActions — те же, что ClaudeCliAiProvider._draftActions (CreateNewGame/FixHeroForNewGame/
    // StartNewGame читают/пишут через MCP черновик, не боевой файл).
    private static readonly HashSet<string> DraftActions = ["CreateNewGame", "FixHeroForNewGame", "StartNewGame"];

    // Нейронка проверки: NAVIDND_PROMPT_PROVIDER=codex — Codex CLI (NAVIDND_CODEX_MODEL — модель, NAVIDND_CODEX_PATH —
    // путь), иначе Claude CLI. Те же промпты и MCP-сервер — так видно, где другая модель отступает от формата.
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

    // Сохраняет сценарий в путь, который прочитает MCP-сервер (get_world_state), и вызывает
    // РЕАЛЬНОГО провайдера. actionPath передаётся для выбора allowedTools/MCP-конфига (см.
    // ClaudeCliAiProvider.PrepareWorkDir) — значение имеет только имя последнего сегмента (=action).
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

    // Как CallRealAiAsync, но дополнительно поднимает фоновый "автоответчик" на roll_dice/ask_player
    // (те же файлы roll_request.json/ask_player_request.json, что в реальной игре поллит
    // GameAiClient.SpinAsync — здесь без UI, отвечаем сразу же плайсхолдером). Нужно для сценариев,
    // где промпт ОБЯЗАН вызвать один из этих MCP-инструментов (атака → бросок, неоднозначная цель →
    // вопрос) — без автоответчика вызов провис бы на реальный таймаут MCP-инструмента (50с).
    public static async Task<AutoAnswerResult> CallRealAiWithAutoAnswersAsync(
        string ruleSet, string action, Storage storage, string userMessage,
        int rollValue = 15, string? askPlayerAnswer = null, string? model = null)
    {
        if (DraftActions.Contains(action)) storage.SaveDraft(); else storage.Save();

        string storageDir = Path.GetDirectoryName(Storage.SavePath)!;
        string rollPath = Path.Combine(storageDir, "roll_request.json");
        string askPath = Path.Combine(storageDir, "ask_player_request.json");
        string selectPath = Path.Combine(storageDir, "select_target_request.json");
        // На случай мусора от предыдущего упавшего прогона — своя, не боевая, папка, но лучше не гадать.
        TryDelete(rollPath);
        TryDelete(askPath);
        TryDelete(selectPath);

        var tracker = new AutoAnswerResult("", false, false);
        var trackerBox = new AutoAnswerBox(tracker);

        using var cts = new CancellationTokenSource();
        var pollTask = AutoAnswerLoopAsync(rollPath, askPath, selectPath, storage, rollValue, askPlayerAnswer, trackerBox, cts.Token);
        try
        {
            string response = await CallRealAiAsync(ruleSet, action, storage, userMessage, model);
            return trackerBox.Value with { RawResponse = response };
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
        int rollValue, string? askPlayerAnswer, AutoAnswerBox box, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!box.Value.RollDiceTriggered && File.Exists(rollPath))
                {
                    try
                    {
                        string json = await File.ReadAllTextAsync(rollPath, ct);
                        var req = JsonSerializer.Deserialize<RollRequest>(json, JsonOptions);
                        if (req != null && !req.Answered)
                        {
                            req.Answered = true;
                            req.Roll1 = rollValue;
                            req.Answer = rollValue;
                            req.Critical = rollValue == 20 ? "critical_success" : rollValue == 1 ? "critical_failure" : null;
                            await File.WriteAllTextAsync(rollPath, JsonSerializer.Serialize(req, JsonOptions), ct);
                            box.Value = box.Value with { RollDiceTriggered = true };
                        }
                    }
                    catch { /* файл мог быть в процессе записи MCP-сервером — попробуем на следующем тике */ }
                }

                if (!box.Value.AskPlayerTriggered && File.Exists(askPath))
                {
                    try
                    {
                        string json = await File.ReadAllTextAsync(askPath, ct);
                        var req = JsonSerializer.Deserialize<AskPlayerRequest>(json, JsonOptions);
                        if (req != null && !req.Answered)
                        {
                            req.Answered = true;
                            req.Answer = askPlayerAnswer ?? (req.Options is { Length: > 0 } ? req.Options[0] : "Продолжаю");
                            await File.WriteAllTextAsync(askPath, JsonSerializer.Serialize(req, JsonOptions), ct);
                            box.Value = box.Value with { AskPlayerTriggered = true };
                        }
                    }
                    catch { }
                }

                // select_target: выбираем первую допустимую клетку, не занятую героем (без UI — видимым считаем всё).
                if (!box.Value.SelectTargetTriggered && File.Exists(selectPath))
                {
                    try
                    {
                        string json = await File.ReadAllTextAsync(selectPath, ct);
                        var req = JsonSerializer.Deserialize<TargetSelectionRequest>(json, JsonOptions);
                        if (req != null && !req.Answered)
                        {
                            var ws = storage.WorldState;
                            var heroPos = ws.Hero?.Position;
                            var cell = TargetGeometry.SelectableCells(ws, req, (_, _) => true, (_, _) => true)
                                .Where(c => heroPos is not { Count: >= 2 } || c != (heroPos[0], heroPos[1]))
                                .OrderBy(c => c.col).ThenBy(c => c.row)
                                .Cast<(int col, int row)?>().FirstOrDefault();
                            req.Answered = true;
                            req.Result = cell is { } p
                                ? new TargetSelectionResult { Targets = [TargetGeometry.Describe(ws, p.col, p.row)] }
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

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // Структурная проверка + разбор для дальнейших ассертов: применяет ответ к ТОМУ ЖЕ Storage
    // сценария (как это сделал бы реальный движок) — и корневой патч, и вложенные history[].patch
    // (см. Prompts/.../systemPrompt.md § БОЙ). Бросает исключение, если ИИ вернул невалидный/
    // неприменимый diff. Возвращает тексты history-записей — по ним удобно проверять, что механика
    // реально попала в "[...]", как того требует промпт.
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
                    // Как SequentialHistoryPlayer.AnimateAndApplyPatch в реальном движке: movePath —
                    // служебное поле только для анимации, "position" (последний элемент) уже пришёл
                    // отдельно — снимаем его перед ApplyUpdateWorldState, иначе Storage.ValidateKeys
                    // залогирует "Unknown field 'movePath'" (не ошибка, но лишний шум, которого в
                    // реальном движке при этой же схеме не бывает).
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

    // Копия SequentialHistoryPlayer.ParseMergingDuplicateRootKeys (private там, реальный движок
    // мирится с дублями корневых ключей вроде двух "hero": вместо одного объединённого — см. её
    // комментарий) — тест должен так же не падать на этой опечатке модели, а не отвергать ответ,
    // который в реальной игре применился бы нормально.
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

    // Копия GameAiClient.ExtractJson (private там) — вырезает первый сбалансированный {...} из
    // текста ответа модели (на случай обрамляющего текста/markdown, которые ClaudeCliAiProvider
    // обычно уже снимает сам, но лишняя защита здесь не помешает).
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

// Тексты всех history-записей ответа (для проверки нарратива/механики) + сырой корневой JsonObject
// (для точечных точечных проверок нестандартных полей, если понадобится).
public record PromptResult(string RawResponse, List<string> HistoryTexts, JsonObject Root)
{
    public bool AnyTextContains(string substring) =>
        HistoryTexts.Any(t => t.Contains(substring, StringComparison.OrdinalIgnoreCase));
}

public record AutoAnswerResult(string RawResponse, bool RollDiceTriggered, bool AskPlayerTriggered, bool SelectTargetTriggered = false);
