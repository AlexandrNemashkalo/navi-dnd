using System.Text;

namespace NaviDnD.Tests;

public class AtomicFileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "navidnd-atomic-" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedWriteNeverReplacesPreviousSave(bool existing)
    {
        string path = Path.Combine(directory, "game.json");
        const string previous = "{\"hero\":\"Мира\",\"progress\":42}";
        if (existing) File.WriteAllText(path, previous);
        Assert.Throws<IOException>(() => AtomicFile.Write(path, stream =>
        {
            stream.Write(Encoding.UTF8.GetBytes("{\"hero\":"));
            throw new IOException("Simulated disk failure after a partial write");
        }));
        Assert.Equal(existing, File.Exists(path));
        if (existing) Assert.Equal(previous, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        AtomicFile.WriteAllText(path, "{\"hero\":\"Мира\",\"progress\":43}");
        Assert.Contains("\"progress\":43", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void FailedReplacementRemovesOnlyTheTemporaryFile()
    {
        string path = Path.Combine(directory, "blocked.json");
        Directory.CreateDirectory(path);
        string marker = Path.Combine(path, "keep.txt");
        File.WriteAllText(marker, "Preserve existing data");
        try
        {
            var error = Record.Exception(() => AtomicFile.WriteAllText(path, "new data"));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.Equal("Preserve existing data", File.ReadAllText(marker));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { File.Delete(marker); Directory.Delete(path); }
    }

    public void Dispose()
    {
        foreach (string path in Directory.GetFiles(directory)) File.Delete(path);
        Directory.Delete(directory);
    }
}
