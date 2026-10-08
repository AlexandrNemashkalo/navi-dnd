using NaviDnD.Data;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

internal sealed class UpdateDisplay(WorldState settings, DisplayConfig display)
{
    private readonly object gate = new();
    private string status = L.T("Проверка новой версии…");
    private double? fraction;
    private string detail = L.T("Esc — отменить");
    private string? rendered;
    private int statusRow, width;

    internal void Open()
    {
        width = display.InnerWidth(display.ViewCols(settings.Map));
        new MainMenuDisplay(settings, display).DrawForUpdate();
        statusRow = Console.WindowHeight - 3;
        Render();
    }

    internal void Set(string message, double? progress = null, string? hint = null)
    {
        lock (gate) { status = message; fraction = progress; detail = hint ?? L.T("Esc — отменить"); }
    }

    private void Render()
    {
        string text, hint; double? value;
        lock (gate) { text = status; hint = detail; value = fraction; }
        string snapshot = text + hint + value;
        if (rendered == snapshot) return;
        rendered = snapshot;
        int barWidth = Math.Clamp(width - 20, 10, 50);
        double percent = Math.Clamp(value ?? 0, 0, 1);
        string bar = value == null ? "" : "[" + new string('█', (int)(barWidth * percent))
            + new string('░', barWidth - (int)(barWidth * percent)) + $"] {percent:P0}";
        Write(statusRow - 2, text); Write(statusRow - 1, bar); Write(statusRow, hint);
    }

    private void Write(int row, string text)
    {
        if (text.Length > width - 2) text = text[..(width - 3)] + "…";
        text = text.PadLeft((width + text.Length) / 2).PadRight(width);
        Console.SetCursorPosition(DisplayConfig.LeftMargin + 1, row);
        ColorHelper.WriteColored(text, display.MainForeground, display.MainBackground);
    }

    internal async Task<T> RunAsync<T>(Task<T> task, CancellationTokenSource cancellation)
    {
        while (!task.IsCompleted)
        {
            Render();
            ConsoleMouseReader.DrainMouseEvents();
            if (ConsoleMouseReader.TryReadKey()?.Key == ConsoleKey.Escape)
            {
                cancellation.Cancel();
                Set(L.T("Отмена загрузки…"), hint: "");
            }
            await Task.Delay(50);
        }
        Render();
        return await task;
    }

    internal async Task<bool> ConfirmAsync(string text, bool confirm)
    {
        Set(text, hint: confirm ? L.T("Enter — обновить и перезапустить    Esc — меню") : L.T("Enter / Esc — меню"));
        while (true)
        {
            Render();
            ConsoleMouseReader.DrainMouseEvents();
            var key = ConsoleMouseReader.TryReadKey()?.Key;
            if (key == ConsoleKey.Enter) return confirm;
            if (key == ConsoleKey.Escape) return false;
            await Task.Delay(50);
        }
    }
}
