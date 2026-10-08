using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NaviDnD.Helpers;

namespace NaviDnD.Clients;

// Нейронка через OpenAI Codex CLI (`codex exec`) — вместо Claude CLI (настройка «НЕЙРОНКА»). Тот же промпт и тот же
// MCP-сервер игры: кубики, вопросы игроку, выбор цели, plan_location и т.д. работают так же.
//  - правила мастера заменяют инструкции программиста через model_instructions_file; контекст — в stdin;
//  - MCP-сервер — переопределениями конфигурации `-c mcp_servers.navidnd.*` (свой config.toml пользователя не
//    трогаем); набор инструментов действия сервер отдаёт сам (`--tools`, AiActionTools), раз у Codex нет
//    аналога --allowedTools;
//  - песочница read-only: агент Codex не меняет файлы и не запускает изменяющие команды;
//  - ответ — последнее сообщение агента (`-o файл`), а события `--json` — для ошибок и лога.
// Модель — CodexModel из настроек (пусто — модель Codex по умолчанию); модели Claude из запроса не применяются.
public class CodexCliAiProvider(AppConfig config, AiLogger? logger = null) : IAiProvider
{
    // Codex — агент для кода; здесь он мастер игры: только ответ по формату промпта и MCP-инструменты игры.
    private const string Preface =
        "Ты работаешь внутри игры как её мастер, а не как помощник по коду: не читай и не меняй файлы, не запускай " +
        "команды оболочки. Из инструментов используй только MCP-сервер navidnd. Ответ — строго в формате из " +
        "инструкций ниже, без пояснений вокруг.\n\n";

    public Task<string> Complete(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model) =>
        WithRetry(() => CompleteOnce(systemBlocks, userMessage, actionPath), AiActionTools.ToolsFor(Path.GetFileName(actionPath)).Length == 0);

    // Игра читает ответ целиком (SequentialHistoryPlayer), поток Codex отдаёт сообщения агента только готовыми —
    // поэтому «поток» — весь ответ одним куском.
    public async Task<string> CompleteWithStreaming(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath,
        string model, Action<string>? onChunk)
    {
        string response = await Complete(systemBlocks, userMessage, actionPath, model);
        onChunk?.Invoke(response);
        return response;
    }

    // Перегрузка/сбой сети на стороне OpenAI — повтор только для запросов без игровых инструментов.
    private static bool IsTransient(Exception ex) =>
        ex.Message.Contains("429") || ex.Message.Contains("503") || ex.Message.Contains("overloaded", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("stream disconnected", StringComparison.OrdinalIgnoreCase);

    private async Task<string> WithRetry(Func<Task<string>> call, bool canRetry)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return await call(); }
            // MCP мог уже изменить мир или получить бросок: повтор всего хода повторит побочный эффект.
            catch (Exception ex) when (ex is not AiSetupException && canRetry && attempt < 3 && IsTransient(ex))
            {
                logger?.LogNote($"Codex: временная ошибка (попытка {attempt}) — повтор через {attempt * 5} с");
                await Task.Delay(attempt * 5000);
            }
        }
    }

    private async Task<string> CompleteOnce(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath)
    {
        string action = Path.GetFileName(actionPath);
        string workDir = Path.Combine(Path.GetTempPath(), $"navidnd-codex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        string lastMessagePath = Path.Combine(workDir, "last-message.txt");
        string instructionsPath = Path.Combine(workDir, "instructions.md");
        try
        {
            string userPart = systemBlocks.Count > 1 ? string.Join("\n\n", systemBlocks.Skip(1)) + "\n\n" + userMessage : userMessage;
            if (systemBlocks.Count == 0) throw new ArgumentException("Не задан системный промпт мастера.", nameof(systemBlocks));
            await File.WriteAllTextAsync(instructionsPath, Preface + systemBlocks[0], new UTF8Encoding(false));
            logger?.LogRequest(action, systemBlocks, userPart);
            logger?.LogNote($"Codex: model={ModelFor(action)}, reasoning={ReasoningFor(action)}");

            var psi = BuildPsi(workDir, lastMessagePath, instructionsPath, action);
            var sw = Stopwatch.StartNew();
            Process? started;
            try { started = Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
            {
                throw new AiSetupException($"Codex CLI не найден ({config.CodexCliPath}): установи его (npm i -g @openai/codex), войди (codex login) " +
                                    "и при необходимости укажи путь в настройках («ПУТЬ К CODEX»).");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                throw new AiSetupException("Нет доступа к запуску Codex CLI.\nПроверь разрешения, блокировку антивирусом и «ПУТЬ К CODEX» в настройках.");
            }
            using var process = started ?? throw new Exception("Не удалось запустить Codex CLI.");
            ChildProcessJob.Add(process);   // игру закрыли — запрос завершается вместе с ней
            await process.StandardInput.WriteAsync(userPart);
            process.StandardInput.Close();

            var stderrTask = process.StandardError.ReadToEndAsync();
            var events = new StringBuilder();
            string? lastAgentText = null, error = null;
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                events.AppendLine(line);
                var (text, err) = ParseEvent(line);
                if (text != null) lastAgentText = text;
                if (err != null) error = err;
            }
            await process.WaitForExitAsync();
            string stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stderr)) logger?.LogNote($"STDERR: {stderr.Trim()}");

            string? final = File.Exists(lastMessagePath) ? File.ReadAllText(lastMessagePath, Encoding.UTF8) : null;
            if (string.IsNullOrWhiteSpace(final) && process.ExitCode == 0 && error == null) final = lastAgentText;
            if (process.ExitCode != 0 || error != null || string.IsNullOrWhiteSpace(final))
            {
                string reason = error ?? (process.ExitCode != 0 ? $"код {process.ExitCode}" : "пустой ответ");
                if (!string.IsNullOrWhiteSpace(stderr)) reason += $"; stderr: {stderr.Trim()}";
                logger?.LogResponse(events.ToString(), $"ERROR: {reason}", sw.Elapsed);
                throw CreateError($"Codex CLI: {reason}");
            }
            string response = StripMarkdownFences(final);
            logger?.LogResponse(events.ToString(), response, sw.Elapsed);
            return response;
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); }
            catch (Exception ex) { logger?.LogNote($"Не удалось удалить временную папку {workDir}: {ex.Message}"); }
        }
    }

    public static Exception CreateError(string rawMessage)
    {
        string[] authMarkers = ["not logged in", "not authenticated", "unauthorized", "authentication_error",
            "authentication required", "authentication token", "failed to authenticate", "please log in",
            "please login", "codex login", "token expired", "token has expired", "refresh_token_reused",
            "invalid_api_key", "incorrect api key", "401"];
        if (authMarkers.Any(marker => rawMessage.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return new AiSetupException("Codex не авторизован или вход истёк.\nВыполни codex login в терминале и повтори запрос.");
        string[] accessMarkers = ["403", "model_not_found", "does not have access", "do not have access",
            "insufficient_quota", "usage limit", "usage_limit"];
        if (accessMarkers.Any(marker => rawMessage.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return new AiSetupException("Codex: нет доступа к модели или исчерпан лимит.\nПроверь аккаунт, подписку, лимиты и модель в настройках.");
        return new Exception(rawMessage);
    }

    private ProcessStartInfo BuildPsi(string workDir, string lastMessagePath, string instructionsPath, string action)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveCodex(config.CodexCliPath),
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string a in new[] { "exec", "--json", "--ephemeral", "--ignore-user-config", "--skip-git-repo-check", "--sandbox", "read-only", "--cd", workDir,
                     "--output-last-message", lastMessagePath })
            psi.ArgumentList.Add(a);
        string selectedModel = ModelFor(action);
        if (!string.IsNullOrWhiteSpace(selectedModel)) { psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(selectedModel); }
        Config(psi, "approval_policy", "'never'");
        Config(psi, "model_instructions_file", TomlLiteral(instructionsPath));
        Config(psi, "model_reasoning_effort", TomlLiteral(ReasoningFor(action)));
        Config(psi, "project_doc_max_bytes", "0");
        Config(psi, "web_search", "'disabled'");
        Config(psi, "features.shell_tool", "false");

        // MCP-сервер игры — только если действию нужны инструменты; набор отдаёт сам сервер (--tools).
        if (AiActionTools.ToolsFor(action).Length > 0)
        {
            var args = AiActionTools.McpArgs(action, logger, withToolFilter: true);
            Config(psi, "mcp_servers.navidnd.command", TomlLiteral(config.McpServerPath));
            Config(psi, "mcp_servers.navidnd.args", "[" + string.Join(",", args.Select(TomlLiteral)) + "]");
            Config(psi, "mcp_servers.navidnd.required", "true");
            // Игрок уже разрешил мастеру пользоваться инструментами игры; CLI работает без диалога approvals.
            foreach (string tool in AiActionTools.ToolsFor(action))
                Config(psi, $"mcp_servers.navidnd.tools.{tool}.approval_mode", "'approve'");
            Config(psi, "mcp_servers.navidnd.startup_timeout_sec", "30");
            // ask_player/roll_dice ждут игрока (до 50 с), plan_location строит локацию — с запасом.
            Config(psi, "mcp_servers.navidnd.tool_timeout_sec", "180");
        }
        psi.ArgumentList.Add("-");   // промпт — из stdin
        return psi;
    }

    private string ReasoningFor(string action) => action is "DescribeHero" or "RepairJson" ? "low"
        : config.CodexReasoningEffort is "low" or "medium" or "high" ? config.CodexReasoningEffort : "medium";

    private string ModelFor(string action)
    {
        string selected = action switch
        {
            "CreateNewGame" or "DescribeHero" => config.CodexCreateNewGameModel,
            "CreateWorld" or "StartNewGame" => config.CodexStartNewGameModel,
            _ => config.CodexModel,
        };
        return string.IsNullOrWhiteSpace(selected) ? config.CodexModel : selected;
    }

    // Use the same bounded discovery as startup, including repair of stale absolute paths.
    private static string ResolveCodex(string path) =>
        AiCliDiscovery.Resolve("codex", path) ?? AiCliDiscovery.NormalizePath(path);

    private static void Config(ProcessStartInfo psi, string key, string tomlValue)
    {
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"{key}={tomlValue}");
    }

    // JSON-экранирование строк совместимо с базовыми строками TOML: сохраняет и пути, и апострофы.
    private static string TomlLiteral(string s) => JsonSerializer.Serialize(s);

    // Событие `codex exec --json` (строка JSON): текст сообщения агента и/или ошибка. Поддержаны оба известных
    // формата: новый ({"type":"item.completed","item":{"type":"agent_message","text":…}}, "turn.failed", "error")
    // и старый ({"msg":{"type":"agent_message","message":…}} / {"msg":{"type":"error",…}}).
    private static (string? text, string? error) ParseEvent(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            string type = Str(root, "type") ?? "";
            if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object
                && Str(item, "type") is "agent_message" or "assistant_message")
                return (Str(item, "text"), null);
            if (type == "turn.failed" && root.TryGetProperty("error", out var te))
                return (null, te.ValueKind == JsonValueKind.Object ? Str(te, "message") ?? te.ToString() : te.ToString());
            if (type == "error") return (null, Str(root, "message") ?? line);
            if (root.TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.Object)
            {
                string mtype = Str(msg, "type") ?? "";
                if (mtype == "agent_message") return (Str(msg, "message"), null);
                if (mtype is "error" or "stream_error") return (null, Str(msg, "message") ?? msg.ToString());
            }
        }
        catch (JsonException) { /* не JSON — строка лога */ }
        return (null, null);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    // Ответ в ```json … ``` — без ограждения (как у Claude CLI).
    private static string StripMarkdownFences(string text)
    {
        var s = text.Trim();
        if (s.StartsWith("```"))
        {
            int newline = s.IndexOf('\n');
            if (newline >= 0) s = s[(newline + 1)..];
        }
        if (s.EndsWith("```")) s = s[..^3];
        return s.Trim();
    }
}
