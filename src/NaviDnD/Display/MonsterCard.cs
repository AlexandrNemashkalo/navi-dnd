using System.Text.RegularExpressions;
using NaviDnD.Helpers;

namespace NaviDnD;

// Monster stat card for the right panel: multi-colored lines (labels are muted, values and section headings are
// in the main color, names of actions are in the color of the creature, vulnerabilities are in green,
// resistances/immunities are in amber). Distances from the base (“cells”) are converted to feet, as in the
// entire game.
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
        ["T"] = L.T("tiny"), ["S"] = L.T("little"), ["M"] = L.T("medium"),
        ["L"] = L.T("large"), ["H"] = L.T("huge"), ["G"] = L.T("gargantuan"),
    };

    private static (string en, string ru)[] AbilityAbbr =>
        [("Str", L.T("Str")), ("Dex", L.T("Dex")), ("Con", L.T("Con")), ("Int", L.T("Int")), ("Wis", L.T("Wis")), ("Cha", L.T("Cha"))];

    public static List<Line> Build(MonsterInfo info, int width, Palette p)
    {
        var lines = new List<Line>();

        // Hat: type, size, worldview
        string size = SizeRu.GetValueOrDefault(info.Size, info.Size);
        AddWrapped(lines, width, (Capitalize(string.Join(", ", new[] { info.Type, size, info.Alignment }.Where(s => s.Length > 0))), p.Dim));
        lines.Add(new Line());

        AddField(lines, width, p, L.T("AC"), info.Ac);
        AddField(lines, width, p, L.T("HP"), info.Hp);
        AddField(lines, width, p, L.T("Speed"), ToFeet(info.Speed));
        lines.Add(new Line());

        AddAbilityGrid(lines, width, p, info);
        lines.Add(new Line());

        AddField(lines, width, p, L.T("Vulnerable"), info.Vulnerable, p.Good);
        AddField(lines, width, p, L.T("Resistant"), info.Resist, p.Warn);
        AddField(lines, width, p, L.T("Immune"), info.Immune, p.Warn);
        AddField(lines, width, p, L.T("Condition immune"), info.ConditionImmune, p.Warn);
        AddField(lines, width, p, L.T("Saving throws"), TranslateAbilities(info.Save));
        AddField(lines, width, p, L.T("Skills"), info.Skill);
        AddField(lines, width, p, L.T("Senses"), JoinNonEmpty(ToFeet(info.Senses), info.Passive.Length > 0 ? L.F("passive Perception {0}", info.Passive) : ""));
        AddField(lines, width, p, L.T("Languages"), info.Languages);
        AddField(lines, width, p, L.T("Challenge"), info.Cr);
        if (info.Spells.Length > 0) AddField(lines, width, p, L.T("Spells"), Clean(info.Spells));

        AddSection(lines, width, p, L.T("TRAITS"), info.Traits, "");
        AddSection(lines, width, p, L.T("ACTIONS"), info.Actions, "");
        AddSection(lines, width, p, L.T("REACTIONS"), info.Reactions, "");
        AddSection(lines, width, p, L.T("LEGENDARY ACTIONS"), info.Legendary, info.LegendaryIntro);

        while (lines.Count > 0 && lines[^1].Segments.Count == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    // Cuts into pages; a blank line at the beginning of the page is not needed. The section header does not
    // remain the last line of the page - it is moved to the next line along with its content.
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
        string[] names = [L.T("STR"), L.T("DEX"), L.T("CON"), L.T("INT"), L.T("WIS"), L.T("CHA")];
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
        // The title is in the main color, the line is muted: the color of the creature remains behind the names of the actions.
        var header = new Line();
        header.Segments.Add((title + " ", null));
        header.Segments.Add((new string('─', Math.Max(0, width - title.Length - 1)), p.Dim));
        lines.Add(header);
        if (!string.IsNullOrWhiteSpace(intro)) AddWrapped(lines, width, (Clean(intro), p.Dim));
        for (int i = 0; i < entries.Count; i++)
        {
            var (name, text) = entries[i];
            if (i > 0 || !string.IsNullOrWhiteSpace(intro)) lines.Add(new Line()); // records are not merged
            if (string.IsNullOrWhiteSpace(name)) AddWrapped(lines, width, (Clean(text), null));
            else AddWrapped(lines, width, (Clean(name) + ". ", p.Accent), (Clean(text), null));
        }
    }

    // Word wrapping while preserving the color of each word.
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
                while (w.Length > width) // word is longer than string
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

    // "6 squares" → "30 ft", "16/64 squares" → "80/320 ft".
    private static string ToFeet(string s) => Regex.Replace(s ?? "", @"(\d+)(?:\s*/\s*(\d+))?\s*клет\p{L}*", m =>
        m.Groups[2].Success
            ? L.F("{0}/{1} ft", int.Parse(m.Groups[1].Value) * 5, int.Parse(m.Groups[2].Value) * 5)
            : L.F("{0} ft", int.Parse(m.Groups[1].Value) * 5));

    private static string TranslateAbilities(string s)
    {
        foreach (var (en, ru) in AbilityAbbr) s = Regex.Replace(s ?? "", $@"\b{en}\b", ru);
        return s ?? "";
    }

    private static string Clean(string s) => ToFeet(Regex.Replace(Regex.Replace(s ?? "", "<[^>]+>", ""), @"\s+", " ").Trim());

    private static string JoinNonEmpty(params string[] parts) => string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
