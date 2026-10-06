using System.Diagnostics;
using NaviDnD.Helpers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace NaviDnD.Clients;

public class ClaudeCliAiProvider(AppConfig config, AiLogger? logger = null) : IAiProvider
{
    private static readonly JsonSerializerOptions _unicodeJson = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private static readonly JsonSerializerOptions _camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string _settingsJson = JsonSerializer.Serialize(new
    {
        permissions = new
        {
            deny = new[]
            {
                "Bash", "Edit", "Glob", "Grep", "LS",
                "MultiEdit", "NotebookEdit", "NotebookRead",
                "Read", "TodoRead", "TodoWrite",
                "WebFetch", "WebSearch", "Write", "Agent"
            }
        }
    });

    // Сервер перегружен/недоступен (529, 503, overloaded) — временно: запрос повторяется (до 3 попыток, пауза растёт),
    // иначе ход мастера терялся при первой же перегрузке.
    private static bool IsTransient(Exception ex) =>
        ex.Message.Contains("529") || ex.Message.Contains("503") || ex.Message.Contains("verloaded", StringComparison.OrdinalIgnoreCase);

    private async Task<string> WithRetry(Func<Task<string>> call)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return await call(); }
            catch (Exception ex) when (attempt < 3 && IsTransient(ex))
            {
                logger?.LogNote($"Сервер перегружен (попытка {attempt}) — повтор через {attempt * 5} с");
                await Task.Delay(attempt * 5000);
            }
        }
    }

    public Task<string> Complete(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model) =>
        WithRetry(() => CompleteOnce(systemBlocks, userMessage, actionPath, model));

    public Task<string> CompleteWithStreaming(
        IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model,
        Action<string>? onChunk) =>
        WithRetry(() => CompleteWithStreamingOnce(systemBlocks, userMessage, actionPath, model, onChunk));

    // Claude CLI не запустился (нет по пути из настроек) — ошибка настройки, а не сбой ответа.
    private Process StartCli(ProcessStartInfo psi)
    {
        try { return Process.Start(psi) ?? throw new AiSetupException("Не удалось запустить Claude CLI."); }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new AiSetupException($"Claude CLI не найден ({config.ClaudeCliPath}): установи его, войди (claude login) " +
                                       "и при необходимости укажи путь в настройках («ПУТЬ К CLAUDE»).");
        }
    }

    private async Task<string> CompleteOnce(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model)
    {
        string stdinContent = BuildStdinContent(systemBlocks, userMessage);
        string workDir = PrepareWorkDir(actionPath);
        try
        {
            var psi = BuildPsi(workDir, model, actionPath, systemBlocks[0], "json");

            string label = Path.GetFileName(actionPath);
            logger?.LogRequest(label, systemBlocks, stdinContent);

            var sw = Stopwatch.StartNew();
            using var process = StartCli(psi);
            ChildProcessJob.Add(process);   // игру закрыли — запрос завершается вместе с ней
            await process.StandardInput.WriteAsync(stdinContent);
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask  = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string output = await outputTask;
            string stderr = await errorTask;

            if (!string.IsNullOrWhiteSpace(stderr))
                logger?.LogNote($"STDERR: {stderr.Trim()}");

            try
            {
                var doc = JsonDocument.Parse(output);
                bool isError = doc.RootElement.TryGetProperty("is_error", out var errProp) && errProp.GetBoolean();
                string resultText = doc.RootElement.TryGetProperty("result", out var result)
                    ? result.GetString() ?? output
                    : output;

                if (isError)
                {
                    logger?.LogResponse(output, $"ERROR: {resultText}", sw.Elapsed);
                    throw new Exception(BuildErrorMessage($"Claude CLI: {resultText}"));
                }

                string stripped = StripMarkdownFences(resultText);
                logger?.LogResponse(output, stripped, sw.Elapsed);
                return stripped;
            }
            catch (JsonException)
            {
                logger?.LogResponse(output, output, sw.Elapsed);
                return output;
            }
        }
        finally
        {
            CleanupWorkDir(workDir);
        }
    }

    private async Task<string> CompleteWithStreamingOnce(
        IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model,
        Action<string>? onChunk)
    {
        string stdinContent = BuildStdinContent(systemBlocks, userMessage);
        string workDir = PrepareWorkDir(actionPath);
        try
        {
            var psi = BuildPsi(workDir, model, actionPath, systemBlocks[0], "stream-json");

            string label = Path.GetFileName(actionPath);
            logger?.LogRequest(label, systemBlocks, stdinContent);

            var sw = Stopwatch.StartNew();
            using var process = StartCli(psi);
            ChildProcessJob.Add(process);   // игру закрыли — запрос завершается вместе с ней
            await process.StandardInput.WriteAsync(stdinContent);
            process.StandardInput.Close();

            // Read stderr concurrently to prevent buffer-full deadlock.
            var stderrTask = process.StandardError.ReadToEndAsync();

            string? fullResult = null;
            string rawResultJson = "stream-json";
            var streamedSoFar = new StringBuilder();

            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var (chunk, result, error) = ParseStreamEvent(line, streamedSoFar);

                if (error != null)
                    throw new Exception(BuildErrorMessage($"Claude CLI: {error}"));

                if (result != null)
                {
                    fullResult = result;
                    rawResultJson = line;
                }

                if (chunk != null && onChunk != null)
                    onChunk(chunk);
            }

            await process.WaitForExitAsync();
            string stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stderr))
                logger?.LogNote($"STDERR: {stderr.Trim()}");

            if (process.ExitCode != 0 && fullResult == null)
            {
                string exitMessage = $"Claude CLI завершился с ошибкой (код {process.ExitCode}). Попробуй ещё раз.";
                if (!string.IsNullOrWhiteSpace(stderr)) exitMessage += $" stderr: {stderr.Trim()}";
                throw new Exception(BuildErrorMessage(exitMessage));
            }

            // Prefer the authoritative result-event text; fall back to accumulated streaming text.
            string response = StripMarkdownFences(fullResult ?? streamedSoFar.ToString());
            logger?.LogResponse(rawResultJson, response, sw.Elapsed);
            return response;
        }
        finally
        {
            CleanupWorkDir(workDir);
        }
    }

    // Returns (streamChunk for display, fullResult from result-event, errorMessage).
    private static (string? chunk, string? fullResult, string? error) ParseStreamEvent(
        string line, StringBuilder streamedSoFar)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp)) return (null, null, null);
            string eventType = typeProp.GetString() ?? "";

            if (eventType == "assistant" && root.TryGetProperty("message", out var msg))
            {
                if (!msg.TryGetProperty("content", out var content)) return (null, null, null);

                var sb = new StringBuilder();
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                        && block.TryGetProperty("text", out var textProp))
                        sb.Append(textProp.GetString());
                }

                string newText = sb.ToString();
                if (string.IsNullOrEmpty(newText)) return (null, null, null);

                string accumulated = streamedSoFar.ToString();
                if (newText.Length > accumulated.Length)
                {
                    // Accumulated format: each event contains full text so far — emit only delta.
                    string delta = newText[accumulated.Length..];
                    streamedSoFar.Clear();
                    streamedSoFar.Append(newText);
                    return (delta, null, null);
                }
                else
                {
                    // Delta format: each event contains only new text.
                    streamedSoFar.Append(newText);
                    return (newText, null, null);
                }
            }

            if (eventType == "result")
            {
                bool isError = root.TryGetProperty("is_error", out var ep) && ep.GetBoolean();
                string? resultStr = root.TryGetProperty("result", out var rp) ? rp.GetString() : null;

                if (isError) return (null, null, resultStr ?? "Unknown error");
                if (resultStr == null) return (null, null, null);

                // Emit any text that wasn't streamed yet (handles edge case where streaming missed the end).
                string accumulated = streamedSoFar.ToString();
                string? remaining = resultStr.Length > accumulated.Length
                    ? resultStr[accumulated.Length..]
                    : null;

                return (remaining, resultStr, null);
            }

            return (null, null, null);
        }
        catch (JsonException) { return (null, null, null); }
    }

    private static string BuildStdinContent(IReadOnlyList<string> systemBlocks, string userMessage) =>
        systemBlocks.Count > 1
            ? string.Join("\n\n", systemBlocks.Skip(1)) + "\n\n" + userMessage
            : userMessage;

    private ProcessStartInfo BuildPsi(string workDir, string model, string actionPath, string systemPrompt, string outputFormat)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = config.ClaudeCliPath,
            WorkingDirectory       = workDir,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            StandardInputEncoding  = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrWhiteSpace(config.ClaudeOAuthToken))
            psi.Environment["CLAUDE_CODE_OAUTH_TOKEN"] = config.ClaudeOAuthToken;

        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(model);

        // Windows ограничивает длину командной строки процесса 32 767 символами — большой
        // системный промпт (SendAction уже ~25К) через --system-prompt рано или поздно упрётся в
        // это. --system-prompt-file (недокументированный, но реальный флаг — см. описание --bare:
        // "--system-prompt[-file]") читает промпт из файла, так что в команду попадает только путь.
        // workDir создаётся заново на каждый вызов и удаляется в finally после завершения процесса
        // (см. Complete/CompleteWithStreaming), так что файл живёт ровно пока нужен.
        string systemPromptPath = Path.Combine(workDir, "system-prompt.txt");
        File.WriteAllText(systemPromptPath, systemPrompt, new UTF8Encoding(false));
        psi.ArgumentList.Add("--system-prompt-file");
        psi.ArgumentList.Add(systemPromptPath);

        psi.ArgumentList.Add("--tools");
        psi.ArgumentList.Add("");

        // Инструменты действия — общий список (AiActionTools), у Claude CLI — с префиксом сервера.
        string[] allowedTools = [.. AiActionTools.ToolsFor(Path.GetFileName(actionPath)).Select(t => "mcp__navidnd__" + t)];

        if (allowedTools is { Length: > 0 })
        {
            psi.ArgumentList.Add("--mcp-config");
            psi.ArgumentList.Add(Path.Combine(workDir, "mcp.json"));
            psi.ArgumentList.Add("--allowedTools");
            psi.ArgumentList.Add(string.Join(",", allowedTools));
        }

        psi.ArgumentList.Add("--strict-mcp-config");
        psi.ArgumentList.Add("--setting-sources");
        psi.ArgumentList.Add("");
        psi.ArgumentList.Add("--no-session-persistence");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add(outputFormat);
        if (outputFormat == "stream-json")
            psi.ArgumentList.Add("--verbose");
        psi.ArgumentList.Add("-p");

        return psi;
    }

    private string PrepareWorkDir(string actionPath)
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"navidnd-{Guid.NewGuid():N}");
        string claudeDir = Path.Combine(workDir, ".claude");
        Directory.CreateDirectory(claudeDir);

        File.WriteAllText(Path.Combine(claudeDir, "settings.json"), _settingsJson, Encoding.UTF8);

        var mcpArgs = AiActionTools.McpArgs(Path.GetFileName(actionPath), logger, withToolFilter: false);

        var mcpConfig = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["navidnd"] = new
                {
                    command = config.McpServerPath,
                    args = mcpArgs
                }
            }
        };
        File.WriteAllText(Path.Combine(workDir, "mcp.json"), JsonSerializer.Serialize(mcpConfig, _camelCase), Encoding.UTF8);

        return workDir;
    }

    // claude.exe запускает MCP-сервер (NaviDnD.McpServer.exe) как дочерний процесс с CWD=workDir
    // (наследуется от родителя — см. BuildPsi WorkingDirectory). При успешном завершении claude.exe
    // сам останавливает MCP-сервер до выхода, поэтому Directory.Delete обычно проходит. Но если
    // claude.exe падает рано (например ошибка авторизации — "does not have access to Claude") ДО
    // штатного завершения, MCP-сервер может остаться висеть с CWD внутри workDir — Windows не даёт
    // удалить директорию, пока она чья-то текущая рабочая, и Directory.Delete бросает IOException
    // "used by another process". Эта ошибка вылетала из finally ПОВЕРХ уже летящего исключения с
    // настоящей причиной (ошибка авторизации) — C# при исключении внутри finally теряет
    // оригинальное, наружу уходило только "файл используется другим процессом", а понятный текст
    // про логин пропадал. Поэтому очистка — best-effort и не должна перекрывать реальную ошибку.
    private void CleanupWorkDir(string workDir)
    {
        try
        {
            Directory.Delete(workDir, recursive: true);
        }
        catch (Exception ex)
        {
            logger?.LogNote($"Не удалось удалить временную папку {workDir}: {ex.Message}");
        }
    }

    // Claude CLI отвечает этим текстом, когда CLAUDE_CODE_OAUTH_TOKEN просрочен/недействителен или
    // организация потеряла доступ (например сменили аккаунт, под которым сгенерирован токен) —
    // добавляем понятную подсказку прямо в сообщение, которое увидит игрок в истории диалога
    // (GameAiClient.RecordError пишет ex.Message как есть).
    private static string BuildErrorMessage(string rawMessage)
    {
        if (rawMessage.Contains("does not have access to Claude", StringComparison.OrdinalIgnoreCase)
            || rawMessage.Contains("login again", StringComparison.OrdinalIgnoreCase)
            || rawMessage.Contains("please run /login", StringComparison.OrdinalIgnoreCase))
        {
            return $"{rawMessage} — похоже, недействителен CLAUDE_CODE_OAUTH_TOKEN (AppConfig.ClaudeOAuthToken). " +
                   "Выполните claude login под нужным аккаунтом или claude setup-token и обновите токен в настройках игры.";
        }
        return rawMessage;
    }

    private static string StripMarkdownFences(string text)
    {
        var s = text.Trim();
        if (s.StartsWith("```"))
        {
            int newline = s.IndexOf('\n');
            if (newline >= 0) s = s[(newline + 1)..];
        }
        if (s.EndsWith("```"))
            s = s[..^3];
        s = s.Trim();

        try
        {
            using var doc = JsonDocument.Parse(s);
            return JsonSerializer.Serialize(doc.RootElement, _unicodeJson);
        }
        catch { return s; }
    }
}
