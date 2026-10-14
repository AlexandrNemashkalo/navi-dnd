using System.IO.Compression;
using System.Text.Json;

namespace NaviDnD.Installer;

// Patches for the installer: next to it (releases/patches - fresh installation from the repository, without a
// network) or from the latest release GitLab (the old client ≤1.1.4 launched a new installer with one database
// - it downloads the patches itself).
internal static class UpdateClient
{
    private const string LatestRelease = "https://gitlab.com/api/v4/projects/navitalevich%2Fnavi-dnd/releases/permalink/latest";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(15) };

    internal static string InstalledVersion(string root)
    {
        string path = Path.Combine(root, "release-version.txt");
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
    }

    // Patches from a folder, arranged in a chain from the installed version; extra ones (older than the established one) are skipped.
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

    // Patches next to the installer (releases/patches) - from the installed version; empty - nothing suitable nearby.
    internal static List<string> LocalPatches(string setupDirectory, string installed)
    {
        string dir = Path.Combine(setupDirectory, "patches");
        return Directory.Exists(dir) ? OrderChain(installed, Directory.GetFiles(dir, "NaviDnD-patch-*.zip")) : [];
    }

    // Patches of the latest release from the installed version - in workDir, with check SHA256. Empty - there is nothing to update.
    internal static async Task<List<string>> DownloadPatchesAsync(string installed, string workDir, Action<string>? status = null)
    {
        status?.Invoke(L.T("Checking for updates…"));
        using var release = JsonDocument.Parse(await Client.GetStringAsync(LatestRelease));
        var link = release.RootElement.GetProperty("assets").GetProperty("links").EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("name").GetString() == UpdateManifest.FileName);
        if (link.ValueKind == JsonValueKind.Undefined) return [];
        string url = link.GetProperty("url").GetString()!;
        if (!url.StartsWith("https://gitlab.com/navitalevich/navi-dnd/", StringComparison.Ordinal)) throw new InvalidDataException(L.T("Invalid update address."));
        var manifest = UpdateManifest.Parse(await Client.GetStringAsync(url));
        var patches = manifest.PatchesFrom(installed) ?? throw new InvalidDataException(L.F("No patches for version {0}.", installed));
        var result = new List<string>();
        int index = 0;
        foreach (var patch in patches)
        {
            status?.Invoke(L.F("Downloading update {0}/{1}: {2}", ++index, patches.Length, patch.To));
            string path = Path.Combine(workDir, patch.Name);
            await DownloadAsync(patch.Url, path);
            if (UpdateManifest.Sha256(path) != patch.Sha256.ToLowerInvariant()) throw new InvalidDataException(L.T("Checksum mismatch: ") + patch.Name);
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

    // Update Assistant - a copy of this installer in the game folder: the following patches are applied by it
    // without downloading the installer. The same file is not copied.
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
