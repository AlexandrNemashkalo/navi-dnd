using System.IO.Compression;
using System.Text.Json;

namespace NaviDnD.Installer;

// Патчи для установщика: рядом с ним (releases/patches — свежая установка из репозитория, без сети) или из последнего
// релиза GitLab (старый клиент ≤1.1.4 запустил новый установщик с одной базой — патчи он докачивает сам).
internal static class UpdateClient
{
    private const string LatestRelease = "https://gitlab.com/api/v4/projects/navitalevich%2Fnavi-dnd/releases/permalink/latest";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(15) };

    internal static string InstalledVersion(string root)
    {
        string path = Path.Combine(root, "release-version.txt");
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
    }

    // Патчи из папки, выстроенные цепочкой от установленной версии; лишние (старше установленной) пропускаются.
    internal static List<string> OrderChain(string installed, IEnumerable<string> zips)
    {
        var byBase = new Dictionary<string, (string Path, string To)>();
        foreach (string zip in zips)
        {
            using var archive = ZipFile.OpenRead(zip);
            var patch = Patch.Read(archive);
            byBase[patch.BaseVersion] = (zip, patch.Version);
        }
        var chain = new List<string>();
        for (string at = installed; byBase.TryGetValue(at, out var next); at = next.To) chain.Add(next.Path);
        return chain;
    }

    // Патчи рядом с установщиком (releases/patches) — от установленной версии; пусто — рядом ничего подходящего.
    internal static List<string> LocalPatches(string setupDirectory, string installed)
    {
        string dir = Path.Combine(setupDirectory, "patches");
        return Directory.Exists(dir) ? OrderChain(installed, Directory.GetFiles(dir, "NaviDnD-patch-*.zip")) : [];
    }

    // Патчи последнего релиза от установленной версии — в workDir, с проверкой SHA256. Пусто — обновлять нечего.
    internal static async Task<List<string>> DownloadPatchesAsync(string installed, string workDir, Action<string>? status = null)
    {
        status?.Invoke(L.T("Проверка обновлений…"));
        using var release = JsonDocument.Parse(await Client.GetStringAsync(LatestRelease));
        var link = release.RootElement.GetProperty("assets").GetProperty("links").EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("name").GetString() == UpdateManifest.FileName);
        if (link.ValueKind == JsonValueKind.Undefined) return [];
        string url = link.GetProperty("url").GetString()!;
        if (!url.StartsWith("https://gitlab.com/navitalevich/navi-dnd/", StringComparison.Ordinal)) throw new InvalidDataException(L.T("Некорректный адрес обновления."));
        var manifest = UpdateManifest.Parse(await Client.GetStringAsync(url));
        var patches = manifest.PatchesFrom(installed) ?? throw new InvalidDataException(L.F("Нет патчей для версии {0}.", installed));
        var result = new List<string>();
        int index = 0;
        foreach (var patch in patches)
        {
            status?.Invoke(L.F("Загрузка обновления {0}/{1}: {2}", ++index, patches.Length, patch.To));
            string path = Path.Combine(workDir, patch.Name);
            await DownloadAsync(patch.Url, path);
            if (UpdateManifest.Sha256(path) != patch.Sha256.ToLowerInvariant()) throw new InvalidDataException(L.T("Не совпала контрольная сумма: ") + patch.Name);
            result.Add(path);
        }
        return result;
    }

    private static async Task DownloadAsync(string url, string destination)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var output = File.Create(destination);
        await response.Content.CopyToAsync(output);
    }

    // Помощник обновления — копия этого установщика в папке игры: следующие патчи применяются им без скачивания
    // установщика. Тот же файл — не копируется.
    internal static void InstallHelper(string root)
    {
        string? self = Environment.ProcessPath;
        if (self == null || !File.Exists(self) || !Path.GetFileName(self).EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
        string target = Path.Combine(root, UpdateManifest.HelperPath.Replace('/', Path.DirectorySeparatorChar));
        if (Path.GetFullPath(self).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(target) && UpdateManifest.Sha256(target) == UpdateManifest.Sha256(self)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temp = target + ".new";
        File.Copy(self, temp, true);
        File.Move(temp, target, true);
    }
}
