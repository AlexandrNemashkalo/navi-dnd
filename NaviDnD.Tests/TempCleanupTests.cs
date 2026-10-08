using NaviDnD.Helpers;

namespace NaviDnD.Tests;

// Очистка %TEMP% при запуске: только свои папки/файлы игры и только старые — свежие может использовать вторая копия
// игры или помощник обновления, чужие файлы не трогаются.
public class TempCleanupTests
{
    [Fact]
    public void SweepRemovesOnlyOldOwnedItems()
    {
        string temp = Path.Combine(Path.GetTempPath(), "NaviDnD-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string guid = new('a', 32);
            var old = DateTime.UtcNow.AddDays(-2);
            string update = Dir(temp, "NaviDnD-update-" + guid, old, "NaviDnD-Setup-win-x64.exe");
            string claude = Dir(temp, "navidnd-" + guid, old, ".claude/settings.json");
            string codex = Dir(temp, "navidnd-codex-" + guid, old, "last-message.txt");
            string speech = Path.Combine(temp, "NaviDnD-speech-" + guid + ".wav");
            File.WriteAllText(speech, "wav");
            File.SetLastWriteTimeUtc(speech, old);
            string fresh = Dir(temp, "NaviDnD-update-" + new string('b', 32), DateTime.UtcNow, "NaviDnD-updates.json");
            string foreign = Dir(temp, "navidnd-mine", old, "notes.txt");
            string log = Path.Combine(temp, "NaviDnD-2.0.3-push.log");
            File.WriteAllText(log, "log");
            File.SetLastWriteTimeUtc(log, old);

            Assert.Equal(4, TempCleanup.Sweep(temp, DateTime.UtcNow.AddDays(-1)));
            Assert.False(Directory.Exists(update));
            Assert.False(Directory.Exists(claude));
            Assert.False(Directory.Exists(codex));
            Assert.False(File.Exists(speech));
            Assert.True(Directory.Exists(fresh));
            Assert.True(Directory.Exists(foreign));
            Assert.True(File.Exists(log));
        }
        finally { Directory.Delete(temp, true); }
    }

    private static string Dir(string temp, string name, DateTime time, string file)
    {
        string dir = Path.Combine(temp, name);
        string path = Path.Combine(dir, file.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        Directory.SetLastWriteTimeUtc(dir, time);
        return dir;
    }
}
