using System.IO.Compression;
using System.Text.Json;

namespace NaviDnD.Installer;

// Release patch - ZIP: patch.json (description) and files/<path> (new and changed files).  BaseFiles - a
// complete list of files of the original version with SHA256:, it is used to check that it is the one installed
// and nothing is damaged (otherwise - a complete installation of the database); Files - new/changed files with
// SHA256 result;  Removed - files of the original version to be deleted. Storage/, logs/ and assistant Updater/
// never touch the patch.  VersionFile - where release-version.txt is located (for NaviDnD.app on macOS -
// Contents/MacOS/, the root of the patch is the application itself).
internal sealed record Patch(string BaseVersion, string Version, Dictionary<string, string> BaseFiles,
    Dictionary<string, string> Files, string[] Removed)
{
    internal const string DefaultVersionFile = "release-version.txt";
    public string? VersionFile { get; init; }
    internal string VersionPath => VersionFile ?? DefaultVersionFile;

    internal const string ManifestName = "patch.json";
    internal const string FilesPrefix = "files/";

    internal static Patch Read(ZipArchive archive)
    {
        using var input = archive.GetEntry(ManifestName)?.Open() ?? throw new InvalidDataException(L.T("The patch has no description."));
        Patch patch;
        try { patch = JsonSerializer.Deserialize<Patch>(input, UpdateManifest.Json) ?? throw new InvalidDataException(L.T("Invalid patch.")); }
        catch (JsonException) { throw new InvalidDataException(L.T("Invalid patch description.")); }
        if (patch.BaseVersion == null || patch.Version == null
            || UpdateManifest.ParseVersion(patch.Version) <= UpdateManifest.ParseVersion(patch.BaseVersion))
            throw new InvalidDataException(L.T("Invalid patch version."));
        if (patch.BaseFiles == null || patch.Files == null || patch.Removed == null || !patch.Files.ContainsKey(RelativePath(patch.VersionPath)))
            throw new InvalidDataException(L.T("Incomplete patch description."));
        foreach (var file in patch.BaseFiles.Concat(patch.Files))
        {
            RelativePath(file.Key);
            if (file.Value is not { Length: 64 } || !file.Value.All(Uri.IsHexDigit)) throw new InvalidDataException(L.T("Invalid file checksum."));
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in patch.Files.Keys.Concat(patch.Removed))
            if (!names.Add(RelativePath(name))) throw new InvalidDataException(L.T("Duplicate patch path."));
        foreach (string name in patch.Removed)
            if (!patch.BaseFiles.ContainsKey(name)) throw new InvalidDataException(L.T("A file to remove is missing from the base."));
        foreach (string name in patch.Files.Keys)
            if (archive.GetEntry(FilesPrefix + name) == null) throw new InvalidDataException(L.T("The patch is missing a file: ") + name);
        return patch;
    }

    // Whether the original version of the patch is installed. full - all database files (damaged installation),
    // otherwise - only those that the patch replaces or deletes (the next patches in the chain: the rest has
    // already been checked first).
    internal void VerifyBase(string root, bool full, Action<double>? progress = null)
    {
        string versionFile = Path.Combine(root, VersionPath);
        string version = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "";
        if (version != BaseVersion) throw new InvalidDataException(L.F("The patch is for version {0}, installed is {1}.", BaseVersion, version.Length > 0 ? version : L.T("unknown")));
        var check = full ? BaseFiles.ToList() : BaseFiles.Where(f => Files.ContainsKey(f.Key) || Removed.Contains(f.Key)).ToList();
        for (int i = 0; i < check.Count; i++)
        {
            var (name, sha) = (check[i].Key, check[i].Value);
            string path = Path.Combine(root, RelativePath(name));
            RejectLinks(root, path);
            if (!File.Exists(path) || UpdateManifest.Sha256(path) != sha.ToLowerInvariant())
                throw new InvalidDataException(L.T("A game file is modified or missing: ") + name);
            progress?.Invoke((i + 1) / (double)check.Count);
        }
    }

    internal static string RelativePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Contains('\0') ||
            name.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ')))
            throw new InvalidDataException(L.T("Invalid patch path: ") + name);
        string first = name.Split('/')[0];
        if (first.Equals("Storage", StringComparison.OrdinalIgnoreCase) || first.Equals("logs", StringComparison.OrdinalIgnoreCase)
            || first.Equals("Updater", StringComparison.OrdinalIgnoreCase) || first.StartsWith(".update-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T("Invalid patch path: ") + name);
        return name;
    }

    internal static void RejectLinks(string root, string target)
    {
        for (string? path = target; path != null && path.Length >= root.TrimEnd(Path.DirectorySeparatorChar).Length; path = Path.GetDirectoryName(path))
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(L.T("An installed file must not be a link."));
    }
}

internal static class PatchInstaller
{
    // Patches in order with one operation: the first one checks the entire database, each next one checks what
    // starts with the version of the previous one. Files are extracted and checked against SHA256 before
    // replacement; replaced and deleted ones go into reserve; in case of any error, everything returns as it
    // was. Storage/ and logs/ are not affected. Returns the installed version.
    internal static string ApplyChain(string root, IReadOnlyList<string> patchZips, Action<string, double?>? progress = null)
    {
        root = Path.GetFullPath(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException(L.T("The install folder must not be a link."));
        if (patchZips.Count == 0) throw new ArgumentException(L.T("No patches."));
        string work = Path.Combine(root, ".update-" + Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(work, "new"), backup = Path.Combine(work, "old");
        var changed = new List<(string Target, string? Backup)>();
        var removedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool keepBackup = false;
        string? version = null;
        try
        {
            for (int i = 0; i < patchZips.Count; i++)
            {
                using var archive = ZipFile.OpenRead(patchZips[i]);
                var patch = Patch.Read(archive);
                if (version != null && patch.BaseVersion != version) throw new InvalidDataException(L.T("Gap in the patch chain."));
                progress?.Invoke(L.F("Checking game files ({0})…", patch.BaseVersion), 0);
                patch.VerifyBase(root, full: i == 0, f => progress?.Invoke(L.F("Checking game files ({0})…", patch.BaseVersion), f));

                string stage = Path.Combine(staging, i.ToString());
                int n = 0;
                foreach (var (name, sha) in patch.Files)
                {
                    string temp = SafePath(stage, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                    archive.GetEntry(Patch.FilesPrefix + name)!.ExtractToFile(temp);
                    if (UpdateManifest.Sha256(temp) != sha.ToLowerInvariant()) throw new InvalidDataException(L.T("Corrupted patch file: ") + name);
                    progress?.Invoke(L.F("Updating to {0}…", patch.Version), ++n / (double)(patch.Files.Count + patch.Removed.Length));
                }
                foreach (var name in patch.Files.Keys)
                {
                    string target = SafePath(root, name);
                    Patch.RejectLinks(root, target);
                    if (Directory.Exists(target)) throw new IOException(L.T("A folder is in place of the file: ") + name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    string? old = null;
                    if (File.Exists(target))
                    {
                        old = SafePath(backup, $"{i}/{name}");
                        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
                        File.Move(target, old);
                    }
                    changed.Add((target, old));
                    File.Move(SafePath(stage, name), target);
                }
                foreach (var name in patch.Removed)
                {
                    string target = SafePath(root, name);
                    Patch.RejectLinks(root, target);
                    if (!File.Exists(target)) continue;
                    string old = SafePath(backup, $"{i}/{name}");
                    Directory.CreateDirectory(Path.GetDirectoryName(old)!);
                    File.Move(target, old);
                    changed.Add((target, old));
                    removedDirs.Add(Path.GetDirectoryName(target)!);
                }
                version = patch.Version;
            }
        }
        catch (Exception installError)
        {
            try
            {
                foreach (var file in changed.AsEnumerable().Reverse())
                {
                    if (File.Exists(file.Target)) File.Delete(file.Target);
                    if (file.Backup != null) File.Move(file.Backup, file.Target);
                }
            }
            catch (Exception rollbackError)
            {
                keepBackup = true;
                throw new IOException(L.T("Update and rollback failed. Backup files are kept in: ") + backup,
                    new AggregateException(installError, rollbackError));
            }
            throw;
        }
        finally { if (!keepBackup && Directory.Exists(work)) Directory.Delete(work, true); }

        // Remove empty folders after deletion (not above the game folder).
        foreach (string dir in removedDirs.OrderByDescending(d => d.Length))
            for (string? path = dir; path != null && path.Length > root.Length; path = Path.GetDirectoryName(path))
            {
                try { if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); else break; }
                catch (IOException) { break; }
            }
        return version!;
    }

    // Full installation on macOS (the game files have been changed or the version is out of chain; on Windows
    // this is done by the installer): parts of the base → ZIP → unpacking next to the game, a chain of patches
    // on top, then all files are replaced with a reserve; If there is an error, everything returns as it was.
    // The extra files of the previous version remain - just like the Windows installer on top of the old
    // folder. folder is the game folder in the database archive, versionFile is the path to release-version.txt
    // inside it.
    internal static void InstallBase(string root, IReadOnlyList<string> parts, IReadOnlyList<string> patches, Action<string, double?> progress,
        string folder = "NaviDnD", string versionFile = Patch.DefaultVersionFile)
    {
        root = Path.GetFullPath(root);
        string work = Path.Combine(root, ".update-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(work, "old");
        var changed = new List<(string Target, string? Backup)>();
        bool keepBackup = false;
        try
        {
            Directory.CreateDirectory(work);
            string zip = Path.Combine(work, "base.zip");
            progress(L.T("Unpacking the update…"), null);
            using (var output = File.Create(zip))
                foreach (string part in parts)
                {
                    using var input = File.OpenRead(part);
                    input.CopyTo(output);
                }
            string extracted = Path.Combine(work, "base");
            ZipFile.ExtractToDirectory(zip, extracted);
            File.Delete(zip);
            string fresh = Path.Combine(extracted, folder);
            if (!File.Exists(Path.Combine(fresh, versionFile))) throw new InvalidDataException(L.T("Incomplete update description."));
            if (patches.Count > 0) PatchInstaller.ApplyChain(fresh, patches, progress);

            string[] files = Directory.GetFiles(fresh, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string name = Patch.RelativePath(Path.GetRelativePath(fresh, files[i]).Replace(Path.DirectorySeparatorChar, '/'));
                string target = Path.Combine(root, name);
                Patch.RejectLinks(root, target);
                if (Directory.Exists(target)) throw new IOException(L.T("A folder is in place of the file: ") + name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string? old = null;
                if (File.Exists(target))
                {
                    old = Path.Combine(backup, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(old)!);
                    File.Move(target, old);
                }
                changed.Add((target, old));
                File.Move(files[i], target);
                progress(L.T("Installing the update…"), (i + 1) / (double)files.Length);
            }
        }
        catch (Exception installError)
        {
            try
            {
                foreach (var file in changed.AsEnumerable().Reverse())
                {
                    if (File.Exists(file.Target)) File.Delete(file.Target);
                    if (file.Backup != null) File.Move(file.Backup, file.Target);
                }
            }
            catch (Exception rollbackError)
            {
                keepBackup = true;
                throw new IOException(L.T("Update and rollback failed. Backup files are kept in: ") + backup,
                    new AggregateException(installError, rollbackError));
            }
            throw;
        }
        finally { if (!keepBackup && Directory.Exists(work)) Directory.Delete(work, true); }
    }

    private static string SafePath(string root, string relative)
    {
        string target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T("Invalid path in the patch."));
        return target;
    }
}
