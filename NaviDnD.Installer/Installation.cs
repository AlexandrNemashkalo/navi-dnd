using System.IO.Compression;

namespace NaviDnD.Installer;

internal static class Installation
{
    // progress: этап и доля 0..1 всей установки базы (склейка частей ~10%, распаковка ~80%, перенос в папку игры ~10%).
    internal static void Apply(string destination, string partsDirectory, Action<string, double?>? progress = null)
    {
        string root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Папка установки не должна быть ссылкой.");
        string work = Path.Combine(root, ".update-" + Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(work, "new"), backup = Path.Combine(work, "old");
        Directory.CreateDirectory(staging);
        var changed = new List<(string Target, string? Backup)>();
        bool keepBackup = false;
        try
        {
            var parts = Directory.GetFiles(partsDirectory, "NaviDnD-payload.*").OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (parts.Length == 0) throw new FileNotFoundException("Рядом с установщиком нужны все NaviDnD-payload.*.");
            using var stream = new FileStream(Path.Combine(work, "payload.zip"), FileMode.CreateNew, FileAccess.ReadWrite);
            long total = parts.Sum(p => new FileInfo(p).Length), joined = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (Path.GetFileName(parts[i]) != $"NaviDnD-payload.{i + 1:D3}") throw new InvalidDataException("Отсутствует часть архива.");
                using var input = File.OpenRead(parts[i]); input.CopyTo(stream);
                joined += input.Length;
                progress?.Invoke("Подготовка архива…", 0.1 * joined / Math.Max(1, total));
            }
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            long size = Math.Max(1, archive.Entries.Sum(e => e.Length)), unpacked = 0;
            foreach (var entry in archive.Entries)
            {
                unpacked += entry.Length;
                progress?.Invoke("Распаковка игры…", 0.1 + 0.8 * unpacked / size);
                string relative = entry.FullName.Replace('\\', '/');
                string first = relative.Split('/')[0];
                if (first.Equals("Storage", StringComparison.OrdinalIgnoreCase) || first.Equals("logs", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Архив содержит пользовательские данные.");
                string target = SafePath(staging, relative);
                if (relative.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target);
            }
            if (!File.Exists(Path.Combine(staging, "NaviDnD.exe"))) throw new InvalidDataException("Архив не содержит игру.");
            var files = Directory.GetFiles(staging, "*", SearchOption.AllDirectories);
            int moved = 0;
            foreach (string source in files)
            {
                progress?.Invoke("Установка файлов…", 0.9 + 0.1 * ++moved / files.Length);
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
                File.Move(source, target);   // распаковка — в папке игры, тот же диск: перенос без копирования 1,5 ГБ
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
                throw new IOException("Ошибка обновления и отката. Резервные файлы сохранены: " + backup,
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
            throw new InvalidDataException("Некорректный путь в архиве.");
        return target;
    }

    private static void EnsureNoLinks(string root, string target)
    {
        for (string? path = target; path != null && path.Length > root.Length; path = Path.GetDirectoryName(path))
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Файл установки не должен быть ссылкой.");
    }
}
