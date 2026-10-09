namespace NaviDnD.Helpers;

public static class Spinner
{
    public static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    // Ожидание по центру строки row (ширина width от колонки left): «⠋ Создаём мир...».
    public static async Task WhileCentered(Task task, string message, int row, int left, int width, List<int> color)
    {
        Console.CursorVisible = false;
        string text = $"{Frames[0]} {message}...";
        int x = left + Math.Max(0, (width - text.Length) / 2);
        for (int i = 0; !task.IsCompleted; i++)
        {
            ConsoleMouseReader.DrainWhileWaiting();
            Console.SetCursorPosition(x, row);
            ColorHelper.WriteColored($"{Frames[i % Frames.Length]} {message}...", color);
            await Task.Delay(100);
        }
    }

    public static async Task While(Task task, string? message = null)
    {
        message ??= L.T("Мастер думает");
        Console.CursorVisible = false;
        int top  = Console.CursorTop;
        int i    = 0;

        while (!task.IsCompleted)
        {
            ConsoleMouseReader.DrainWhileWaiting();
            Console.SetCursorPosition(2, top);
            Console.Write($" {Frames[i % Frames.Length]} {message}...");
            i++;
            await Task.Delay(100);
        }

        Console.SetCursorPosition(0, top);
        Console.Write(new string(' ', message.Length + 9));
        Console.SetCursorPosition(0, top);
    }
}
