using System.Text;

namespace NaviDnD.Migrations;

// Start migration and report: log - logs/migrations.log; errors - also in Storage/update-error.txt (the game
// will show them in “UPDATES”, like other update errors).
public static class MigrateCommand
{
    // “NaviDnD.exe --migrate <game folder>” - from the update assistant, without a window. 0 - everything has been translated (or nothing).
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
            Error(root, L.T("Could not update the saves: ") + e.Message);
            return 1;
        }
    }

    // Fallback path when starting the game: files are still in the old format (build from source, update failed).
    public static void RunIfNeeded(string root)
    {
        try
        {
            if (StorageMigration.Needed(Path.Combine(root, "Storage"))) Run(root);
        }
        catch { /*do not prevent the game from starting*/ }
    }

    private static void Report(string root, StorageMigration.Result result)
    {
        if (result.Migrated == 0 && result.Ok) return;
        Log(root, $"Формат {StorageFormat.Current}: переведено файлов {result.Migrated}, копия — {result.BackupDir ?? "нет"}"
                  + string.Concat(result.Errors.Select(e => "\n  ошибка: " + e)));
        if (!result.Ok)
            Error(root, L.F("Not all saves were updated ({0}). The previous files are in {1}.", string.Join("; ", result.Errors),
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
