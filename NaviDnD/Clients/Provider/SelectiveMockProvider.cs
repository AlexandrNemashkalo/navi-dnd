using System.Text;

namespace NaviDnD.Clients;

// Wraps a real provider and intercepts requests for actions listed in mockedActions.
// If response.json exists next to the system prompt, returns it instead of calling the real provider.
// Use ["*"] to mock all actions, or specific names like ["SendAction", "CreateNewGame"].
// mockResponsesDirOverride (опционально) — читать response.json из ДРУГОЙ базовой папки вместо
// actionPath (реальные Prompts/{RuleSet}/{Action}/) — используется E2E-тестами, чтобы держать
// свои моки отдельно от git-tracked промптов (см. NaviDnD.UiTests/GameSession.cs).
internal sealed class SelectiveMockProvider(IAiProvider inner, IReadOnlySet<string> mockedActions, string? mockResponsesDirOverride = null) : IAiProvider
{
    private const int ChunkSize = 8;
    private const int DelayMs = 10;

    // Сколько раз уже вызывали мок для каждого имени действия — нужно, чтобы РАЗЛИЧАТЬ
    // последовательные вызовы ОДНОГО и того же действия в рамках одного теста. Например,
    // FireEnemyTurn использует тот же actionPath ("SendAction"), что и обычное действие игрока —
    // без нумерации оба вызова получили бы ОДИН и тот же файл ответа, из-за чего в диалоге
    // дублировался бы один и тот же текст (см. EnemyTurnBehaviorScenarioTests).
    private readonly Dictionary<string, int> _callCounts = [];

    // Мок отвечает не мгновенно — имитирует реальную задержку сети/модели, чтобы UI ожидания
    // (Spinner.While — "Создаём персонажа...", "Создаём мир..." и т.п.) реально успевал
    // отрисоваться и его можно было проверить в E2E-тестах, а не просто предполагать, что он есть.
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

        // 1й вызов — всегда response.json (совместимо со всеми существующими тестами). 2й и
        // далее — сперва пробуем responseN.json (различимый ответ для повторного вызова того же
        // действия, например обычный SendAction игрока → FireEnemyTurn), и только если такого
        // файла нет — молча повторяем response.json (прежнее поведение для тестов, где второй
        // вызов не нужно отличать от первого).
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
