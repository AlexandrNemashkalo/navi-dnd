using System.Text;
using System.Text.RegularExpressions;

namespace NaviDnD.Helpers;

// Collapse redundant RGB changes in a buffered frame before sending it to the terminal.
public static class AnsiColorRuns
{
    private static readonly Regex Rgb = new(@"\x1b\[(38|48);2;\d+;\d+;\d+m", RegexOptions.Compiled);

    public static string Compact(string frame)
    {
        var output = new StringBuilder(frame.Length);
        string? foreground = null, background = null;
        string? pendingForeground = null, pendingBackground = null;
        int offset = 0;

        void FlushColors()
        {
            if (pendingForeground != null && pendingForeground != foreground)
                output.Append(pendingForeground);
            if (pendingBackground != null && pendingBackground != background)
                output.Append(pendingBackground);
            foreground = pendingForeground ?? foreground;
            background = pendingBackground ?? background;
            pendingForeground = pendingBackground = null;
        }

        void AppendContent(ReadOnlySpan<char> content)
        {
            if (content.IsEmpty) return;
            FlushColors();
            output.Append(content);
            // Other ANSI commands may reset colors; never assume their resulting state.
            if (content.Contains("\x1b[", StringComparison.Ordinal))
                foreground = background = null;
        }

        foreach (Match match in Rgb.Matches(frame))
        {
            AppendContent(frame.AsSpan(offset, match.Index - offset));
            if (match.Groups[1].Value == "38") pendingForeground = match.Value;
            else pendingBackground = match.Value;
            offset = match.Index + match.Length;
        }
        AppendContent(frame.AsSpan(offset));
        FlushColors();
        return output.ToString();
    }
}
