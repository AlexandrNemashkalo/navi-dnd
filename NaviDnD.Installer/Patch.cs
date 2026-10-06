using System.IO.Compression;
using System.Text.Json;

namespace NaviDnD.Installer;

// Патч релиза — ZIP: patch.json (описание) и files/<путь> (новые и изменённые файлы).
//  BaseFiles — полный список файлов исходной версии с SHA256: по нему проверяется, что установлена именно она и
//  ничего не повреждено (иначе — полная установка базы); Files — новые/изменённые файлы с SHA256 результата;
//  Removed — удаляемые файлы исходной версии. Storage/, logs/ и помощник Updater/ патч не трогает никогда.
internal sealed record Patch(string BaseVersion, string Version, Dictionary<string, string> BaseFiles,
    Dictionary<string, string> Files, string[] Removed)
{
    internal const string ManifestName = "patch.json";
    internal const string FilesPrefix = "files/";

    internal static Patch Read(ZipArchive archive)
    {
        using var input = archive.GetEntry(ManifestName)?.Open() ?? throw new InvalidDataException("Нет описания патча.");
        Patch patch;
        try { patch = JsonSerializer.Deserialize<Patch>(input, UpdateManifest.Json) ?? throw new InvalidDataException("Некорректный патч."); }
        catch (JsonException) { throw new InvalidDataException("Некорректное описание патча."); }
        if (patch.BaseVersion == null || patch.Version == null
            || UpdateManifest.ParseVersion(patch.Version) <= UpdateManifest.ParseVersion(patch.BaseVersion))
            throw new InvalidDataException("Некорректная версия патча.");
        if (patch.BaseFiles == null || patch.Files == null || patch.Removed == null || !patch.Files.ContainsKey("release-version.txt"))
            throw new InvalidDataException("Неполное описание патча.");
        foreach (var file in patch.BaseFiles.Concat(patch.Files))
        {
            RelativePath(file.Key);
            if (file.Value is not { Length: 64 } || !file.Value.All(Uri.IsHexDigit)) throw new InvalidDataException("Некорректная сумма файла.");
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in patch.Files.Keys.Concat(patch.Removed))
            if (!names.Add(RelativePath(name))) throw new InvalidDataException("Повторяющийся путь патча.");
        foreach (string name in patch.Removed)
            if (!patch.BaseFiles.ContainsKey(name)) throw new InvalidDataException("Удаляемый файл отсутствует в базе.");
        foreach (string name in patch.Files.Keys)
            if (archive.GetEntry(FilesPrefix + name) == null) throw new InvalidDataException("В патче нет файла: " + name);
        return patch;
    }

    // Установлена ли исходная версия патча. full — все файлы базы (повреждённая установка), иначе — только те, что патч
    // заменяет или удаляет (следующие патчи цепочки: остальное уже проверено первым).
    internal void VerifyBase(string root, bool full, Action<double>? progress = null)
    {
        string versionFile = Path.Combine(root, "release-version.txt");
        string version = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "";
        if (version != BaseVersion) throw new InvalidDataException($"Патч для версии {BaseVersion}, установлена {(version.Length > 0 ? version : "неизвестная")}.");
        var check = full ? BaseFiles.ToList() : BaseFiles.Where(f => Files.ContainsKey(f.Key) || Removed.Contains(f.Key)).ToList();
        for (int i = 0; i < check.Count; i++)
        {
            var (name, sha) = (check[i].Key, check[i].Value);
            string path = Path.Combine(root, RelativePath(name));
            RejectLinks(root, path);
            if (!File.Exists(path) || UpdateManifest.Sha256(path) != sha.ToLowerInvariant())
                throw new InvalidDataException("Файл игры изменён или отсутствует: " + name);
            progress?.Invoke((i + 1) / (double)check.Count);
        }
    }

    internal static string RelativePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Contains('\0') ||
            name.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ')))
            throw new InvalidDataException("Недопустимый путь патча: " + name);
        string first = name.Split('/')[0];
        if (first.Equals("Storage", StringComparison.OrdinalIgnoreCase) || first.Equals("logs", StringComparison.OrdinalIgnoreCase)
            || first.Equals("Updater", StringComparison.OrdinalIgnoreCase) || first.StartsWith(".update-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Недопустимый путь патча: " + name);
        return name;
    }

    internal static void RejectLinks(string root, string target)
    {
        for (string? path = target; path != null && path.Length >= root.TrimEnd(Path.DirectorySeparatorChar).Length; path = Path.GetDirectoryName(path))
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Файл установки не должен быть ссылкой.");
    }
}

internal static class PatchInstaller
{
    // Патчи по порядку одной операцией: первый проверяет всю базу, каждый следующий — что начинается с версии
    // предыдущего. Файлы извлекаются и сверяются по SHA256 до замены; заменённые и удалённые уходят в резерв, при любой
    // ошибке всё возвращается как было. Storage/ и logs/ не затрагиваются. Возвращает установленную версию.
    internal static string ApplyChain(string root, IReadOnlyList<string> patchZips, Action<string, double?>? progress = null)
    {
        root = Path.GetFullPath(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Папка установки не должна быть ссылкой.");
        if (patchZips.Count == 0) throw new ArgumentException("Нет патчей.");
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
                if (version != null && patch.BaseVersion != version) throw new InvalidDataException("Разрыв в цепочке патчей.");
                progress?.Invoke($"Проверка файлов игры ({patch.BaseVersion})…", 0);
                patch.VerifyBase(root, full: i == 0, f => progress?.Invoke($"Проверка файлов игры ({patch.BaseVersion})…", f));

                string stage = Path.Combine(staging, i.ToString());
                int n = 0;
                foreach (var (name, sha) in patch.Files)
                {
                    string temp = SafePath(stage, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                    archive.GetEntry(Patch.FilesPrefix + name)!.ExtractToFile(temp);
                    if (UpdateManifest.Sha256(temp) != sha.ToLowerInvariant()) throw new InvalidDataException("Повреждён файл патча: " + name);
                    progress?.Invoke($"Обновление до {patch.Version}…", ++n / (double)(patch.Files.Count + patch.Removed.Length));
                }
                foreach (var name in patch.Files.Keys)
                {
                    string target = SafePath(root, name);
                    Patch.RejectLinks(root, target);
                    if (Directory.Exists(target)) throw new IOException("На месте файла папка: " + name);
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
                throw new IOException("Ошибка обновления и отката. Резервные файлы сохранены: " + backup,
                    new AggregateException(installError, rollbackError));
            }
            throw;
        }
        finally { if (!keepBackup && Directory.Exists(work)) Directory.Delete(work, true); }

        // Опустевшие после удаления папки — убрать (не выше папки игры).
        foreach (string dir in removedDirs.OrderByDescending(d => d.Length))
            for (string? path = dir; path != null && path.Length > root.Length; path = Path.GetDirectoryName(path))
            {
                try { if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); else break; }
                catch (IOException) { break; }
            }
        return version!;
    }

    private static string SafePath(string root, string relative)
    {
        string target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Некорректный путь в патче.");
        return target;
    }
}
