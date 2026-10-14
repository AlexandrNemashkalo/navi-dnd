using NaviDnD.Data;

namespace NaviDnD.UiTests;

// Common screen tests reused between test cases (GameScenarioTests, NewGameScenarioTests, ...) - frames and
// tabs are the same regardless of scenario.
internal static class ScreenAssertions
{
    private static readonly char[] LeftBorderChars = ['╭', '╰', '├', '│'];
    private static readonly char[] RightBorderChars = ['╮', '╯', '┤', '│'];

    // Frame: where the line actually contains something (not the empty tail of the buffer), there should be
    // frame symbols on the strictly expected columns on the left and right, and not overflowing/overflowing
    // content.
    public static void CheckBorders(List<string> rows, string context, List<string> failures)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            string line = rows[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            // DisplayConfig.LeftMargin (=1) - the column before the frame itself
            // (BorderDrawer.WriteLeftMargin), the frame symbol is located IMMEDIATELY AFTER the indent, not in
            // column 0.
            if (line.Length <= DisplayConfig.LeftMargin) continue;
            char left = line[DisplayConfig.LeftMargin];
            if (!LeftBorderChars.Contains(left))
                failures.Add($"[{context}] Строка {i}: слева (колонка {DisplayConfig.LeftMargin}) нет символа рамки (там '{left}'): «{line.TrimEnd()}»");

            if (line.Length > GameConsole.RightBorderCol)
            {
                char right = line[GameConsole.RightBorderCol];
                if (!RightBorderChars.Contains(right) && !char.IsWhiteSpace(right))
                    failures.Add($"[{context}] Строка {i}: справа (колонка {GameConsole.RightBorderCol}) нет символа рамки (там '{right}') — контент мог переполнить карточку: «{line.TrimEnd()}»");
            }
        }
    }

    // Checks that the declared text (F1/F2/... + label) is actually in the title - and the label comes
    // immediately after the prefix, that is, the button has not been renamed or disappeared.
    public static void RequireTab(string title, string prefix, string expectedLabel, string context, List<string> failures)
    {
        int idx = title.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0)
        {
            failures.Add($"[{context}] Кнопка «{prefix}» не найдена в заголовке: «{title.TrimEnd()}»");
            return;
        }
        int labelStart = idx + prefix.Length;
        string rest = title[labelStart..];
        if (!rest.TrimStart().StartsWith(expectedLabel, StringComparison.Ordinal))
            failures.Add($"[{context}] После «{prefix}» ожидалась метка «{expectedLabel}», заголовок: «{title.TrimEnd()}»");
    }

    // +2: DisplayConfig.LeftMargin(1) + frame(1) - mirrors MouseUiHelper.ComputeTitleTabs.
    public static (int startX, int endX)? FindTab(string title, string prefix)
    {
        int idx = title.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return null;
        int end = idx + prefix.Length;
        while (end < title.Length && title[end] != ' ') end++;
        return (2 + idx + prefix.Length, 2 + end); // only the name is clickable, without “[Fn]”
    }

    // Real text color check via a screenshot of the client area of the window (ScreenCapture.SampleCellColor) -
    // legacy console attributes (ReadConsoleOutputAttribute) does not reflect the 24-bit ANSI that the game
    // uses, so the only reliable way is to remove a pixel and compare RGB with the tolerance (font
    // antialiasing/scaling may shift the value slightly).
    public static void CheckTextColor(
        int gamePid, List<string> rows, string searchText, (int r, int g, int b) expected, int tolerance,
        (byte r, byte g, byte b) background, string context, List<string> failures)
    {
        int rowIndex = rows.FindIndex(r => r.Contains(searchText));
        if (rowIndex < 0) return; // the very fact of missing text is already reported by a separate check

        int col = rows[rowIndex].IndexOf(searchText, StringComparison.Ordinal);
        if (col < 0) return;

        (byte r, byte g, byte b) sample;
        try { sample = GameConsole.SampleCellColor(gamePid, col + 2, rowIndex, background); }
        catch (Exception ex)
        {
            failures.Add($"[{context}] Не удалось снять пиксель для проверки цвета «{searchText}»: {ex.Message}");
            return;
        }

        var (r, g, b) = sample;
        bool close = Math.Abs(r - expected.r) <= tolerance
                  && Math.Abs(g - expected.g) <= tolerance
                  && Math.Abs(b - expected.b) <= tolerance;
        if (!close)
            failures.Add($"[{context}] Цвет «{searchText}»: ожидался ~({expected.r},{expected.g},{expected.b}), получен ({r},{g},{b}) — допуск ±{tolerance}.");
    }

    // Like CheckTextColor, but for a single character in a known (col, row) cell) - without +2 shift (that
    // shift is needed only to get INSIDE a multi-character word, and not on its edge). Used for action/speed
    // symbols in the legend (● ■ ▲), which differ only in color, not text - search for them by substring not
    // enough.
    public static void CheckCellColor(
        int gamePid, int col, int row, (int r, int g, int b) expected, int tolerance,
        (byte r, byte g, byte b) background, string context, List<string> failures)
    {
        (byte r, byte g, byte b) sample;
        try { sample = GameConsole.SampleCellColor(gamePid, col, row, background); }
        catch (Exception ex)
        {
            failures.Add($"[{context}] Не удалось снять пиксель ({col},{row}): {ex.Message}");
            return;
        }

        var (r, g, b) = sample;
        bool close = Math.Abs(r - expected.r) <= tolerance
                  && Math.Abs(g - expected.g) <= tolerance
                  && Math.Abs(b - expected.b) <= tolerance;
        if (!close)
            failures.Add($"[{context}] Цвет ячейки ({col},{row}): ожидался ~({expected.r},{expected.g},{expected.b}), получен ({r},{g},{b}) — допуск ±{tolerance}.");
    }
}
