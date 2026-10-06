using NaviDnD.Data;
using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: с пустой историей (DialogDisplay.Draw) диалоговый блок раньше схлопывался до заголовка +
// разделителя без единой строки контента — вместо фиксированной высоты в MaxHistoryLines строк,
// как при непустой истории. Fixtures/effects_game.json задаёт "history": [] (и полный hero, включая
// equipmentSlots, — нужен для безопасного рендера экрана Персонажа).
public class EmptyDialogScenarioTests
{
    private readonly ITestOutputHelper _output;

    public EmptyDialogScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void DialogBlock_WithEmptyHistory_StillRendersFixedHeight()
    {
        using var game = GameSession.Launch(fixtureFileName: "effects_game.json");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.ToCharacterScreen(game.Pid);
        _output.WriteLine("Экран Персонажа отрисован.");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "ДИАЛОГОВОЕ ОКНО", 5000),
            "Заголовок «ДИАЛОГОВОЕ ОКНО» не найден на экране.");

        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        var failures = new List<string>();

        int titleIdx = rows.FindIndex(r => r.Contains("ДИАЛОГОВОЕ ОКНО"));
        Assert.True(titleIdx >= 0); // уже проверено WaitForAnyRowContains выше, но нужен индекс

        int maxHistoryLines = new DisplayConfig().MaxHistoryLines;

        _output.WriteLine($"Заголовок диалога — строка {titleIdx}. Проверяю {maxHistoryLines} строк контента под ним, несмотря на пустую историю...");

        int topSeparatorIdx = titleIdx + 1;
        if (topSeparatorIdx >= rows.Count || !rows[topSeparatorIdx].TrimStart().StartsWith('├'))
            failures.Add($"Строка {topSeparatorIdx} (сразу под заголовком) — не разделитель «├...»: «{rows.ElementAtOrDefault(topSeparatorIdx)?.TrimEnd()}»");

        for (int i = 0; i < maxHistoryLines; i++)
        {
            int rowIdx = topSeparatorIdx + 1 + i;
            if (rowIdx >= rows.Count || !rows[rowIdx].TrimStart().StartsWith('│'))
            {
                failures.Add($"Строка {rowIdx} (контент диалога, {i + 1}-я из {maxHistoryLines}) — не строка контента «│...»: «{rows.ElementAtOrDefault(rowIdx)?.TrimEnd()}». Блок схлопнулся раньше фиксированной высоты.");
                break;
            }
        }

        int bottomSeparatorIdx = topSeparatorIdx + 1 + maxHistoryLines;
        if (bottomSeparatorIdx >= rows.Count || !rows[bottomSeparatorIdx].TrimStart().StartsWith('├'))
            failures.Add($"Строка {bottomSeparatorIdx} (нижняя граница блока после {maxHistoryLines} строк контента) — не разделитель «├...»: «{rows.ElementAtOrDefault(bottomSeparatorIdx)?.TrimEnd()}»");

        TestPacing.Step();
        _output.WriteLine(failures.Count == 0 ? "Все проверки пройдены." : $"Провалено проверок: {failures.Count}.");
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }
}
