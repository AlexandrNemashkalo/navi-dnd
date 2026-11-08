using System.Text.Json;

namespace NaviDnD;

public class RequestHistoryEntry
{
    public string Text { get; set; } = "";
    public string? Author { get; set; }
    public JsonElement? Patch { get; set; }
}

public class AskPlayerRequest
{
    public List<RequestHistoryEntry> History { get; set; } = [];
    public string[]? Options { get; set; }
    public bool Answered { get; set; }
    public string? Answer { get; set; }
}
