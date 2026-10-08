using System.Text.RegularExpressions;
using NaviDnD.Helpers;

namespace NaviDnD;

// Карточка статов монстра для правой панели: многоцветные строки (подписи приглушённые, значения и
// заголовки секций — основным цветом, названия действий — цветом существа, уязвимости зелёным,
// сопротивления/иммунитеты янтарным). Расстояния из базы («клетки») переводятся в футы, как во всей игре.
public static class MonsterCard
{
    public sealed record Palette(List<int> Dim, List<int> Accent, List<int> Good, List<int> Warn);

    public sealed class Line
    {
        public List<(string text, List<int>? color)> Segments { get; } = [];
        public string Plain => string.Concat(Segments.Select(s => s.text));
    }

    private static Dictionary<string, string> SizeRu => new()
    {
        ["T"] = L.T("крошечный"), ["S"] = L.T("маленький"), ["M"] = L.T("средний"),
        ["L"] = L.T("большой"), ["H"] = L.T("огромный"), ["G"] = L.T("громадный"),
    };

    private static (string en, string ru)[] AbilityAbbr =>
        [("Str", L.T("Сил")), ("Dex", L.T("Лов")), ("Con", L.T("Тел")), ("Int", L.T("Инт")), ("Wis", L.T("Мдр")), ("Cha", L.T("Хар"))];

    public static List<Line> Build(MonsterInfo info, int width, Palette p)
    {
        var lines = new List<Line>();

        // Шапка: тип, размер, мировоззрение
        string size = SizeRu.GetValueOrDefault(info.Size, info.Size);
        AddWrapped(lines, width, (Capitalize(string.Join(", ", new[] { info.Type, size, info.Alignment }.Where(s => s.Length > 0))), p.Dim));
        lines.Add(new Line());

        AddField(lines, width, p, L.T("КД"), info.Ac);
        AddField(lines, width, p, L.T("ХП"), info.Hp);
        AddField(lines, width, p, L.T("Скорость"), ToFeet(info.Speed));
        lines.Add(new Line());

        AddAbilityGrid(lines, width, p, info);
        lines.Add(new Line());

        AddField(lines, width, p, L.T("Уязвимость"), info.Vulnerable, p.Good);
        AddField(lines, width, p, L.T("Сопротивление"), info.Resist, p.Warn);
        AddField(lines, width, p, L.T("Иммунитет"), info.Immune, p.Warn);
        AddField(lines, width, p, L.T("Не подвержен"), info.ConditionImmune, p.Warn);
        AddField(lines, width, p, L.T("Спасброски"), TranslateAbilities(info.Save));
        AddField(lines, width, p, L.T("Навыки"), info.Skill);
        AddField(lines, width, p, L.T("Чувства"), JoinNonEmpty(ToFeet(info.Senses), info.Passive.Length > 0 ? L.F("пасс. Восприятие {0}", info.Passive) : ""));
        AddField(lines, width, p, L.T("Языки"), info.Languages);
        AddField(lines, width, p, L.T("Опасность"), info.Cr);
        if (info.Spells.Length > 0) AddField(lines, width, p, L.T("Заклинания"), Clean(info.Spells));

        AddSection(lines, width, p, L.T("ОСОБЕННОСТИ"), info.Traits, "");
        AddSection(lines, width, p, L.T("ДЕЙСТВИЯ"), info.Actions, "");
        AddSection(lines, width, p, L.T("РЕАКЦИИ"), info.Reactions, "");
        AddSection(lines, width, p, L.T("ЛЕГЕНДАРНЫЕ ДЕЙСТВИЯ"), info.Legendary, info.LegendaryIntro);

        while (lines.Count > 0 && lines[^1].Segments.Count == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    // Режет на страницы; пустая строка в начале страницы не нужна. Заголовок секции не остаётся
    // последней строкой страницы — переносится на следующую вместе со своим содержимым.
    public static List<List<Line>> Paginate(List<Line> lines, int linesPerPage)
    {
        var pages = new List<List<Line>>();
        var current = new List<Line>();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (current.Count == 0 && line.Segments.Count == 0) continue;
            bool isHeaderAtBottom = current.Count == linesPerPage - 1 && line.Plain.Contains('─');
            if (current.Count >= linesPerPage || isHeaderAtBottom)
            {
                pages.Add(current);
                current = [];
                if (line.Segments.Count == 0) continue;
            }
            current.Add(line);
        }
        if (current.Count > 0 || pages.Count == 0) pages.Add(current);
        return pages;
    }

    private static void AddField(List<Line> lines, int width, Palette p, string label, string value, List<int>? valueColor = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        AddWrapped(lines, width, (label + " ", p.Dim), (Clean(value), valueColor));
    }

    private static void AddAbilityGrid(List<Line> lines, int width, Palette p, MonsterInfo info)
    {
        string[] names = [L.T("СИЛ"), L.T("ЛОВ"), L.T("ТЕЛ"), L.T("ИНТ"), L.T("МДР"), L.T("ХАР")];
        string[] raw = [info.Str, info.Dex, info.Con, info.Int, info.Wis, info.Cha];
        int col = Math.Clamp(width / 6, 4, 6);

        var header = new Line();
        var mods = new Line();
        var scores = new Line();
        for (int i = 0; i < 6; i++)
        {
            header.Segments.Add((names[i].PadRight(col), p.Dim));
            bool ok = int.TryParse(raw[i], out int score);
            int mod = ok ? (int)Math.Floor((score - 10) / 2.0) : 0;
            mods.Segments.Add(((ok ? (mod >= 0 ? $"+{mod}" : $"{mod}") : "—").PadRight(col), null));
            scores.Segments.Add(((ok ? $"{score}" : "").PadRight(col), p.Dim));
        }
        lines.Add(header);
        lines.Add(mods);
        lines.Add(scores);
    }

    private static void AddSection(List<Line> lines, int width, Palette p, string title,
        List<(string Name, string Text)> entries, string intro)
    {
        if (entries.Count == 0 && string.IsNullOrWhiteSpace(intro)) return;
        lines.Add(new Line());
        // Заголовок — основным цветом, линия — приглушённо: цвет существа остаётся за названиями действий.
        var header = new Line();
        header.Segments.Add((title + " ", null));
        header.Segments.Add((new string('─', Math.Max(0, width - title.Length - 1)), p.Dim));
        lines.Add(header);
        if (!string.IsNullOrWhiteSpace(intro)) AddWrapped(lines, width, (Clean(intro), p.Dim));
        for (int i = 0; i < entries.Count; i++)
        {
            var (name, text) = entries[i];
            if (i > 0 || !string.IsNullOrWhiteSpace(intro)) lines.Add(new Line()); // записи не сливаются
            if (string.IsNullOrWhiteSpace(name)) AddWrapped(lines, width, (Clean(text), null));
            else AddWrapped(lines, width, (Clean(name) + ". ", p.Accent), (Clean(text), null));
        }
    }

    // Перенос по словам с сохранением цвета каждого слова.
    private static void AddWrapped(List<Line> lines, int width, params (string text, List<int>? color)[] parts)
    {
        var line = new Line();
        int len = 0;
        foreach (var (text, color) in parts)
        {
            foreach (var word in Regex.Split(text, @"(?<= )"))
            {
                if (word.Length == 0) continue;
                string w = word;
                if (len + w.TrimEnd().Length > width && len > 0)
                {
                    TrimLastSpace(line);
                    lines.Add(line);
                    line = new Line();
                    len = 0;
                }
                while (w.Length > width) // слово длиннее строки
                {
                    line.Segments.Add((w[..width], color));
                    lines.Add(line);
                    line = new Line();
                    w = w[width..];
                }
                Append(line, w, color);
                len += w.Length;
            }
        }
        TrimLastSpace(line);
        if (line.Segments.Count > 0) lines.Add(line);
    }

    private static void Append(Line line, string text, List<int>? color)
    {
        if (line.Segments.Count > 0 && ReferenceEquals(line.Segments[^1].color, color))
            line.Segments[^1] = (line.Segments[^1].text + text, color);
        else
            line.Segments.Add((text, color));
    }

    private static void TrimLastSpace(Line line)
    {
        if (line.Segments.Count == 0) return;
        var (t, c) = line.Segments[^1];
        line.Segments[^1] = (t.TrimEnd(), c);
    }

    // «6 клеток» → «30 фт», «16/64 клетки» → «80/320 фт».
    private static string ToFeet(string s) => Regex.Replace(s ?? "", @"(\d+)(?:\s*/\s*(\d+))?\s*клет\p{L}*", m =>
        m.Groups[2].Success
            ? L.F("{0}/{1} фт", int.Parse(m.Groups[1].Value) * 5, int.Parse(m.Groups[2].Value) * 5)
            : L.F("{0} фт", int.Parse(m.Groups[1].Value) * 5));

    private static string TranslateAbilities(string s)
    {
        foreach (var (en, ru) in AbilityAbbr) s = Regex.Replace(s ?? "", $@"\b{en}\b", ru);
        return s ?? "";
    }

    private static string Clean(string s) => ToFeet(Regex.Replace(Regex.Replace(s ?? "", "<[^>]+>", ""), @"\s+", " ").Trim());

    private static string JoinNonEmpty(params string[] parts) => string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
