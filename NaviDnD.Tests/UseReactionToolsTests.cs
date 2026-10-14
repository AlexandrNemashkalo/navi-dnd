using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

// Real log: use_reaction was called with an empty history/question ("" [free-text]) - the dialogue would still
// open to the player and wait 50s for a response to nothing. We check the failure WITHOUT a real wait (valid
// path is blocked until 50s/file response - not for unit test).
public class UseReactionToolsTests : IDisposable
{
    private readonly List<string> _files = [];

    // State files and everything the tool puts next to them (navidnd_test_<guid>*) - after each test.
    public void Dispose()
    {
        foreach (string path in _files)
            foreach (string file in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "*"))
                try { File.Delete(file); } catch (IOException) { }
    }

    private UseReactionTools CreateTools()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"navidnd_test_{Guid.NewGuid():N}.json");
        _files.Add(tempPath);
        return new UseReactionTools(new McpServerConfig(tempPath, LogPath: null));
    }

    [Fact]
    public void UseReaction_EmptyHistory_ReturnsErrorImmediately()
    {
        var tools = CreateTools();
        string result = tools.UseReaction(history: [], options: null);
        var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void UseReaction_BlankLastEntryText_ReturnsErrorImmediately()
    {
        var tools = CreateTools();
        var history = new[] { new RequestHistoryEntry { Text = "   " } };
        string result = tools.UseReaction(history, options: null);
        var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }
}
