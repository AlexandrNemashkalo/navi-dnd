using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NaviDnD.Helpers;

// Called by a single consumer Speech (one request at a time). Each model has its own process, it remains in
// memory between replicas: the voice “en_*” is English (v3_en - game file Speech/models), the rest are Russian
// (v5_5_ru - included Silero). Both can be loaded in advance (Speech.WarmUp), switching the language does not
// reload the model.
internal static class SileroSpeech
{
    private sealed record Worker(Process Process, Task<string> Errors);

    private static readonly Dictionary<string, Worker> Workers = [];
    // NaviDnD.app on macOS: kit - archived in the application (Resources/Speech/silero.zip), unpacked in DataRoot.
    private static string BundledRoot => Path.Combine(AppConfig.InMacApp ? AppConfig.DataRoot : AppConfig.ProjectRoot, "Speech", "silero");
    private static string BundledZip => Path.Combine(AppConfig.ProjectRoot, "Speech", "silero.zip");
    private static readonly SemaphoreSlim UnpackGate = new(1, 1);
    private static bool HasBundle => File.Exists(Path.Combine(BundledRoot, ".ready"));
    private static string Root => HasBundle ? BundledRoot : Path.Combine(AppConfig.DataRoot, "Storage", "Speech", "silero");
    private static string Python => !OperatingSystem.IsWindows() ? Path.Combine(Root, HasBundle ? "python" : "venv", "bin", "python3")
        : HasBundle ? Path.Combine(Root, "python", "python.exe") : Path.Combine(Root, "venv", "Scripts", "python.exe");

    internal static string EnglishModelPath => Path.Combine(AppConfig.ProjectRoot, "Speech", "models", "v3_en.pt");

    public static bool IsEnglishVoice(string voice) => voice.StartsWith("en_", StringComparison.Ordinal);

    // NaviDnD.app: Python and PyTorch cannot be kept inside a signed application as a folder (the macOS
    // label accepts folders like python3.12 and *.dist-info as nested applications) - they are stored there
    // as an archive and are unpacked here upon first launch and after an update that replaces the archive
    // (label .source - size and time archive).
    public static async Task UnpackBundledAsync(Action<string>? status = null, CancellationToken token = default)
    {
        await UnpackGate.WaitAsync(token);
        try
        {
            var zip = new FileInfo(BundledZip);
            if (!zip.Exists) return;
            string source = $"{zip.Length}:{zip.LastWriteTimeUtc.Ticks}";
            string marker = Path.Combine(BundledRoot, ".source");
            if (File.Exists(marker) && File.ReadAllText(marker) == source && HasBundle) return;
            status?.Invoke(L.T("Unpacking voice-over"));
            string parent = Path.GetDirectoryName(BundledRoot)!;
            string fresh = BundledRoot + ".new";
            Directory.CreateDirectory(parent);
            if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
            await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(zip.FullName, fresh), token);
            File.WriteAllText(Path.Combine(fresh, ".source"), source);
            if (Directory.Exists(BundledRoot)) Directory.Delete(BundledRoot, true);
            Directory.Move(fresh, BundledRoot);
        }
        finally { UnpackGate.Release(); }
    }

    // Silero is already installed (ready assembly - always): you can download the model in advance without downloading anything.
    public static bool Installed => File.Exists(Path.Combine(Root, ".ready"));

    public static async Task SynthesizeAsync(string text, string? ssmlText, string voice, string wav, string python,
        Action<string> status, CancellationToken token)
    {
        if (AppConfig.InMacApp && File.Exists(BundledZip)) await UnpackBundledAsync(status, token);
        if (!Installed)
        {
            status(L.T("Installing Silero and PyTorch"));
            var setup = CreateStart(OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash");
            string[] setupArguments = OperatingSystem.IsWindows()
                ? ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                   Path.Combine(AppConfig.ProjectRoot, "Speech", "setup-silero.ps1"), "-Python", python, "-Destination", Root]
                : [Path.Combine(AppConfig.ProjectRoot, "Speech", "setup-silero.sh"), "--python", FindPython(python), "--destination", Root];
            foreach (var arg in setupArguments)
                setup.ArgumentList.Add(arg);
            if (!OperatingSystem.IsWindows()) setup.Environment["PATH"] = UnixTools.ChildPath(null);
            using var process = Process.Start(setup) ?? throw new IOException(L.T("Could not start Silero installation"));
            using var kill = token.Register(() => Kill(process));
            var stdout = process.StandardOutput.ReadToEndAsync(token);
            var stderr = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            await Task.WhenAll(stdout, stderr);
            Directory.CreateDirectory(Path.Combine(AppConfig.DataRoot, "logs"));
            await File.WriteAllTextAsync(Path.Combine(AppConfig.DataRoot, "logs", "silero-setup.log"),
                stdout.Result + stderr.Result, token);
            if (process.ExitCode != 0) throw new IOException(OperatingSystem.IsWindows()
                ? L.T("Silero installation failed. Python 3.10–3.12 x64 is required; check the Python path and your internet connection.")
                : L.T("Silero installation failed. Python 3.10–3.12 is required (for example: brew install python@3.12); check the Python path and your internet connection."));
        }
        string model = IsEnglishVoice(voice) ? EnglishModelPath : Path.Combine(Root, "v5_5_ru.pt");
        if (!File.Exists(model)) throw new FileNotFoundException(L.T("The voice model file is missing — reinstall the game."), model);
        var worker = await WorkerFor(model, status, token);
        try
        {
            status(L.T("Silero synthesis"));
            // The English model v3 at 48 kHz synthesizes twice as long as 24 kHz (and three times longer than the Russian v5.5); 24 kHz is enough for speech.
            int rate = IsEnglishVoice(voice) ? 24000 : 48000;
            string request = JsonSerializer.Serialize(new { text, ssml_text = ssmlText, voice, output = wav, sample_rate = rate });
            await worker.Process.StandardInput.WriteLineAsync(request.AsMemory(), CancellationToken.None);
            await worker.Process.StandardInput.FlushAsync(CancellationToken.None);
            // The replica was stopped - we do not interrupt the synthesis (the killed process would have to be
            // loaded again, it would take longer): we wait for a response so that the protocol does not go
            // astray, but no longer than 10 s - then the process is considered frozen.
            var read = worker.Process.StandardOutput.ReadLineAsync();
            await Task.WhenAny(read, Task.Delay(Timeout.Infinite, token)).ConfigureAwait(false);
            if (!read.IsCompleted && await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10))) != read)
                throw new TimeoutException(L.T("Silero exited without a response"));
            string response = await read ?? throw new IOException(L.T("Silero exited without a response"));
            token.ThrowIfCancellationRequested();
            using var result = JsonDocument.Parse(response);
            if (!result.RootElement.GetProperty("ok").GetBoolean()) throw new LineFailedException(L.T("Silero could not voice the line"));
        }
        // Process broken (not responding, exited, protocol broken) - close; a stopped or unsuccessful replica means the process is intact.
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
        status(L.T("Loading the Silero model"));
        var start = CreateStart(Python);
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var arg in new[] { "-u", Path.Combine(AppConfig.ProjectRoot, "Speech", "silero_worker.py"), model })
            start.ArgumentList.Add(arg);
        var process = Process.Start(start) ?? throw new IOException(L.T("Could not start Silero"));
        var worker = new Worker(process, process.StandardError.ReadToEndAsync());
        Workers[model] = worker;
        try
        {
            using var kill = token.Register(() => Kill(process));
            string? ready = await process.StandardOutput.ReadLineAsync(token);
            if (ready == null) throw new IOException(L.T("Silero did not load the model"));
            using var handshake = JsonDocument.Parse(ready);
            if (!handshake.RootElement.TryGetProperty("ready", out var readyValue) || !readyValue.GetBoolean())
                throw new IOException(L.T("Silero did not load the model"));
            return worker;
        }
        catch { Close(model); throw; }
    }

    // macOS/Linux: interpreter specified in the settings, if any, otherwise the newest suitable python3.1x
    // (PyTorch 2.6 and numpy 1.26 have assemblies for 3.10–3.12) from PATH, Homebrew, python.org or pyenv; The
    // installation script checks the version.
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
