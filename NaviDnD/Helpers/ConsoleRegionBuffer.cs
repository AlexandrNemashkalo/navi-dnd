using System.Text;
using System.Text.RegularExpressions;

namespace NaviDnD.Helpers;

// One console region owns its row cache; neighboring regions never invalidate it.
public sealed class ConsoleRegionBuffer
{
    private static readonly Regex Ansi = new(@"\x1b\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);
    private string[]? _rows;
    private (int left, int top, int width) _bounds;

    public void Invalidate() => _rows = null;

    public string Update(int left, int top, int width, IReadOnlyList<string> rows, string background)
    {
        bool moved = _bounds != (left, top, width) || _rows?.Length != rows.Count;
        var next = new string[rows.Count];
        var patch = new StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            string text = rows[i];
            int remaining = Math.Max(0, width - Ansi.Replace(text, "").Length);
            next[i] = text + background + new string(' ', remaining);
            if (moved || _rows![i] != next[i])
                patch.Append($"\x1b[{top + i + 1};{left + 1}H").Append(next[i]);
        }
        _bounds = (left, top, width);
        _rows = next;
        return patch.ToString();
    }
}
