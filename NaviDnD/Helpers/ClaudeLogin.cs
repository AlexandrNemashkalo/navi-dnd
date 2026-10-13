using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NaviDnD.Helpers;

// Вход в Claude из настроек: `claude auth login` открывает вход в браузере и сохраняет его в самом Claude Code
// (Windows — .claude/.credentials.json, macOS — Связка ключей), `claude auth status` — проверка (JSON). Токен игре
// не нужен: вход обновляется сам. Всё в фоне: экран настроек показывает состояние и перерисовывается по Status.
internal static class ClaudeLogin
{
    private static readonly object Gate = new();
    private static Process? _login;
    private static string _status = "";
    private static int _checking;

    internal static string Status { get { lock (Gate) return _status; } }

    // Пункт настроек: идёт вход — отменить, иначе — войти.
    internal static void Toggle(AppConfig config)
    {
        lock (Gate)
        {
            if (_login is { HasExited: false } running)
            {
                try { running.Kill(entireProcessTree: true); } catch { }
                _login = null;
                _status = L.T("вход отменён");
                return;
            }
        }
        Start(config);
    }

    // Проверка входа (открытие настроек и после входа).
    internal static void Check(AppConfig config)
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        lock (Gate) if (_login == null) _status = L.T("проверка…");
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
                    _status = loggedIn ? (email.Length > 0 ? L.F("выполнен: {0}", email) : L.T("выполнен")) : L.T("не выполнен — Enter");
            }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidOperationException
                or System.ComponentModel.Win32Exception or OperationCanceledException)
            {
                lock (Gate) if (_login == null) _status = L.T("claude не найден — укажи путь ниже");
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
            ChildProcessJob.Add(process);   // игру закрыли — вход завершается вместе с ней
            lock (Gate)
            {
                _login = process;
                _status = L.T("войди в браузере… Enter — отмена");
            }
            // Ссылку для входа CLI пишет в вывод: если браузер не открылся, её видно в логе.
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
                    if (_login != process) return;   // отменён
                    _login = null;
                }
                Check(config);
            });
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            lock (Gate) _status = L.T("claude не найден — укажи путь ниже");
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

    // Тот же claude, что у мастера (путь из настроек, на macOS/Linux — PATH для запускателя из npm), и тот же токен:
    // проверка показывает вход, которым пользуется игра.
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
