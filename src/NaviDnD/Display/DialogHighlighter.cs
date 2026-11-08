using System.Text.RegularExpressions;

namespace NaviDnD;

// Highlighting the AI text in the dialogue: names of creatures/objects in their color from the map (including
// cases), mechanics in [...] muted, damage/healing/changes “A→B” and the outcome of the roll in color. The
// words are in both game languages. Pure function over a line: counted when drawing visible lines, not when
// building the cache.
public sealed class DialogHighlighter
{
    public sealed record Palette(List<int> Mechanics, List<int> Bad, List<int> Good);

    private readonly List<(Regex regex, List<int> color)> _names;
    private readonly Palette _palette;

    // The whole number or formula of the dice: “8”, “2d4=5”, “1d6+2=5”, “2d4 + 3”.
    private const string Num = @"\d+(?:\s*[кkdд]\s*\d+)?(?:\s*[+\-]\s*\d+(?:\s*[кkdд]\s*\d+)?)*(?:\s*=\s*\d+)?";

    private static readonly Regex Damage = new(
        $@"(?<![\p{{L}}\d])((урон|damage)\p{{L}}*\s*{Num}|{Num}\s*(урон|колющ|рубящ|дробящ|огн|холод|некрот|психич|излуч|звук|кисл|силов|электр|яд|damage|piercing|slashing|bludgeoning|fire|cold|necrotic|psychic|radiant|thunder|acid|force|lightning|poison)\p{{L}}*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Heal = new(
        $@"(?<![\p{{L}}\d])((лечени|исцел|восстан|heal|restor|regain)\p{{L}}*\s*\+?{Num}|\+\d+\s*(хп|hp)(?![\p{{L}}\d]))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Change = new(@"(\d+)\s*→\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex GoodWord = new(@"(?<!\p{L})(попадани|успех|успешн|крит\p{L}*\s+успех|hits?(?!\s+points?)(?!\p{L})|success|crit\p{L}*\s+success)\p{L}*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BadWord = new(@"(?<!\p{L})(промах|провал|miss(?:es|ed)?(?!\p{L})|fail)\p{L}*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const string Vowels = "аяоеёуюыийьАЯОЕЁУЮЫИЙЬ";
    // The short stem (“Gul” from “Gul”) is itself a common word (“gul”), so we require the ending.
    private const string ShortStemEndings = "(ь|а|я|у|ю|е|и|ы|й|ой|ей|ом|ем|ам|ям|ами|ями|ах|ях)";

    // symbols - symbols from the map (“SK1”, “NUR”): exact match, case sensitive.
    public DialogHighlighter(IEnumerable<(string name, List<int> color)> names, Palette palette,
        IEnumerable<(string symbol, List<int> color)>? symbols = null)
    {
        _palette = palette;
        _names = names
            .Where(n => !string.IsNullOrWhiteSpace(n.name) && n.color is { Count: 3 })
            .SelectMany(n => PatternsFor(n.name).Select(p => (p, n.color)))
            .OrderByDescending(x => x.p.Length) // long names before: "Torvin's Skeleton" is more important than "Skeleton"
            .Select(x => (new Regex(x.p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), x.color))
            .Concat((symbols ?? [])
                .Where(s => !string.IsNullOrWhiteSpace(s.symbol) && s.color is { Count: 3 })
                .Select(s => (new Regex(@"(?<![\p{L}\d])" + Regex.Escape(s.symbol.Trim()) + @"(?![\p{L}\d])"), s.color)))
            .ToList();
    }

    // The color of each character in the string (null is the default color). bracketDepth - whether the bracket
    // [...] is open at the beginning of the line (the mechanics carry over to the following lines of the
    // message); updated to the end of the line.
    public List<(string text, List<int>? color)> Segments(string line, ref int bracketDepth)
    {
        var colors = new List<int>?[line.Length];

        int depth = bracketDepth;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '[') depth++;
            if (depth > 0) colors[i] = _palette.Mechanics;
            if (line[i] == ']' && depth > 0) depth--;
        }
        bracketDepth = depth;

        Paint(colors, Damage, _palette.Bad);
        Paint(colors, Heal, _palette.Good);
        Paint(colors, BadWord, _palette.Bad);
        Paint(colors, GoodWord, _palette.Good);
        foreach (Match m in Change.Matches(line))
        {
            if (!int.TryParse(m.Groups[1].Value, out int from) || !int.TryParse(m.Groups[2].Value, out int to) || from == to) continue;
            Fill(colors, m.Index, m.Length, to < from ? _palette.Bad : _palette.Good);
        }

        var named = new bool[line.Length];
        foreach (var (regex, color) in _names)
            foreach (Match m in regex.Matches(line))
            {
                if (named.AsSpan(m.Index, m.Length).Contains(true)) continue; // already taken by a longer name
                Fill(colors, m.Index, m.Length, color);
                named.AsSpan(m.Index, m.Length).Fill(true);
            }

        var result = new List<(string, List<int>?)>();
        int start = 0;
        for (int i = 1; i <= line.Length; i++)
        {
            if (i < line.Length && ReferenceEquals(colors[i], colors[start])) continue;
            result.Add((line[start..i], colors[start]));
            start = i;
        }
        return result;

        void Paint(List<int>?[] target, Regex regex, List<int> color)
        {
            foreach (Match m in regex.Matches(line)) Fill(target, m.Index, m.Length, color);
        }
    }

    private static void Fill(List<int>?[] colors, int index, int length, List<int> color)
    {
        for (int i = index; i < index + length; i++) colors[i] = color;
    }

    // Full name (all words based on the basics) and, for compound names, the first word separately (“Torvin’s
    // skeleton” → and “Torvin’s skeleton”, and simply “skeleton”).
    private static IEnumerable<string> PatternsFor(string name)
    {
        var words = name.Split([' ', '-', ','], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) yield break;

        const string left = @"(?<![\p{L}\d])", right = @"(?![\p{L}\d])";
        yield return left + string.Join(@"[\s\-]+", words.Select(WordPattern)) + right;
        if (words.Length > 1 && words[0].Length >= 4)
            yield return left + WordPattern(words[0]) + right;
    }

    private static string WordPattern(string word)
    {
        // English name - without cases: “Goblin”, “Goblins”, “Goblin's”.
        if (!word.Any(c => c is >= 'Ѐ' and <= 'ӿ')) return Regex.Escape(word) + "(?:'s|s|es)?";
        string stem = word.TrimEnd(Vowels.ToCharArray());
        if (stem.Length >= 4) return Regex.Escape(stem) + @"\p{L}{0,3}";
        if (stem.Length >= 2) return Regex.Escape(stem) + ShortStemEndings;
        return Regex.Escape(word);
    }
}
