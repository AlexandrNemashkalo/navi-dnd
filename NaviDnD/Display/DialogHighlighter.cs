using System.Text.RegularExpressions;

namespace NaviDnD;

// Подсветка текста нейронки в диалоге: имена существ/объектов их цветом с карты (с учётом падежей),
// механика в [...] приглушённо, урон/лечение/изменения «A→B» и исход броска — цветом.
// Чистая функция над строкой: считается при отрисовке видимых строк, а не при сборке кэша.
public sealed class DialogHighlighter
{
    public sealed record Palette(List<int> Mechanics, List<int> Bad, List<int> Good);

    private readonly List<(Regex regex, List<int> color)> _names;
    private readonly Palette _palette;

    // Число или формула кубиков целиком: «8», «2к4=5», «1d6+2=5», «2к4 + 3».
    private const string Num = @"\d+(?:\s*[кkdд]\s*\d+)?(?:\s*[+\-]\s*\d+(?:\s*[кkdд]\s*\d+)?)*(?:\s*=\s*\d+)?";

    private static readonly Regex Damage = new(
        $@"(?<![\p{{L}}\d])(урон\p{{L}}*\s*{Num}|{Num}\s*(урон|колющ|рубящ|дробящ|огн|холод|некрот|психич|излуч|звук|кисл|силов|электр|яд)\p{{L}}*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Heal = new(
        $@"(?<![\p{{L}}\d])((лечени|исцел|восстан)\p{{L}}*\s*\+?{Num}|\+\d+\s*(хп|hp)(?![\p{{L}}\d]))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Change = new(@"(\d+)\s*→\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex GoodWord = new(@"(?<!\p{L})(попадани|успех|успешн|крит\p{L}*\s+успех)\p{L}*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BadWord = new(@"(?<!\p{L})(промах|провал)\p{L}*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const string Vowels = "аяоеёуюыийьАЯОЕЁУЮЫИЙЬ";
    // Короткая основа («Гул» от «Гуль») сама по себе — обычное слово («гул»), поэтому требуем окончание.
    private const string ShortStemEndings = "(ь|а|я|у|ю|е|и|ы|й|ой|ей|ом|ем|ам|ям|ами|ями|ах|ях)";

    // symbols — символы с карты («SK1», «NUR»): совпадение точное, с учётом регистра.
    public DialogHighlighter(IEnumerable<(string name, List<int> color)> names, Palette palette,
        IEnumerable<(string symbol, List<int> color)>? symbols = null)
    {
        _palette = palette;
        _names = names
            .Where(n => !string.IsNullOrWhiteSpace(n.name) && n.color is { Count: 3 })
            .SelectMany(n => PatternsFor(n.name).Select(p => (p, n.color)))
            .OrderByDescending(x => x.p.Length) // длинные имена раньше: «Скелет Торвина» важнее «Скелет»
            .Select(x => (new Regex(x.p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), x.color))
            .Concat((symbols ?? [])
                .Where(s => !string.IsNullOrWhiteSpace(s.symbol) && s.color is { Count: 3 })
                .Select(s => (new Regex(@"(?<![\p{L}\d])" + Regex.Escape(s.symbol.Trim()) + @"(?![\p{L}\d])"), s.color)))
            .ToList();
    }

    // Цвет каждого символа строки (null — цвет по умолчанию). bracketDepth — открыта ли скобка [...]
    // на начале строки (механика переносится на следующие строки сообщения); обновляется на конец строки.
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
                if (named.AsSpan(m.Index, m.Length).Contains(true)) continue; // уже занято более длинным именем
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

    // Полное имя (все слова по основам) и, для составных имён, первое слово отдельно
    // («Скелет Торвина» → и «скелету Торвина», и просто «скелет»).
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
        string stem = word.TrimEnd(Vowels.ToCharArray());
        if (stem.Length >= 4) return Regex.Escape(stem) + @"\p{L}{0,3}";
        if (stem.Length >= 2) return Regex.Escape(stem) + ShortStemEndings;
        return Regex.Escape(word);
    }
}
