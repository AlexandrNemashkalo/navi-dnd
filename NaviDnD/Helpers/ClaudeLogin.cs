using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NaviDnD.Helpers;

// Login to Claude from the settings: `claude auth login` opens the login in the browser and saves it in the
// Claude Code itself (Windows - .claude/.credentials.json, macOS - Keychain), `claude auth status` - check
// (JSON). The game does not need a token: the login updates itself. Everything is in background: the settings
// screen shows the status and is redrawn by Status.
internal static class ClaudeLogin
{
    private static readonly object Gate = new();
    private static Process? _login;
    private static string _status = "";
    private static int _checking;

    internal static string Status { get { lock (Gate) return _status; } }

    // Settings item: logging in - cancel, otherwise - logging in.
    internal static void Toggle(AppConfig config)
    {
        lock (Gate)
        {
            if (_login is { HasExited: false } running)
            {
                try { running.Kill(entireProcessTree: true); } catch { }
                _login = null;
                _status = L.T("sign-in cancelled");
                return;
            }
        }
        Start(config);
    }

    // Login verification (opening settings and after login).
    internal static void Check(AppConfig config)
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        lock (Gate) if (_login == null) _status = L.T("checking…");
        _ = Task.Run(async () =>
        {
            try
            {
                string text = await RunAsync(config, ["auth", "status"], TimeSpan.FromSeconds(30));
                using var json = JsonDocument.Parse(text[text.IndexOf('{')..]);
                var root = json.RootElement;
                bool loggedIn = root.TryGetProperty("loggedIn", out var value) && value.GetBoolean();
                string email = root.TryGetProperty("email", out var mail) ? mail.GetString() ?? "" : "";
                lock (Gate) if (_login == null)
                    _status = loggedIn ? (email.Length > 0 ? L.F("signed in: {0}", email) : L.T("signed in")) : L.T("not signed in — Enter");
            }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidOperationException
                or System.ComponentModel.Win32Exception or OperationCanceledException)
            {
                lock (Gate) if (_login == null) _status = L.T("claude not found — set the path below");
            }
            finally { Interlocked.Exchange(ref _checking, 0); }
        });
    }

    private static void Start(AppConfig config)
    {
        try
        {
            var start = CreateStart(config, ["auth", "login"]);
            var process = Process.Start(start) ?? throw new IOException();
            ChildProcessJob.Add(process);   // the game was closed - the login ends along with it
            lock (Gate)
            {
                _login = process;
                _status = L.T("sign in in the browser… Enter — cancel");
            }
            // CLI writes the login link to the output: if the browser does not open, it can be seen in the log.
            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _ = Task.Run(async () =>
            {
                await process.WaitForExitAsync();
                Log(output.ToString());
                lock (Gate)
                {
                    if (_login != process) return;   // canceled
                    _login = null;
                }
                Check(config);
            });
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            lock (Gate) _status = L.T("claude not found — set the path below");
        }
    }

    private static async Task<string> RunAsync(AppConfig config, string[] arguments, TimeSpan timeout)
    {
        using var process = Process.Start(CreateStart(config, arguments)) ?? throw new IOException();
        process.StandardInput.Close();
        using var cancel = new CancellationTokenSource(timeout);
        var output = process.StandardOutput.ReadToEndAsync(cancel.Token);
        try { await process.WaitForExitAsync(cancel.Token); }
        catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch { } throw; }
        return await output;
    }

    // The same claude as the GM (path from settings, on macOS/Linux - PATH for the launcher from npm), and the
    // same token: check shows the input that the game uses.
    private static ProcessStartInfo CreateStart(AppConfig config, string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? config.ClaudeCliPath
                : AiCliDiscovery.Resolve("claude", config.ClaudeCliPath) ?? AiCliDiscovery.NormalizePath(config.ClaudeCliPath),
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        if (!string.IsNullOrWhiteSpace(config.ClaudeOAuthToken)) start.Environment["CLAUDE_CODE_OAUTH_TOKEN"] = config.ClaudeOAuthToken;
        if (!OperatingSystem.IsWindows()) start.Environment["PATH"] = AiCliDiscovery.ChildPath(start.FileName);
        return start;
    }

    private static void Log(string text)
    {
        try
        {
            string dir = Path.Combine(AppConfig.DataRoot, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "claude-login.log"), $"=== {DateTime.Now:s}\n{text}\n");
        }
        catch { }
    }
}
