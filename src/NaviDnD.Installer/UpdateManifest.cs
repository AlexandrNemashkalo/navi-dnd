using System.Security.Cryptography;
using System.Text.Json;

namespace NaviDnD.Installer;

// Description of release updates (releases/NaviDnD-updates.json, link in GitLab Release): unchangeable base
// (complete assembly in parts - for first installation and recovery), assistant installer and chain of patches
// from the base to the current version. Each file is with a link to the tag where it is published, and SHA256:
// unchangeable parts are not sent again. macOS has its own chain (NaviDnD-macOS-updates.json): game files are
// different, there is no assistant - the game itself installs the patches.
internal sealed record UpdateFile(string Name, string Url, string Sha256, long Size);
internal sealed record UpdatePatch(string From, string To, string Name, string Url, string Sha256, long Size);
internal sealed record UpdateBase(string Version, UpdateFile[] Files);
internal sealed record UpdateManifest(int Format, string Version, UpdateBase Base, UpdateFile? Installer, UpdatePatch[] Patches)
{
    internal const string FileName = "NaviDnD-updates.json";
    internal const string MacFileName = "NaviDnD-macOS-updates.json";
    internal const string MacPayloadPrefix = "NaviDnD-macOS-payload.";
    internal const string InstallerName = "NaviDnD-Setup-win-x64.exe";
    // Update assistant in the game folder: a copy of the installer, launched from a temporary folder (game files are not occupied).
    internal const string HelperPath = "Updater/" + InstallerName;
    private const string UrlPrefix = "https://gitlab.com/navitalevich/navi-dnd/-/raw/";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static UpdateManifest Parse(string json, bool macOS = false)
    {
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, Json) ?? throw new InvalidDataException(L.T("Empty update description."));
        manifest.Validate(macOS);
        return manifest;
    }

    private void Validate(bool macOS)
    {
        if (Format != 1) throw new InvalidDataException(L.T("Unknown update description format."));
        if (Base?.Files is not { Length: > 0 } || (Installer == null) != macOS || Patches == null)
            throw new InvalidDataException(L.T("Incomplete update description."));
        ParseVersion(Version);
        ParseVersion(Base.Version);
        for (int i = 0; i < Base.Files.Length; i++)
        {
            Check(Base.Files[i].Name, Base.Files[i].Url, Base.Files[i].Sha256);
            if (Base.Files[i].Name != (macOS ? MacPayloadPrefix : "NaviDnD-payload.") + $"{i + 1:D3}") throw new InvalidDataException(L.T("Base parts are listed out of order."));
        }
        if (Installer != null) Check(Installer.Name, Installer.Url, Installer.Sha256);
        if (Installer != null && Installer.Name != InstallerName) throw new InvalidDataException(L.T("Invalid installer name."));
        // Chain of patches without breaks: base → ... → current version, each version is older than the previous one.
        string at = Base.Version;
        foreach (var patch in Patches)
        {
            Check(patch.Name, patch.Url, patch.Sha256);
            if (patch.From != at || ParseVersion(patch.To) <= ParseVersion(patch.From))
                throw new InvalidDataException(L.T("Gap in the patch chain."));
            at = patch.To;
        }
        if (at != Version) throw new InvalidDataException(L.T("The patch chain doesn't reach the release version."));
    }

    private static void Check(string name, string url, string sha256)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.StartsWith('.'))
            throw new InvalidDataException(L.T("Invalid update file name."));
        if (!url.StartsWith(UrlPrefix, StringComparison.Ordinal) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "gitlab.com")
            throw new InvalidDataException(L.T("Invalid update address."));
        if (sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit)) throw new InvalidDataException(L.T("Invalid checksum."));
    }

    internal static Version ParseVersion(string text) =>
        System.Version.TryParse(text, out var version) && version.Build >= 0 && version.Revision < 0
            ? version : throw new InvalidDataException(L.T("Invalid version: ") + text);

    // Patches from the installed version to the current one; null - the version is not in the chain (a complete installation of the database is required).
    internal UpdatePatch[]? PatchesFrom(string installed)
    {
        if (installed == Version) return [];
        if (installed == Base.Version) return Patches;
        int index = Array.FindIndex(Patches, p => p.From == installed);
        return index < 0 ? null : Patches[index..];
    }

    internal static string Sha256(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
