using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NaviDnD.Helpers;

// Вызывается единственным потребителем Speech (по одному запросу за раз). На каждую модель — свой процесс, он остаётся в
// памяти между репликами: голос «en_*» — английская (v3_en — файл игры Speech/models), остальные — русская (v5_5_ru —
// в комплекте Silero). Обе можно загрузить заранее (Speech.WarmUp), переключение языка не перезагружает модель.
internal static class SileroSpeech
{
    private sealed record Worker(Process Process, Task<string> Errors);

    private static readonly Dictionary<string, Worker> Workers = [];
    private static string BundledRoot => Path.Combine(AppConfig.ProjectRoot, "Speech", "silero");
    private static bool HasBundle => File.Exists(Path.Combine(BundledRoot, ".ready"));
    private static string Root => HasBundle ? BundledRoot : Path.Combine(AppConfig.ProjectRoot, "Storage", "Speech", "silero");
    private static string Python => !OperatingSystem.IsWindows() ? Path.Combine(Root, HasBundle ? "python" : "venv", "bin", "python3")
        : HasBundle ? Path.Combine(Root, "python", "python.exe") : Path.Combine(Root, "venv", "Scripts", "python.exe");

    internal static string EnglishModelPath => Path.Combine(AppConfig.ProjectRoot, "Speech", "models", "v3_en.pt");

    public static bool IsEnglishVoice(string voice) => voice.StartsWith("en_", StringComparison.Ordinal);

    // Silero уже установлен (готовая сборка — всегда): заранее загружать модель можно, ничего не скачивая.
    public static bool Installed => File.Exists(Path.Combine(Root, ".ready"));

    public static async Task SynthesizeAsync(string text, string? ssmlText, string voice, string wav, string python,
        Action<string> status, CancellationToken token)
    {
        if (!Installed)
        {
            status(L.T("Установка Silero и PyTorch"));
            var setup = CreateStart(OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash");
            string[] setupArguments = OperatingSystem.IsWindows()
                ? ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                   Path.Combine(AppConfig.ProjectRoot, "Speech", "setup-silero.ps1"), "-Python", python, "-Destination", Root]
                : [Path.Combine(AppConfig.ProjectRoot, "Speech", "setup-silero.sh"), "--python", FindPython(python), "--destination", Root];
            foreach (var arg in setupArguments)
                setup.ArgumentList.Add(arg);
            if (!OperatingSystem.IsWindows()) setup.Environment["PATH"] = UnixTools.ChildPath(null);
            using var process = Process.Start(setup) ?? throw new IOException(L.T("Не удалось начать установку Silero"));
            using var kill = token.Register(() => Kill(process));
            var stdout = process.StandardOutput.ReadToEndAsync(token);
            var stderr = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            await Task.WhenAll(stdout, stderr);
            Directory.CreateDirectory(Path.Combine(AppConfig.ProjectRoot, "logs"));
            await File.WriteAllTextAsync(Path.Combine(AppConfig.ProjectRoot, "logs", "silero-setup.log"),
                stdout.Result + stderr.Result, token);
            if (process.ExitCode != 0) throw new IOException(OperatingSystem.IsWindows()
                ? L.T("Установка Silero не удалась. Нужен Python 3.10–3.12 x64; проверь путь к Python и интернет.")
                : L.T("Установка Silero не удалась. Нужен Python 3.10–3.12 (например: brew install python@3.12); проверь путь к Python и интернет."));
        }
        string model = IsEnglishVoice(voice) ? EnglishModelPath : Path.Combine(Root, "v5_5_ru.pt");
        if (!File.Exists(model)) throw new FileNotFoundException(L.T("Нет файла модели озвучки — переустанови игру."), model);
        var worker = await WorkerFor(model, status, token);
        try
        {
            status(L.T("Синтез Silero"));
            // Английская модель v3 на 48 кГц синтезирует вдвое дольше 24 кГц (а втрое — дольше русской v5.5); для речи 24 кГц хватает.
            int rate = IsEnglishVoice(voice) ? 24000 : 48000;
            string request = JsonSerializer.Serialize(new { text, ssml_text = ssmlText, voice, output = wav, sample_rate = rate });
            await worker.Process.StandardInput.WriteLineAsync(request.AsMemory(), CancellationToken.None);
            await worker.Process.StandardInput.FlushAsync(CancellationToken.None);
            // Реплику остановили — синтез не обрываем (убитый процесс пришлось бы загружать заново, это дольше):
            // ждём ответа, чтобы протокол не сбился, но не дольше 10 с — дальше процесс считается зависшим.
            var read = worker.Process.StandardOutput.ReadLineAsync();
            await Task.WhenAny(read, Task.Delay(Timeout.Infinite, token)).ConfigureAwait(false);
            if (!read.IsCompleted && await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10))) != read)
                throw new TimeoutException(L.T("Silero завершился без ответа"));
            string response = await read ?? throw new IOException(L.T("Silero завершился без ответа"));
            token.ThrowIfCancellationRequested();
            using var result = JsonDocument.Parse(response);
            if (!result.RootElement.GetProperty("ok").GetBoolean()) throw new LineFailedException(L.T("Silero не смог озвучить реплику"));
        }
        // Процесс сломан (не отвечает, вышел, протокол сбит) — закрыть; остановленная или неудачная реплика — процесс цел.
        catch (Exception e) when (e is not OperationCanceledException and not LineFailedException)
        {
            Close(model);
            throw;
        }
    }

    private sealed class LineFailedException(string message) : IOException(message);

    private static async Task<Worker> WorkerFor(string model, Action<string> status, CancellationToken token)
    {
        if (Workers.TryGetValue(model, out var alive) && !alive.Process.HasExited) return alive;
        Close(model);
        status(L.T("Загрузка модели Silero"));
        var start = CreateStart(Python);
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var arg in new[] { "-u", Path.Combine(AppConfig.ProjectRoot, "Speech", "silero_worker.py"), model })
            start.ArgumentList.Add(arg);
        var process = Process.Start(start) ?? throw new IOException(L.T("Не удалось запустить Silero"));
        var worker = new Worker(process, process.StandardError.ReadToEndAsync());
        Workers[model] = worker;
        try
        {
            using var kill = token.Register(() => Kill(process));
            string? ready = await process.StandardOutput.ReadLineAsync(token);
            if (ready == null) throw new IOException(L.T("Silero не загрузил модель"));
            using var handshake = JsonDocument.Parse(ready);
            if (!handshake.RootElement.TryGetProperty("ready", out var readyValue) || !readyValue.GetBoolean())
                throw new IOException(L.T("Silero не загрузил модель"));
            return worker;
        }
        catch { Close(model); throw; }
    }

    // macOS/Linux: указанный в настройках интерпретатор, если он есть, иначе самый новый подходящий python3.1x (у PyTorch 2.6 и
    // numpy 1.26 есть сборки для 3.10–3.12) из PATH, Homebrew, python.org или pyenv; версию проверяет скрипт установки.
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    internal static string FindPython(string configured)
    {
        string home = UnixTools.Home;
        string[] extra = ["/opt/homebrew/opt/python@3.12/bin", "/opt/homebrew/opt/python@3.11/bin", "/opt/homebrew/opt/python@3.10/bin",
            "/usr/local/opt/python@3.12/bin", "/usr/local/opt/python@3.11/bin", "/usr/local/opt/python@3.10/bin",
            "/Library/Frameworks/Python.framework/Versions/3.12/bin", "/Library/Frameworks/Python.framework/Versions/3.11/bin",
            "/Library/Frameworks/Python.framework/Versions/3.10/bin", Path.Combine(home, ".pyenv", "shims"), Path.Combine(home, ".local", "bin")];
        if (configured.Trim() is { Length: > 0 } name && name != "python" && UnixTools.Which(name, extra) is { } chosen) return chosen;
        foreach (string candidate in new[] { "python3.12", "python3.11", "python3.10", "python3" })
            if (UnixTools.Which(candidate, extra) is { } found) return found;
        return "python3";
    }

    private static ProcessStartInfo CreateStart(string executable) => new(executable)
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
        RedirectStandardOutput = true, RedirectStandardError = true,
        StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };

    private static void Kill(Process? process) { try { process?.Kill(entireProcessTree: true); } catch { } }

    private static void Close(string model)
    {
        if (!Workers.Remove(model, out var worker)) return;
        Kill(worker.Process);
        worker.Process.Dispose();
    }

    public static void Close()
    {
        foreach (var model in Workers.Keys.ToList()) Close(model);
    }
}
