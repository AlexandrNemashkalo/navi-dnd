using System.Text.RegularExpressions;

namespace NaviDnD.Helpers;

// Временные папки игры в %TEMP%, которые не удалились сразу: копия установщика после обновления (помощник запущен из
// неё и сам себя удалить не может), прерванная загрузка, рабочая папка Claude/Codex CLI, которую держал зависший
// MCP-сервер. При запуске удаляются только старше суток: свежие может использовать вторая копия игры или помощник.
internal static class TempCleanup
{
    private static readonly Regex Owned = new(
        @"^(?:NaviDnD-update-|NaviDnD-patches-|navidnd-|navidnd-codex-)[0-9a-f]{32}$|^NaviDnD-speech-[0-9a-f]{32}\.wav$",
        RegexOptions.CultureInvariant);

    internal static void Run() => Task.Run(() => Sweep(Path.GetTempPath(), DateTime.UtcNow.AddDays(-1)));

    internal static int Sweep(string temp, DateTime olderThanUtc)
    {
        int removed = 0;
        try
        {
            foreach (var item in new DirectoryInfo(temp).EnumerateFileSystemInfos("*avi*"))
            {
                if (!Owned.IsMatch(item.Name) || item.LastWriteTimeUtc > olderThanUtc
                    || (item.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                try
                {
                    if (item is DirectoryInfo directory) directory.Delete(recursive: true);
                    else item.Delete();
                    removed++;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }   // занято — в следующий раз
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return removed;
    }
}
