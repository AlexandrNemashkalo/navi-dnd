namespace NaviDnD.Clients;

public interface IAiProvider
{
    Task<string> Complete(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model);

    // Runs the request with streaming. Calls onChunk with each text chunk for live display.
    // Returns the full authoritative response (from the result event, not accumulated chunks).
    Task<string> CompleteWithStreaming(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model, Action<string>? onChunk);
}
