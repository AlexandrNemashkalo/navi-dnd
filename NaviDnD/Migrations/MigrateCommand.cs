using System.Text;

namespace NaviDnD.Migrations;

// Запуск миграции и отчёт: лог — logs/migrations.log; ошибки — ещё и в Storage/update-error.txt (игра покажет их
// в «ОБНОВЛЕНИЯ», как и прочие ошибки обновления).
public static class MigrateCommand
{
    // «NaviDnD.exe --migrate <папка игры>» — из помощника обновления, без окна. 0 — всё переведено (или нечего).
    public static int Run(string root)
    {
        try
        {
            var result = StorageMigration.Run(Path.Combine(root, "Storage"));
            Report(root, result);
            return result.Ok ? 0 : 1;
        }
        catch (Exception e)
        {
            Log(root, "Сбой миграции: " + e);
            Error(root, L.T("Не удалось обновить сохранения: ") + e.Message);
            return 1;
        }
    }

    // Запасной путь при старте игры: файлы всё ещё старого формата (сборка из исходников, сбой обновления).
    public static void RunIfNeeded(string root)
    {
        try
        {
            if (StorageMigration.Needed(Path.Combine(root, "Storage"))) Run(root);
        }
        catch { /* не помешать запуску игры */ }
    }

    private static void Report(string root, StorageMigration.Result result)
    {
        if (result.Migrated == 0 && result.Ok) return;
        Log(root, $"Формат {StorageFormat.Current}: переведено файлов {result.Migrated}, копия — {result.BackupDir ?? "нет"}"
                  + string.Concat(result.Errors.Select(e => "\n  ошибка: " + e)));
        if (!result.Ok)
            Error(root, L.F("Не все сохранения обновлены ({0}). Прежние файлы — в {1}.", string.Join("; ", result.Errors),
                result.BackupDir ?? "Storage"));
    }

    private static void Log(string root, string text)
    {
        try
        {
            string dir = Path.Combine(root, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "migrations.log"), $"{DateTime.Now:O} {text}\n", new UTF8Encoding(false));
        }
        catch { }
    }

    private static void Error(string root, string text)
    {
        try
        {
            string dir = Path.Combine(root, "Storage");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "update-error.txt"), text + "\n", new UTF8Encoding(false));
        }
        catch { }
    }
}
