using System.Text;

namespace NaviDnD.Helpers;

public static class MapSymbolText
{
    // Display only: never alter the symbol used as an entity key in saves or AI requests.
    // A map cell is three columns. Emoji, combining marks and controls cannot occupy it safely.
    public static string Format(string? symbol)
    {
        var text = new StringBuilder(3);
        foreach (var rune in (symbol ?? "").EnumerateRunes())
        {
            if (text.Length == 3) break;
            int c = rune.Value;
            bool narrow = c is >= 0x20 and <= 0x7e || c is >= 0x400 and <= 0x4ff && Rune.IsLetterOrDigit(rune);
            text.Append(narrow ? (char)c : '?');
        }
        return text.ToString().PadRight(3);
    }

    public static string Initiative(string? symbol, bool current) =>
        current ? "> " + Format(symbol) : " " + Format(symbol) + " ";
}
