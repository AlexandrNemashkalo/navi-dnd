using NaviDnD.Data;

namespace NaviDnD.UiTests;

// Общие проверки экрана, переиспользуемые между тест-кейсами (GameScenarioTests,
// NewGameScenarioTests, ...) — рамки и вкладки одинаковы независимо от сценария.
internal static class ScreenAssertions
{
    private static readonly char[] LeftBorderChars = ['╭', '╰', '├', '│'];
    private static readonly char[] RightBorderChars = ['╮', '╯', '┤', '│'];

    // Рамка: там, где строка реально что-то содержит (не пустой хвост буфера),
    // слева и справа должны быть символы рамки на строго ожидаемых колонках,
    // а не съехавший/переполненный контент.
    public static void CheckBorders(List<string> rows, string context, List<string> failures)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            string line = rows[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            // DisplayConfig.LeftMargin (=1) — колонка перед самой рамкой (BorderDrawer.WriteLeftMargin),
            // символ рамки стоит СРАЗУ ПОСЛЕ отступа, не в колонке 0.
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

    // Проверяет, что заявленный текст (F1/F2/... + метка) реально есть в заголовке —
    // и метка идёт сразу после префикса, то есть кнопка не переименована и не пропала.
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

    // +2: DisplayConfig.LeftMargin(1) + рамка(1) — зеркалит MouseUiHelper.ComputeTitleTabs.
    public static (int startX, int endX)? FindTab(string title, string prefix)
    {
        int idx = title.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return null;
        int end = idx + prefix.Length;
        while (end < title.Length && title[end] != ' ') end++;
        return (2 + idx + prefix.Length, 2 + end); // кликабельно только название, без «[Fn]»
    }

    // Реальная проверка цвета текста через скриншот клиентской области окна
    // (ScreenCapture.SampleCellColor) — легаси-атрибуты консоли (ReadConsoleOutputAttribute)
    // не отражают 24-битный ANSI, которым красит игра, поэтому единственный надёжный способ —
    // снять пиксель и сравнить RGB с допуском (антиалиасинг шрифта/масштабирование могут
    // слегка сдвинуть значение).
    public static void CheckTextColor(
        int gamePid, List<string> rows, string searchText, (int r, int g, int b) expected, int tolerance,
        (byte r, byte g, byte b) background, string context, List<string> failures)
    {
        int rowIndex = rows.FindIndex(r => r.Contains(searchText));
        if (rowIndex < 0) return; // сам факт отсутствия текста уже сообщён отдельной проверкой

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

    // Как CheckTextColor, но для одиночного символа в известной (col, row) ячейке — без сдвига +2
    // (тот сдвиг нужен только чтобы попасть ВНУТРЬ многосимвольного слова, а не на его край).
    // Используется для символов действий/скорости в легенде (● ■ ▲), которые различаются только
    // цветом, а не текстом — искать их по подстроке недостаточно.
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
