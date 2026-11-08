using System.IO.Compression;

namespace NaviDnD.Installer;

internal static class Installation
{
    // progress: stage and share 0..1 of the entire installation of the base (gluing parts ~10%, unpacking ~80%, transferring to the game folder ~10%).
    internal static void Apply(string destination, string partsDirectory, Action<string, double?>? progress = null)
    {
        string root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException(L.T("The install folder must not be a link."));
        string work = Path.Combine(root, ".update-" + Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(work, "new"), backup = Path.Combine(work, "old");
        Directory.CreateDirectory(staging);
        var changed = new List<(string Target, string? Backup)>();
        bool keepBackup = false;
        try
        {
            var parts = Directory.GetFiles(partsDirectory, "NaviDnD-payload.*").OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (parts.Length == 0) throw new FileNotFoundException(L.T("All NaviDnD-payload.* files must be next to the installer."));
            using var stream = new FileStream(Path.Combine(work, "payload.zip"), FileMode.CreateNew, FileAccess.ReadWrite);
            long total = parts.Sum(p => new FileInfo(p).Length), joined = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (Path.GetFileName(parts[i]) != $"NaviDnD-payload.{i + 1:D3}") throw new InvalidDataException(L.T("A part of the archive is missing."));
                using var input = File.OpenRead(parts[i]); input.CopyTo(stream);
                joined += input.Length;
                progress?.Invoke(L.T("Preparing the archive…"), 0.1 * joined / Math.Max(1, total));
            }
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            long size = Math.Max(1, archive.Entries.Sum(e => e.Length)), unpacked = 0;
            foreach (var entry in archive.Entries)
            {
                unpacked += entry.Length;
                progress?.Invoke(L.T("Unpacking the game…"), 0.1 + 0.8 * unpacked / size);
                string relative = entry.FullName.Replace('\\', '/');
                string first = relative.Split('/')[0];
                if (first.Equals("Storage", StringComparison.OrdinalIgnoreCase) || first.Equals("logs", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(L.T("The archive contains user data."));
                string target = SafePath(staging, relative);
                if (relative.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target);
            }
            if (!File.Exists(Path.Combine(staging, "NaviDnD.exe"))) throw new InvalidDataException(L.T("The archive doesn't contain the game."));
            var files = Directory.GetFiles(staging, "*", SearchOption.AllDirectories);
            int moved = 0;
            foreach (string source in files)
            {
                progress?.Invoke(L.T("Installing files…"), 0.9 + 0.1 * ++moved / files.Length);
                string relative = Path.GetRelativePath(staging, source);
                string target = SafePath(root, relative);
                EnsureNoLinks(root, target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string? old = null;
                if (File.Exists(target))
                {
                    old = SafePath(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(old)!);
                    File.Move(target, old);
                }
                changed.Add((target, old));
                File.Move(source, target);   // unpacking - in the game folder, the same disk: transfer without copying 1.5 GB
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
        finally { if (!keepBackup) Directory.Delete(work, true); }
    }

    private static string SafePath(string root, string relative)
    {
        string target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T("Invalid path in the archive."));
        return target;
    }

    private static void EnsureNoLinks(string root, string target)
    {
        for (string? path = target; path != null && path.Length > root.Length; path = Path.GetDirectoryName(path))
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(L.T("An installed file must not be a link."));
    }
}
