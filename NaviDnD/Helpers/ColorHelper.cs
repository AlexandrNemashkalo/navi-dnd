namespace NaviDnD.Helpers;

public static class ColorHelper
{
    private static List<int> _currentForeground;
    private static List<int> _currentBackground;

    // Очистить экран фоном bg: Console.Clear заливает текущим ANSI-фоном — если до этого строка карты мира оставила
    // свой фон (вода), им залилась бы и строка под рамкой, которую потом никто не перерисовывает.
    public static void ClearScreen(List<int> bg, List<int> fg)
    {
        SetBackgroundColor(bg);
        SetForegroundColor(fg);
        Console.Clear();
    }

    public static void SetBackgroundColor(List<int> rgb)
    {
        if (rgb != null && rgb.Count == 3)
        {
            _currentBackground = rgb;
            Console.Write($"\x1b[48;2;{rgb[0]};{rgb[1]};{rgb[2]}m");
        }
    }

    public static void SetForegroundColor(List<int> rgb)
    {
        if (rgb != null && rgb.Count == 3)
        {
            _currentForeground = rgb;
            Console.Write($"\x1b[38;2;{rgb[0]};{rgb[1]};{rgb[2]}m");
        }
    }

    public static void WriteColored(string text, List<int> fgColor = null, List<int> bgColor = null)
    {
        if (fgColor == null && bgColor == null)
        {
            Console.Write(text);
            return;
        }

        var oldFg = _currentForeground;
        var oldBg = _currentBackground;

        if (bgColor != null && bgColor.Count == 3)
        {
            Console.Write($"\x1b[48;2;{bgColor[0]};{bgColor[1]};{bgColor[2]}m");
        }

        if (fgColor != null && fgColor.Count == 3)
        {
            Console.Write($"\x1b[38;2;{fgColor[0]};{fgColor[1]};{fgColor[2]}m");
        }

        Console.Write(text);

        if (bgColor != null)
        {
            if (oldBg != null)
                Console.Write($"\x1b[48;2;{oldBg[0]};{oldBg[1]};{oldBg[2]}m");
            else
                Console.Write("\x1b[49m");
        }

        if (fgColor != null)
        {
            if (oldFg != null)
                Console.Write($"\x1b[38;2;{oldFg[0]};{oldFg[1]};{oldFg[2]}m");
            else
                Console.Write("\x1b[39m");
        }
    }

    public static void WriteColored(string text, List<int> fgColor) => WriteColored(text, fgColor, null);

    public static List<int> Darker(List<int> color, double factor = 0.7)
    {
        if (color == null || color.Count != 3) return color;
        return new List<int>
        {
            (int)(color[0] * factor),
            (int)(color[1] * factor),
            (int)(color[2] * factor)
        };
    }

    /// <summary>Смешивает цвет с белым, сохраняя оттенок. factor — доля белого (0..1).</summary>
    public static List<int> Pale(List<int> color, double whiteMix = 0.15)
    {
        if (color == null || color.Count != 3) return color;
        return
        [
            (int)(color[0] + (255 - color[0]) * whiteMix),
            (int)(color[1] + (255 - color[1]) * whiteMix),
            (int)(color[2] + (255 - color[2]) * whiteMix)
        ];
    }

    public static List<int> MixWith(List<int> color, List<int> other, double factor)
    {
        if (color == null || color.Count != 3 || other == null || other.Count != 3) return color;
        return
        [
            (int)(color[0] * (1 - factor) + other[0] * factor),
            (int)(color[1] * (1 - factor) + other[1] * factor),
            (int)(color[2] * (1 - factor) + other[2] * factor)
        ];
    }

    /// <summary>Усиливает насыщенность, удаляя каналы от среднего значения.</summary>
    public static List<int> Saturate(List<int> color, double factor = 2.0)
    {
        if (color == null || color.Count != 3) return color;
        double avg = (color[0] + color[1] + color[2]) / 3.0;
        return
        [
            Math.Clamp((int)(avg + (color[0] - avg) * factor), 0, 255),
            Math.Clamp((int)(avg + (color[1] - avg) * factor), 0, 255),
            Math.Clamp((int)(avg + (color[2] - avg) * factor), 0, 255)
        ];
    }
}
