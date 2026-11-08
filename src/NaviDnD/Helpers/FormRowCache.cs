namespace NaviDnD.Helpers;

// Forms rebuild their logical canvas freely; only changed screen rows need terminal output.
// Compare color values, since hover and portrait rendering often create new RGB lists.
public sealed class FormRowCache
{
    private readonly Dictionary<(int row, int from, int to), (char glyph, int fg, int bg)[]> _rows = [];
    public void Invalidate() => _rows.Clear();

    public bool Changed(int row, int from, int to, char[,] chars, List<int>?[,] foreground,
        List<int>?[,] background, IReadOnlyList<int> defaultForeground, IReadOnlyList<int> defaultBackground)
    {
        var key = (row, from, to);
        bool changed = !_rows.TryGetValue(key, out var previous);
        previous ??= new (char, int, int)[to - from];
        for (int x = from; x < to; x++)
        {
            var cell = (chars[row, x], Rgb(foreground[row, x] ?? defaultForeground), Rgb(background[row, x] ?? defaultBackground));
            if (previous[x - from] != cell) changed = true;
            previous[x - from] = cell;
        }
        _rows[key] = previous;
        return changed;
    }

    private static int Rgb(IReadOnlyList<int> c) => c[0] << 16 | c[1] << 8 | c[2];
}
