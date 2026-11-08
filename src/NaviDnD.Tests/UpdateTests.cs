using System.IO.Compression;
using System.Reflection;
using NaviDnD;
using NaviDnD.Installer;

namespace NaviDnD.Tests;

public class UpdateTests
{
    [Fact]
    public async Task DownloadProgressCountsRealBytesAndKeepsFileContent()
    {
        byte[] data = Enumerable.Range(0, 200000).Select(i => (byte)i).ToArray();
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        var counts = new List<long>();
        var type = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.GameUpdates")!;
        Action<long, long?> progress = (bytes, total) => { Assert.Equal(data.Length, total); counts.Add(bytes); };
        await (Task)type.GetMethod("CopyWithProgressAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [input, output, (long?)data.Length, progress, CancellationToken.None])!;
        Assert.Equal(data, output.ToArray());
        Assert.True(counts.Count > 1);
        Assert.Equal(data.Length, counts.Last());
        Assert.Equal(counts.OrderBy(x => x), counts);
    }

    [Fact]
    public async Task CancelledDownloadDoesNotWriteMoreBytes()
    {
        using var input = new MemoryStream(new byte[200000]);
        using var output = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        var type = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.GameUpdates")!;
        Action<long, long?> progress = (_, _) => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => (Task)type.GetMethod("CopyWithProgressAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [input, output, (long?)input.Length, progress, cancellation.Token])!);
        Assert.InRange(output.Length, 1, input.Length - 1);
    }
    [Theory]
    [InlineData("../bad.exe")]
    [InlineData("Storage/settings.json")]
    [InlineData("NaviDnD-payload.001\ninvalid")]
    public void ChecksumManifestRejectsUnexpectedPaths(string filename)
    {
        var type = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.GameUpdates")!;
        var error = Assert.Throws<TargetInvocationException>(() => type.GetMethod("ParseChecksums", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [new string('a', 64) + "  " + filename]));
        Assert.IsType<InvalidDataException>(error.InnerException);
    }
    [Theory]
    [InlineData("v1.2.0", "1.1.0", true)]
    [InlineData("v1.1.0", "1.1.0", false)]
    [InlineData("v1.0.0", "1.1.0", false)]
    [InlineData("v1.2.0-beta", "1.1.0", false)]
    public void OnlyNewerStableVersionsAreOffered(string next, string current, bool expected)
    {
        var type = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.GameUpdates")!;
        Assert.Equal(expected, type.GetMethod("IsNewer", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [next, current]));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("Storage/settings.json")]
    [InlineData("logs/ai.log")]
    public void UnsafePayloadIsRejectedBeforeFilesChange(string entry)
    {
        WithFolders((root, parts) => {
            File.WriteAllText(Path.Combine(root, "NaviDnD.exe"), "old");
            Payload(parts, ("NaviDnD.exe", "new"), (entry, "bad"));
            Assert.Throws<InvalidDataException>(() => Installation.Apply(root, parts));
            Assert.Equal("old", File.ReadAllText(Path.Combine(root, "NaviDnD.exe")));
        });
    }

    [Fact]
    public void UpdatePreservesSavesAndSpeechModel()
    {
        WithFolders((root, parts) => {
            Directory.CreateDirectory(Path.Combine(root, "Storage", "Speech"));
            File.WriteAllText(Path.Combine(root, "Storage", "settings.json"), "private");
            File.WriteAllText(Path.Combine(root, "Storage", "Speech", "model.pt"), "model");
            File.WriteAllText(Path.Combine(root, "NaviDnD.exe"), "old");
            Payload(parts, ("NaviDnD.exe", "new"), ("Speech/worker.py", "worker"));
            Installation.Apply(root, parts);
            Assert.Equal("new", File.ReadAllText(Path.Combine(root, "NaviDnD.exe")));
            Assert.Equal("private", File.ReadAllText(Path.Combine(root, "Storage", "settings.json")));
            Assert.Equal("model", File.ReadAllText(Path.Combine(root, "Storage", "Speech", "model.pt")));
            Assert.Empty(Directory.GetDirectories(root, ".update-*"));
        });
    }

    [Fact]
    public void FailedReplacementRestoresPreviousFiles()
    {
        WithFolders((root, parts) => {
            File.WriteAllText(Path.Combine(root, "NaviDnD.exe"), "old");
            File.WriteAllText(Path.Combine(root, "blocked"), "keep");
            Payload(parts, ("NaviDnD.exe", "new"), ("blocked/file.txt", "new"));
            Assert.ThrowsAny<IOException>(() => Installation.Apply(root, parts));
            Assert.Equal("old", File.ReadAllText(Path.Combine(root, "NaviDnD.exe")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(root, "blocked")));
        });
    }

    private static void Payload(string parts, params (string Name, string Text)[] files)
    {
        using var archive = ZipFile.Open(Path.Combine(parts, "NaviDnD-payload.001"), ZipArchiveMode.Create);
        foreach (var file in files) { using var writer = new StreamWriter(archive.CreateEntry(file.Name).Open()); writer.Write(file.Text); }
    }
    private static void WithFolders(Action<string, string> action)
    {
        string temp = Path.Combine(Path.GetTempPath(), "NaviDnD-update-test-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(temp, "game"), parts = Path.Combine(temp, "parts");
        Directory.CreateDirectory(root); Directory.CreateDirectory(parts);
        try { action(root, parts); } finally { Directory.Delete(temp, true); }
    }
}
