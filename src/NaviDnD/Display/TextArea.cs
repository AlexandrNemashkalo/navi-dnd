using NaviDnD.Helpers;

namespace NaviDnD.Display;

// Multi-line field (world description, hero description): word wrapping, cursor in text, scrolling. Lines are
// segments of the source text (beginning, length), cursor is the index in the text, scroll is the first visible
// line. Draws by the caller: View gives the visible lines and where the cursor is, Bar gives the scroll bar on
// the right.
internal static class TextArea
{
    // Word wrap: line - up to width characters, breaks at the last space (the space remains the end of the
    // line); a word longer than a line is cut to width.
    public static List<(int start, int len)> Layout(string text, int width)
    {
        var lines = new List<(int, int)>();
        width = Math.Max(1, width);
        int start = 0;
        while (text.Length - start > width)
        {
            int space = text.LastIndexOf(' ', start + width, width);
            if (space > start) { lines.Add((start, space - start)); start = space + 1; }
            else { lines.Add((start, width)); start += width; }
        }
        lines.Add((start, text.Length - start));
        return lines;
    }

    private static int LineOf(List<(int start, int len)> lines, int cursor)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
            if (cursor >= lines[i].start) return i;
        return 0;
    }

    // Visible lines (rows) and the location of the cursor in them (curRow -1 - the cursor is not visible).
    // follow - move the scroll so that the cursor is visible (after editing/moving the cursor); otherwise
    // scroll - as specified by the player (wheel).
    public static List<string> View(string text, int cursor, ref int scroll, int width, int rows, bool follow,
        out int curRow, out int curCol, out int total)
    {
        var lines = Layout(text, width);
        total = lines.Count;
        int line = LineOf(lines, Math.Clamp(cursor, 0, text.Length));
        if (follow)
        {
            if (line < scroll) scroll = line;
            if (line >= scroll + rows) scroll = line - rows + 1;
        }
        scroll = Math.Clamp(scroll, 0, Math.Max(0, lines.Count - rows));
        curRow = line - scroll < rows && line >= scroll ? line - scroll : -1;
        curCol = Math.Min(cursor - lines[line].start, width);
        var shown = new List<string>();
        for (int i = scroll; i < lines.Count && shown.Count < rows; i++) shown.Add(text.Substring(lines[i].start, lines[i].len));
        return shown;
    }

    // Field keys: ←→ Home End (by line), ↑↓ (by line; at the top/bottom line - false: the focus goes further),
    // PgUp/PgDn, Backspace/Delete, print and paste - at the cursor location. true - the key has been processed.
    public static bool Edit(ref string text, ref int cursor, ConsoleKeyInfo key, int max, int width, int rows)
    {
        cursor = Math.Clamp(cursor, 0, text.Length);
        if (ClipboardText.IsPasteKey(key))
        {
            string paste = ClipboardText.Get().Replace("\r", "").Replace("\n", " ");
            paste = paste[..Math.Min(paste.Length, Math.Max(0, max - text.Length))];
            text = text.Insert(cursor, paste);
            cursor += paste.Length;
            return true;
        }
        var lines = Layout(text, width);
        int line = LineOf(lines, cursor), col = cursor - lines[line].start;
        int ToLine(int l) => lines[l].start + Math.Min(col, lines[l].len);
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow: cursor = Math.Max(0, cursor - 1); return true;
            case ConsoleKey.RightArrow: cursor = Math.Min(text.Length, cursor + 1); return true;
            case ConsoleKey.Home: cursor = lines[line].start; return true;
            case ConsoleKey.End: cursor = lines[line].start + lines[line].len; return true;
            case ConsoleKey.UpArrow:
                if (line == 0) return false;
                cursor = ToLine(line - 1);
                return true;
            case ConsoleKey.DownArrow:
                if (line == lines.Count - 1) return false;
                cursor = ToLine(line + 1);
                return true;
            case ConsoleKey.PageUp: cursor = ToLine(Math.Max(0, line - rows)); return true;
            case ConsoleKey.PageDown: cursor = ToLine(Math.Min(lines.Count - 1, line + rows)); return true;
            case ConsoleKey.Backspace:
                if (cursor > 0) { text = text.Remove(cursor - 1, 1); cursor--; }
                return true;
            case ConsoleKey.Delete:
                if (cursor < text.Length) text = text.Remove(cursor, 1);
                return true;
        }
        if (!char.IsControl(key.KeyChar) && text.Length < max)
        {
            text = text.Insert(cursor, key.KeyChar.ToString());
            cursor++;
            return true;
        }
        return false;
    }

    // Click on the visible row row, column col - the cursor goes there.
    public static int CursorAt(string text, int scroll, int width, int row, int col)
    {
        var lines = Layout(text, width);
        int l = Math.Clamp(scroll + row, 0, lines.Count - 1);
        return lines[l].start + Math.Clamp(col, 0, lines[l].len);
    }

    // Scroll bar on rows lines: null - text fits; otherwise for each line - a slider (true) or track.
    public static bool[]? Bar(int rows, int total, int scroll)
    {
        if (total <= rows) return null;
        int thumb = Math.Max(1, rows * rows / total);
        int pos = (int)Math.Round((double)scroll * (rows - thumb) / (total - rows));
        return [.. Enumerable.Range(0, rows).Select(r => r >= pos && r < pos + thumb)];
    }
}
