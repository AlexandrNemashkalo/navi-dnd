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

    // Server is overloaded/unavailable (529, 503, overloaded) - temporary: the request is repeated (up to 3
    // attempts, the pause increases), otherwise the GM’s progress was lost at the first overload.
    private static bool IsTransient(Exception ex) =>
        ex.Message.Contains("529") || ex.Message.Contains("503") || ex.Message.Contains("verloaded", StringComparison.OrdinalIgnoreCase);

    private async Task<string> WithRetry(Func<Task<string>> call)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return await call(); }
            catch (Exception ex) when (attempt < 3 && IsTransient(ex))
            {
                logger?.LogNote($"Server overloaded (attempt {attempt}); retrying in {attempt * 5} s");
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

    // Claude CLI did not start (not on the path from the settings) - a configuration error, not a response failure.
    private Process StartCli(ProcessStartInfo psi)
    {
        try { return Process.Start(psi) ?? throw new AiSetupException(L.T("Could not start Claude CLI.")); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            throw new AiSetupException(L.T("Claude not found.\nInstall Claude Code or set \"CLAUDE PATH\" in the settings."));
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
            ChildProcessJob.Add(process);   // the game was closed - the request ends along with it
            await process.StandardInput.WriteAsync(stdinContent);
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask  = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string output = await outputTask;
            string stderr = await errorTask;

            if (!string.IsNullOrWhiteSpace(stderr))
                logger?.LogNote($"STDERR: {stderr.Trim()}");

            if (process.ExitCode != 0)
                throw CreateError($"Claude CLI (exit code {process.ExitCode}): {output}\n{stderr}");

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
                    throw CreateError($"Claude CLI: {resultText}");
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
            ChildProcessJob.Add(process);   // the game was closed - the request ends along with it
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
                    throw CreateError($"Claude CLI: {error}");

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
                string exitMessage = L.F("Claude CLI exited with an error (code {0}). Try again.", process.ExitCode);
                if (!string.IsNullOrWhiteSpace(stderr)) exitMessage += $" stderr: {stderr.Trim()}";
                throw CreateError(exitMessage);
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
            FileName               = OperatingSystem.IsWindows() ? config.ClaudeCliPath
                : AiCliDiscovery.Resolve("claude", config.ClaudeCliPath) ?? AiCliDiscovery.NormalizePath(config.ClaudeCliPath),
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
        // macOS/Linux: the launcher from npm needs `node`, but it may not be in the PATH game (there is no Homebrew/nvm). in the bare terminal
        if (!OperatingSystem.IsWindows()) psi.Environment["PATH"] = AiCliDiscovery.ChildPath(psi.FileName);

        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(model);

        // Windows limits the length of the process command line to 32,767 characters - a large system prompt
        // (SendAction is already ~25K) via --system-prompt will sooner or later run into this.
        // --system-prompt-file (undocumented but real flag - see description --bare: "--system-prompt[-file]")
        // reads the prompt from a file, so that only the path is included in the command. workDir is created
        // anew for each call and deleted in finally after the process is completed (see
        // Complete/CompleteWithStreaming)), so the file lives exactly as long as it is needed.
        string systemPromptPath = Path.Combine(workDir, "system-prompt.txt");
        File.WriteAllText(systemPromptPath, systemPrompt, new UTF8Encoding(false));
        psi.ArgumentList.Add("--system-prompt-file");
        psi.ArgumentList.Add(systemPromptPath);

        psi.ArgumentList.Add("--tools");
        psi.ArgumentList.Add("");

        // Action tools - general list (AiActionTools), for Claude CLI - with a server prefix.
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

        var mcpEnvironment = AiActionTools.McpEnvironment();
        var mcpConfig = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["navidnd"] = mcpEnvironment.Count == 0
                    ? (object)new { command = config.McpServerPath, args = mcpArgs }
                    : new { command = config.McpServerPath, args = mcpArgs, env = mcpEnvironment }
            }
        };
        File.WriteAllText(Path.Combine(workDir, "mcp.json"), JsonSerializer.Serialize(mcpConfig, _camelCase), Encoding.UTF8);

        return workDir;
    }

    // claude.exe starts the MCP-server (NaviDnD.McpServer.exe) as a child process of CWD=workDir (inherited
    // from the parent - see BuildPsi WorkingDirectory). On success, claude.exe itself stops the MCP-server
    // before exiting, so Directory.Delete usually passes. But if claude.exe crashes early (for example,
    // authorization error - "does not have access to Claude") BEFORE normal completion, the MCP- server may
    // remain hanging with CWD inside workDir - Windows does not allow you to delete the directory until it
    // someone's current working one, and IOException throws IOException "used by another process" This error
    // was thrown from finally on OVER the already flying exception with the real reason (authorization error) -
    // C# when exception is inside finally loses the original one, outside All that was left was “the file is
    // being used by another process”, and the clear text about the login disappeared. Therefore, cleaning is
    // best-effort and should not cover the real error.
    private void CleanupWorkDir(string workDir)
    {
        try
        {
            Directory.Delete(workDir, recursive: true);
        }
        catch (Exception ex)
        {
            logger?.LogNote($"Could not remove temporary directory {workDir}: {ex.Message}");
        }
    }

    public static Exception CreateError(string rawMessage)
    {
        string[] authMarkers = ["not logged in", "login again", "please run /login",
            "authentication_error", "invalid authentication", "invalid oauth token",
            "oauth token has expired", "token expired", "unauthorized", "failed to authenticate"];
        if (authMarkers.Any(marker => rawMessage.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return new AiSetupException(L.T("Claude is not signed in or the session expired.\nRun claude auth login; if a token is set in the game settings, update or remove it."));
        if (rawMessage.Contains("does not have access to Claude", StringComparison.OrdinalIgnoreCase))
            return new AiSetupException(L.T("The account has no access to Claude.\nCheck your account, subscription and the token in the settings."));
        return new Exception(rawMessage);
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
