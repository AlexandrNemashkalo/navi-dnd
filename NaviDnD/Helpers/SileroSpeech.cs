using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NaviDnD.Helpers;

// Вызывается единственным потребителем Speech. Модель остаётся в памяти между репликами.
internal static class SileroSpeech
{
    private static Process? worker;
    private static Task<string>? errors;
    private static string BundledRoot => Path.Combine(AppConfig.ProjectRoot, "Speech", "silero");
    private static bool HasBundle => File.Exists(Path.Combine(BundledRoot, ".ready"));
    private static string Root => HasBundle ? BundledRoot : Path.Combine(AppConfig.ProjectRoot, "Storage", "Speech", "silero");
    private static string Python => HasBundle ? Path.Combine(Root, "python", "python.exe") : Path.Combine(Root, "venv", "Scripts", "python.exe");

    public static async Task SynthesizeAsync(string text, string? ssmlText, string voice, string wav, string python,
        Action<string> status, CancellationToken token)
    {
        try
        {
            if (!File.Exists(Path.Combine(Root, ".ready")))
            {
                status(L.T("Установка Silero и PyTorch"));
                var setup = CreateStart("powershell.exe");
                foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                    Path.Combine(AppConfig.ProjectRoot, "Speech", "setup-silero.ps1"), "-Python", python, "-Destination", Root })
                    setup.ArgumentList.Add(arg);
                using var process = Process.Start(setup) ?? throw new IOException(L.T("Не удалось начать установку Silero"));
                using var kill = token.Register(() => Kill(process));
                var stdout = process.StandardOutput.ReadToEndAsync(token);
                var stderr = process.StandardError.ReadToEndAsync(token);
                await process.WaitForExitAsync(token);
                await Task.WhenAll(stdout, stderr);
                Directory.CreateDirectory(Path.Combine(AppConfig.ProjectRoot, "logs"));
                await File.WriteAllTextAsync(Path.Combine(AppConfig.ProjectRoot, "logs", "silero-setup.log"),
                    stdout.Result + stderr.Result, token);
                if (process.ExitCode != 0) throw new IOException(L.T("Установка Silero не удалась. Нужен Python 3.10–3.12 x64; проверь путь к Python и интернет."));
            }
            if (worker == null || worker.HasExited)
            {
                Close();
                status(L.T("Загрузка модели Silero"));
                var start = CreateStart(Python);
                start.Environment["PYTHONIOENCODING"] = "utf-8";
                foreach (var arg in new[] { "-u", Path.Combine(AppConfig.ProjectRoot, "Speech", "silero_worker.py"),
                    Path.Combine(Root, "v5_5_ru.pt") }) start.ArgumentList.Add(arg);
                worker = Process.Start(start) ?? throw new IOException(L.T("Не удалось запустить Silero"));
                errors = worker.StandardError.ReadToEndAsync();
                using var kill = token.Register(() => Kill(worker));
                string? ready = await worker.StandardOutput.ReadLineAsync(token);
                if (ready == null)
                    throw new IOException(L.T("Silero не загрузил модель"));
                using var handshake = JsonDocument.Parse(ready);
                if (!handshake.RootElement.TryGetProperty("ready", out var readyValue) || !readyValue.GetBoolean())
                    throw new IOException(L.T("Silero не загрузил модель"));
            }
            using var cancel = token.Register(() => Kill(worker));
            status(L.T("Синтез Silero"));
            string request = JsonSerializer.Serialize(new { text, ssml_text = ssmlText, voice, output = wav });
            await worker.StandardInput.WriteLineAsync(request.AsMemory(), token);
            await worker.StandardInput.FlushAsync(token);
            string response = await worker.StandardOutput.ReadLineAsync(token) ?? throw new IOException(L.T("Silero завершился без ответа"));
            using var result = JsonDocument.Parse(response);
            if (!result.RootElement.GetProperty("ok").GetBoolean()) throw new IOException(L.T("Silero не смог озвучить реплику"));
        }
        catch { Close(); throw; }
    }

    private static ProcessStartInfo CreateStart(string executable) => new(executable)
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
        RedirectStandardOutput = true, RedirectStandardError = true,
        StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };

    private static void Kill(Process? process) { try { process?.Kill(entireProcessTree: true); } catch { } }
    public static void Close()
    {
        Kill(worker);
        worker?.Dispose();
        worker = null;
        errors = null;
    }
}
