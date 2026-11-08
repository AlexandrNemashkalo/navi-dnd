using System.Text.RegularExpressions;

namespace NaviDnD.Helpers;

// Temporary game folders in %TEMP% that were not deleted immediately: a copy of the installer after the update
// (the assistant was launched from it and cannot delete itself), an interrupted download, the working folder
// Claude/Codex CLI, which was kept by the frozen MCP- server. At startup, only those older than a day are
// deleted: fresh ones can be used by a second copy of the game or an assistant.
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
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }   // busy - next time
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return removed;
    }
}
