using System.Text;

namespace NaviDnD.McpServer;

internal static class SearchToolLog
{
    private static readonly object Gate = new();

    internal static void Write(string? path, string message)
    {
        if (path == null) return;
        lock (Gate) File.AppendAllText(path, $"  MCP {message}\n", new UTF8Encoding(false));
    }
}
