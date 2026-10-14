using System.Text;

namespace NaviDnD.Clients;

// Wraps a real provider and intercepts requests for actions listed in mockedActions. If response.json exists
// next to the system prompt, returns it instead of calling the real provider. Use ["*"] to mock all actions, or
// specific names like ["SendAction", "CreateNewGame"]. mockResponsesDirOverride (optional) - read response.json
// from ANOTHER base folder instead of actionPath (real Prompts/{RuleSet}/{Action}/) - used by E2E- tests to
// keep their mocks separate from git-tracked prompts (see NaviDnD.UiTests/GameSession.cs).
internal sealed class SelectiveMockProvider(IAiProvider inner, IReadOnlySet<string> mockedActions, string? mockResponsesDirOverride = null) : IAiProvider
{
    private const int ChunkSize = 8;
    private const int DelayMs = 10;

    // The number of times a mock has already been called for each action name is necessary to distinguish
    // between successive calls of the same action within the same test. For example, FireEnemyTurn uses the
    // same actionPath ("SendAction") as a normal player action - without numbering, both calls would receive
    // the SAME response file, causing the same text to be duplicated in the dialogue (see
    // EnemyTurnBehaviorScenarioTests).
    private readonly Dictionary<string, int> _callCounts = [];

    // The mock does not respond instantly - it simulates the real network/model delay so that UI expectations
    // (Spinner.While - “Creating a character...”, “Creating a world...”, etc.) actually have time to be
    // rendered and it can be checked in E2E- tests, and not just assume that it exists.
    private const int MockResponseDelayMs = 5000;

    public async Task<string> Complete(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model)
    {
        if (TryGetMock(actionPath, out string mock))
        {
            await Task.Delay(MockResponseDelayMs);
            return mock;
        }
        return await inner.Complete(systemBlocks, userMessage, actionPath, model);
    }

    public async Task<string> CompleteWithStreaming(
        IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model,
        Action<string>? onChunk)
    {
        if (TryGetMock(actionPath, out string mock))
        {
            await Task.Delay(MockResponseDelayMs);
            if (onChunk != null)
                for (int i = 0; i < mock.Length; i += ChunkSize)
                {
                    onChunk(mock.Substring(i, Math.Min(ChunkSize, mock.Length - i)));
                    await Task.Delay(DelayMs);
                }
            return mock;
        }
        return await inner.CompleteWithStreaming(systemBlocks, userMessage, actionPath, model, onChunk);
    }

    private bool TryGetMock(string actionPath, out string content)
    {
        string actionName = Path.GetFileName(actionPath);
        if (!mockedActions.Contains("*") && !mockedActions.Contains(actionName))
        {
            content = "";
            return false;
        }

        int callNumber = _callCounts[actionName] = _callCounts.GetValueOrDefault(actionName) + 1;
        string baseDir = mockResponsesDirOverride != null
            ? Path.Combine(mockResponsesDirOverride, actionName)
            : actionPath;

        // 1st call is always response.json (compatible with all existing tests). 2nd and further - first we try
        // responseN.json (a distinguishable response to call the same action again, for example the player's
        // usual SendAction → FireEnemyTurn), and only if there is no such file - we silently repeat
        // response.json (the previous behavior for tests where the second call does not need to be
        // distinguished from the first).
        string stubPath = callNumber > 1 && File.Exists(Path.Combine(baseDir, $"response{callNumber}.json"))
            ? Path.Combine(baseDir, $"response{callNumber}.json")
            : Path.Combine(baseDir, "response.json");

        if (!File.Exists(stubPath))
        {
            content = "";
            return false;
        }

        content = File.ReadAllText(stubPath, Encoding.UTF8);
        return true;
    }
}
