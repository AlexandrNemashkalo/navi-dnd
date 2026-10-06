using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
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

    internal static string MenuLabel => pending?.IsCompletedSuccessfully == true && pending.Result is { } release
        ? $"ОБНОВЛЕНИЕ {release.Version}" : "ОБНОВЛЕНИЯ";

    internal static bool IsNewer(string candidate, string current) =>
        Version.TryParse(candidate.TrimStart('v'), out var next) && Version.TryParse(current, out var installed) && next > installed;

    private static async Task<Release?> CheckAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
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
            return null;
        }
    }

    internal static async Task<bool> ShowAsync()
    {
        try
        {
            pending = CheckAsync();
            var release = await pending;
            if (release == null)
            {
                Notify($"Установлена версия {CurrentVersion}.\nНовая версия не найдена. При отсутствии интернета проверку можно повторить позже.");
                return false;
            }
            if (MessageBox(IntPtr.Zero, $"Доступна {release.Version} (установлена {CurrentVersion}).\n\nСкачать и установить? Игра закроется. Сохранения и настройки останутся на месте.",
                "Обновление NaviDnD", 0x24 | 0x10000 | 0x40000) != 6) return false;
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "release-version.txt")))
            {
                Notify("Обновление из игры доступно для установленной сборки. Для исходников используйте git pull и пересборку.");
                return false;
            }
            string directory = Path.Combine(Path.GetTempPath(), "NaviDnD-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var sums = release.Assets.Single(a => a.Name == "SHA256SUMS.txt");
                string manifest = Path.Combine(directory, sums.Name);
                await DownloadAsync(sums, manifest);
                var files = ParseChecksums(await File.ReadAllTextAsync(manifest));
                if (!files.ContainsKey("NaviDnD-Setup-win-x64.exe") || !files.Keys.Any(n => n.StartsWith("NaviDnD-payload.")))
                    throw new InvalidDataException("В релизе отсутствуют файлы установщика.");
                foreach (var file in files)
                {
                    Console.Title = "NaviDnD — скачивание " + file.Key;
                    var asset = release.Assets.Single(a => a.Name == file.Key);
                    string target = Path.Combine(directory, file.Key);
                    await DownloadAsync(asset, target);
                    await using var input = File.OpenRead(target);
                    string hash = Convert.ToHexString(await SHA256.HashDataAsync(input));
                    if (!hash.Equals(file.Value, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Не совпала контрольная сумма: " + file.Key);
                }
                var start = new ProcessStartInfo(Path.Combine(directory, "NaviDnD-Setup-win-x64.exe")) { UseShellExecute = true };
                start.ArgumentList.Add("--update");
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
        catch (Exception error)
        {
            Log(error.Message);
            Notify("Обновление не установлено.\n" + error.Message + "\nТекущая игра сохранена.");
            return false;
        }
        finally { Console.Title = "NaviDnD"; }
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

    private static async Task DownloadAsync(Asset asset, string destination)
    {
        var uri = new Uri(asset.Url);
        if (uri.Scheme != "https" || uri.Host != "gitlab.com") throw new InvalidDataException("Некорректный адрес обновления.");
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var output = File.Create(destination);
        await response.Content.CopyToAsync(output);
    }

    private static void Log(string message)
    {
        try { string dir = Path.Combine(AppConfig.ProjectRoot, "logs"); Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir, "updates.log"), $"{DateTime.Now:O} {message}\n"); } catch { }
    }
    private static void Notify(string text) => MessageBox(IntPtr.Zero, text, "NaviDnD — обновления", 0x40 | 0x10000 | 0x40000);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
