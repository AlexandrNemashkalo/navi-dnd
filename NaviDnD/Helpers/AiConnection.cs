using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NaviDnD.Helpers;

public enum AiConnectionStatus { Checking, PathNotFound, DesktopOnly, NotAuthorized, Connected, SigningIn, CheckFailed }
public sealed record AiConnectionState(AiConnectionStatus Status, string Account = "", string Path = "");

// Authentication commands only: never sends a prompt or spends model tokens.
public sealed class AiConnection
{
    private readonly object _gate = new();
    private AiConnectionState _state = new(AiConnectionStatus.Checking);
    private Process? _login;
    private string? _loginProvider;
    private int _generation;
    private string? _foundPath;
    private string? _foundProvider;
    public AiConnectionState State { get { lock (_gate) return _state; } }
    public string? TakeFoundPath(string provider)
    {
        lock (_gate)
        {
            var path = _foundProvider == provider ? _foundPath : null;
            _foundPath = null;
            return path;
        }
    }

    public void Find(AppConfig config)
    {
        string provider = config.AiProvider == "codex" ? "codex" : "claude";
        int generation = Interlocked.Increment(ref _generation);
        lock (_gate) _state = new(AiConnectionStatus.Checking);
        _ = Task.Run(() =>
        {
            string? path = AiCliDiscovery.Find(provider);
            lock (_gate)
                if (generation == _generation)
                {
                    _foundPath = path;
                    _foundProvider = provider;
                    if (path == null) _state = new(AiConnectionStatus.PathNotFound);
                }
        });
    }

    public void Check(AppConfig config)
    {
        string provider = config.AiProvider == "codex" ? "codex" : "claude";
        string configured = provider == "codex" ? config.CodexCliPath : config.ClaudeCliPath;
        string token = config.ClaudeOAuthToken;
        lock (_gate)
        {
            if (_login is { HasExited: false } running)
            {
                if (_loginProvider == provider) return;
                try { running.Kill(entireProcessTree: true); } catch { }
                _login = null;
            }
        }
        int generation = Interlocked.Increment(ref _generation);
        lock (_gate) _state = new(AiConnectionStatus.Checking);
        _ = Task.Run(async () =>
        {
            AiConnectionState state;
            string? path = AiCliDiscovery.Resolve(provider, configured);
            if (path != null && !string.Equals(path, AiCliDiscovery.NormalizePath(configured), StringComparison.Ordinal))
                lock (_gate) if (generation == _generation) { _foundPath = path; _foundProvider = provider; }
            if (path == null) state = new(provider == "claude" && AiCliDiscovery.IsClaudeDesktopInstalled(configured)
                ? AiConnectionStatus.DesktopOnly : AiConnectionStatus.PathNotFound);
            else
            {
                try
                {
                    using var process = Process.Start(StartInfo(provider, path, token,
                        provider == "codex" ? ["login", "status"] : ["auth", "status"])) ?? throw new IOException();
                    process.StandardInput.Close();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var output = process.StandardOutput.ReadToEndAsync();
                    var errors = process.StandardError.ReadToEndAsync();
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch { try { process.Kill(entireProcessTree: true); } catch { } throw; }
                    string stdout = await output, stderr = await errors;
                    state = ParseStatus(provider, process.ExitCode, stdout, stderr) with { Path = path };
                    if (state.Status == AiConnectionStatus.Connected && provider == "codex"
                        && (stdout + stderr).Contains("ChatGPT", StringComparison.OrdinalIgnoreCase))
                        state = state with { Account = CodexAccount() };
                }
                catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or JsonException
                    or InvalidOperationException or OperationCanceledException or ArgumentException)
                { state = new(AiConnectionStatus.CheckFailed, Path: path); }
            }
            lock (_gate) if (generation == _generation && _login == null) _state = state;
        });
    }

    public void SignIn(AppConfig config)
    {
        lock (_gate)
        {
            if (_login is { HasExited: false } running)
            {
                try { running.Kill(entireProcessTree: true); } catch { }
                _login = null;
                Check(config);
                return;
            }
        }
        string provider = config.AiProvider == "codex" ? "codex" : "claude";
        string? path = AiCliDiscovery.Resolve(provider, provider == "codex" ? config.CodexCliPath : config.ClaudeCliPath);
        if (path == null) { lock (_gate) _state = new(AiConnectionStatus.PathNotFound); return; }
        Interlocked.Increment(ref _generation);
        try
        {
            var process = Process.Start(StartInfo(provider, path, "", provider == "codex" ? ["login"] : ["auth", "login"], signIn: true)) ?? throw new IOException();
            ChildProcessJob.Add(process);
            // Drain both streams without storing login URLs, codes or credentials in logs.
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            lock (_gate) { _login = process; _loginProvider = provider; _state = new(AiConnectionStatus.SigningIn, Path: path); }
            _ = Task.Run(async () =>
            {
                try { await process.WaitForExitAsync(); await Task.WhenAll(output, errors); }
                finally
                {
                    bool check;
                    lock (_gate) { check = _login == process; if (check) _login = null; }
                    process.Dispose();
                    if (check) Check(config);
                }
            });
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { lock (_gate) _state = new(AiConnectionStatus.CheckFailed, Path: path); }
    }

    public static AiConnectionState ParseStatus(string provider, int exitCode, string output, string errors)
    {
        if (provider == "codex")
        {
            string text = output + "\n" + errors;
            return new(text.Contains("Not logged in", StringComparison.OrdinalIgnoreCase) ? AiConnectionStatus.NotAuthorized
                : exitCode == 0 && text.Contains("Logged in", StringComparison.OrdinalIgnoreCase) ? AiConnectionStatus.Connected : AiConnectionStatus.CheckFailed);
        }
        int start = output.IndexOf('{');
        if (start < 0) return new(AiConnectionStatus.CheckFailed);
        using var json = JsonDocument.Parse(output[start..]);
        var root = json.RootElement;
        if (!root.TryGetProperty("loggedIn", out var loggedIn)) return new(AiConnectionStatus.CheckFailed);
        return new(loggedIn.ValueKind == JsonValueKind.True ? AiConnectionStatus.Connected : AiConnectionStatus.NotAuthorized,
            root.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String ? email.GetString() ?? "" : "");
    }

    private static ProcessStartInfo StartInfo(string provider, string path, string token, string[] arguments, bool signIn = false)
    {
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        if (provider == "claude")
        {
            if (signIn) start.Environment.Remove("CLAUDE_CODE_OAUTH_TOKEN");
            if (!string.IsNullOrWhiteSpace(token)) start.Environment["CLAUDE_CODE_OAUTH_TOKEN"] = token;
        }
        if (!OperatingSystem.IsWindows()) start.Environment["PATH"] = AiCliDiscovery.ChildPath(path);
        return start;
    }

    private static string CodexAccount()
    {
        try
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            using var auth = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "auth.json")));
            string token = auth.RootElement.GetProperty("tokens").GetProperty("id_token").GetString() ?? "";
            return AccountFromIdToken(token);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return ""; }
    }

    // Display metadata only; authentication is established by the CLI, never by decoding a JWT.
    public static string AccountFromIdToken(string token)
    {
        try
        {
            string payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            foreach (string key in new[] { "email", "preferred_username", "name" })
                if (json.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!;
        }
        catch (Exception e) when (e is JsonException or FormatException or IndexOutOfRangeException or InvalidOperationException) { }
        return "";
    }
}
