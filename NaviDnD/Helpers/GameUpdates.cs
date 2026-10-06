using System.Diagnostics;
using System.Net;
using NaviDnD.Display;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NaviDnD.Helpers;

internal static class GameUpdates
{
    private const string ProjectApi = "https://gitlab.com/api/v4/projects/navitalevich%2Fnavi-dnd";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(15) };
    internal static string CurrentVersion => typeof(GameUpdates).Assembly.GetName().Version!.ToString(3);
    private static Task<Release?>? pending;
    internal sealed record Asset(string Name, string Url);
    internal sealed record Release(string Version, string Page, Asset[] Assets);

    internal static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("NAVIDND_TEST_WORLDSTATE") == null)
            pending = CheckAsync();
    }

    private static string ErrorPath => Path.Combine(AppConfig.ProjectRoot, "Storage", "update-error.txt");
    internal static string MenuLabel => File.Exists(ErrorPath) ? "ОШИБКА ОБНОВЛЕНИЯ" : pending?.IsCompletedSuccessfully == true && pending.Result is { } release
        ? $"ОБНОВЛЕНИЕ {release.Version}" : "ОБНОВЛЕНИЯ";

    internal static bool IsNewer(string candidate, string current) =>
        Version.TryParse(candidate.TrimStart('v'), out var next) && Version.TryParse(current, out var installed) && next > installed;

    private static async Task<Release?> CheckAsync(CancellationToken token = default, bool reportErrors = false)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var response = await Client.GetAsync(ProjectApi + "/releases/permalink/latest", timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = json.RootElement;
            string version = root.GetProperty("tag_name").GetString()!;
            if (!IsNewer(version, CurrentVersion)) return null;
            var assets = root.GetProperty("assets").GetProperty("links").EnumerateArray()
                .Select(a => new Asset(a.GetProperty("name").GetString()!, a.GetProperty("url").GetString()!)).ToArray();
            return new Release(version, root.GetProperty("_links").GetProperty("self").GetString()!, assets);
        }
        catch (Exception error)
        {
            Log(error.Message);
            if (reportErrors) throw;
            return null;
        }
    }

    internal static async Task<bool> ShowAsync(UpdateDisplay view)
    {
        view.Open();
        using var cancellation = new CancellationTokenSource();
        try
        {
            if (File.Exists(ErrorPath))
            {
                string error = await File.ReadAllTextAsync(ErrorPath);
                Log(error);
                await view.ConfirmAsync("Предыдущее обновление: " + error, false);
                File.Delete(ErrorPath);
                return false;
            }
            pending = CheckAsync(cancellation.Token, reportErrors: true);
            var release = await view.RunAsync(pending, cancellation);
            if (release == null)
            {
                await view.ConfirmAsync($"Установлена актуальная версия {CurrentVersion}.", false);
                return false;
            }
            if (!await view.ConfirmAsync($"Доступна {release.Version}. Сохранения и настройки сохранятся.", true)) return false;
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "release-version.txt")))
            {
                await view.ConfirmAsync("Для исходников: git pull и пересборка. Обновляется установленная игра.", false);
                return false;
            }
            string directory = Path.Combine(Path.GetTempPath(), "NaviDnD-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var sums = release.Assets.Single(a => a.Name == "SHA256SUMS.txt");
                string manifest = Path.Combine(directory, sums.Name);
                view.Set("Получение списка файлов…");
                await view.RunAsync(DownloadAsync(sums, manifest, cancellation.Token), cancellation);
                var files = ParseChecksums(await File.ReadAllTextAsync(manifest));
                if (!files.ContainsKey("NaviDnD-Setup-win-x64.exe") || !files.Keys.Any(n => n.StartsWith("NaviDnD-payload.")))
                    throw new InvalidDataException("В релизе отсутствуют файлы установщика.");
                int index = 0;
                foreach (var file in files)
                {
                    index++;
                    view.Set($"Загрузка {index}/{files.Count}: {file.Key}", 0);
                    var asset = release.Assets.Single(a => a.Name == file.Key);
                    string target = Path.Combine(directory, file.Key);
                    await view.RunAsync(DownloadAsync(asset, target, cancellation.Token, (read, total) =>
                        view.Set($"Загрузка {index}/{files.Count}: {file.Key}", total > 0 ? (double)read / total : null,
                            $"{read / 1048576.0:F1} МБ скачано • Esc — отменить")), cancellation);
                    view.Set("Проверка контрольной суммы: " + file.Key, 1);
                    await using var input = File.OpenRead(target);
                    string hash = await view.RunAsync(HashAsync(input, cancellation.Token), cancellation);
                    if (!hash.Equals(file.Value, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Не совпала контрольная сумма: " + file.Key);
                }
                var start = new ProcessStartInfo(Path.Combine(directory, "NaviDnD-Setup-win-x64.exe")) { UseShellExecute = true };
                view.Set("Загрузка завершена. Перезапуск игры…", 1, "Сохранения и настройки сохраняются");
                start.ArgumentList.Add("--update-silent");
                start.ArgumentList.Add(AppContext.BaseDirectory);
                start.ArgumentList.Add(Environment.ProcessId.ToString());
                _ = Process.Start(start) ?? throw new IOException("Не удалось запустить установщик.");
                return true;
            }
            catch
            {
                Directory.Delete(directory, true);
                throw;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception error)
        {
            Log(error.Message);
            await view.ConfirmAsync("Обновление не установлено: " + error.Message, false);
            return false;
        }
    }

    internal static Dictionary<string, string> ParseChecksums(string text)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line.Trim(), @"^([a-fA-F0-9]{64})\s+(NaviDnD-Setup-win-x64\.exe|NaviDnD-payload\.\d{3})$");
            if (!match.Success || !files.TryAdd(match.Groups[2].Value, match.Groups[1].Value))
                throw new InvalidDataException("Некорректный список контрольных сумм.");
        }
        return files;
    }

    private static async Task<string> HashAsync(Stream input, CancellationToken token) =>
        Convert.ToHexString(await SHA256.HashDataAsync(input, token));

    private static async Task<bool> DownloadAsync(Asset asset, string destination, CancellationToken token,
        Action<long, long?>? progress = null)
    {
        var uri = new Uri(asset.Url);
        if (uri.Scheme != "https" || uri.Host != "gitlab.com") throw new InvalidDataException("Некорректный адрес обновления.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var output = File.Create(destination);
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        await CopyWithProgressAsync(input, output, response.Content.Headers.ContentLength, progress, timeout.Token);
        return true;
    }

    internal static async Task CopyWithProgressAsync(Stream input, Stream output, long? length,
        Action<long, long?>? progress, CancellationToken token)
    {
        byte[] buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            progress?.Invoke(received, length);
        }
    }

    private static void Log(string message)
    {
        try { string dir = Path.Combine(AppConfig.ProjectRoot, "logs"); Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir, "updates.log"), $"{DateTime.Now:O} {message}\n"); } catch { }
    }
}
