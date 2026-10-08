using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

// Реальный лог: use_reaction был вызван с пустой history/вопросом ("" [free-text]) — диалог всё
// равно открылся бы игроку и ждал 50с ответа ни на что. Проверяем отказ БЕЗ реального ожидания
// (валидный путь блокируется до 50с/ответа файла — не для юнит-теста).
public class UseReactionToolsTests : IDisposable
{
    private readonly List<string> _files = [];

    // Файлы состояния и всё, что инструмент кладёт рядом с ними (navidnd_test_<guid>*), — после каждого теста.
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
