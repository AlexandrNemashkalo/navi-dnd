namespace NaviDnD.Helpers;

public static class Spinner
{
    public static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    // Waiting in the center of the row row (width width from column left): “⠋ Creating a world...”.
    public static async Task WhileCentered(Task task, string message, int row, int left, int width, List<int> color)
    {
        Term.CursorVisible = false;
        string text = $"{Frames[0]} {message}...";
        int x = left + Math.Max(0, (width - text.Length) / 2);
        for (int i = 0; !task.IsCompleted; i++)
        {
            ConsoleMouseReader.DrainWhileWaiting();
            Term.SetCursorPosition(x, row);
            ColorHelper.WriteColored($"{Frames[i % Frames.Length]} {message}...", color);
            await Task.Delay(100);
        }
    }

    public static async Task While(Task task, string? message = null)
    {
        message ??= L.T("The DM is thinking");
        Term.CursorVisible = false;
        int top  = Term.CursorTop;
        int i    = 0;

        while (!task.IsCompleted)
        {
            ConsoleMouseReader.DrainWhileWaiting();
            Term.SetCursorPosition(2, top);
            Console.Write($" {Frames[i % Frames.Length]} {message}...");
            i++;
            await Task.Delay(100);
        }

        Term.SetCursorPosition(0, top);
        Console.Write(new string(' ', message.Length + 9));
        Term.SetCursorPosition(0, top);
    }
}
