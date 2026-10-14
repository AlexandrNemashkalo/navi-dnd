namespace NaviDnD.Clients;

// AI from the settings (AppConfig.AiProvider) for each request: “AI” in the settings changes immediately,
// without restarting the game.
public class SwitchableAiProvider(AppConfig config, AiLogger? logger = null) : IAiProvider
{
    private readonly ClaudeCliAiProvider _claude = new(config, logger);
    private readonly CodexCliAiProvider _codex = new(config, logger);

    private IAiProvider Current => config.AiProvider == "codex" ? _codex : _claude;

    public Task<string> Complete(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model) =>
        Current.Complete(systemBlocks, userMessage, actionPath, model);

    public Task<string> CompleteWithStreaming(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath,
        string model, Action<string>? onChunk) =>
        Current.CompleteWithStreaming(systemBlocks, userMessage, actionPath, model, onChunk);
}
