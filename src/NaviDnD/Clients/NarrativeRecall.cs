using System.Text;
using System.Text.RegularExpressions;
using NaviDnD.Data.Models;

namespace NaviDnD.Clients;

// Select existing records, never summarize or rewrite campaign facts. IDs remain storage indexes.
internal static class NarrativeRecall
{
    internal sealed record Record(int Id, string Name, string Text, bool Thread, int Priority);

    internal static IReadOnlyList<Record> Rank(WorldState state, string focus)
    {
        var narrative = state.Narrative;
        if (narrative == null) return [];
        var placeNames = CurrentPlaceNames(state);
        var present = (state.Map.Entities ?? []).Where(e => e.Deleted != true).Select(e => e.Name)
            .Concat(narrative.Npcs.Where(n => n.Deleted != true && Mentions(focus, n.Name)).Select(n => n.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var records = narrative.PlotThreads.Select((t, i) => (t, i)).Where(x => x.t.Deleted != true)
            .Select(x => new Record(x.i, x.t.Name ?? "", $"[{x.t.Status}; hidden:{x.t.Hidden == true}] known: {x.t.Description}; GM: {x.t.DmNotes}; steps (newest first): {string.Join(" | ", x.t.Steps?.AsEnumerable().Reverse() ?? [])}", true,
                x.t.Hidden != true && Mentions(narrative.CurrentArc, x.t.Name) ? 600
                    : x.t.Hidden == true ? 0 : x.t.Status switch
                    {
                        PlotThreadStatuses.Active => 100,
                        PlotThreadStatuses.Offered => 60,
                        PlotThreadStatuses.Revealed => 40,
                        PlotThreadStatuses.Rumor => 20,
                        _ => 0,
                    }))
            .Concat(narrative.Npcs.Select((n, i) => (n, i)).Where(x => x.n.Deleted != true)
                .Select(x => new Record(x.i, x.n.Name ?? "", $"[{x.n.Attitude}; met:{x.n.Met == true}] known: {x.n.Notes}; GM: {x.n.DmNotes}; memory (newest first): {string.Join(" | ", x.n.Memory?.AsEnumerable().Reverse() ?? [])}", false,
                    present.Contains(x.n.Name) ? 500 : 0))).ToList();
        // Rare shared words help recall an old clue even when its thread title was not repeated in the plan.
        var terms = Regex.Matches(focus, @"[\p{L}\p{N}]{5,}").Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToList();
        var rareTerms = terms.Where(term => records.Count(r => Mentions(r.Text, term)) <= Math.Max(2, records.Count / 10)).ToList();
        return records.Select(r => r with
        {
            Priority = r.Priority + (Mentions(focus, r.Name) ? 1000 : 0)
                + (placeNames.Any(name => Mentions(r.Name, name) || Mentions(r.Text, name)) ? 250 : 0)
                + (present.Any(name => Mentions(r.Text, name)) ? 150 : 0)
                + TopicScore(rareTerms.Count(term => Mentions(r.Text, term))),
        }).OrderByDescending(r => r.Priority).ThenByDescending(r => r.Id).ToList();
    }

    internal static IReadOnlyList<string> CurrentPlaceNames(WorldState state)
    {
        if (state.World is not { Place: { Length: > 0 } } link) return [];
        var geo = string.IsNullOrWhiteSpace(link.Id) ? null : GameWorld.Geo(state);
        var place = geo == null ? null : GameWorld.ResolvePlace(geo, link.Place);
        return new[] { link.Place }.Concat(place?.FormerNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static string Index(IReadOnlyList<Record> ranked, bool threads)
    {
        var records = ranked.Where(r => r.Thread == threads).ToList();
        if (records.Count == 0) return "";
        string key = threads ? "narrative.plotThreads" : "narrative.npcs";
        var sb = new StringBuilder(key + ": ");
        int shown = 0;
        foreach (var record in records.Take(24))
        {
            string entry = $"{Clip(record.Name, 100)}#{record.Id}; ";
            if (sb.Length + entry.Length > 1600) break;
            sb.Append(entry);
            shown++;
        }
        if (shown < records.Count)
            sb.Append($"({shown}/{records.Count} shown; omitted records still exist; get_world_state {key}:#ID or {key}:~topic)");
        return sb.ToString();
    }

    internal static string Population(IReadOnlyList<Record> ranked)
    {
        var sb = new StringBuilder("## Selected campaign records (GM only; not hero knowledge)\n");
        int shown = 0;
        foreach (var record in ranked.Take(10))
        {
            string line = $"{(record.Thread ? "Thread" : "NPC")}#{record.Id} {Clip(record.Name, 100)}: {Clip(record.Text, 1200)}\n";
            if (sb.Length + line.Length > 6500) break;
            sb.Append(line);
            shown++;
        }
        sb.AppendLine($"Records shown: {shown}/{ranked.Count}. Omitted records still exist. For missing or truncated facts use get_world_state narrative.plotThreads:#ID or narrative.npcs:#ID; search archived facts with :~topic. Preserve established puzzle requirements; do not fill missing facts by replacing them.");
        return sb.ToString();
    }

    private static string Clip(string text, int maximum) => text.Length <= maximum ? text : text[..maximum] + "… [truncated; retrieve full record]";

    private static int TopicScore(int matches) => matches >= 2 ? Math.Min(850, 650 + matches * 25) : matches * 50;

    internal static bool Mentions(string? text, string? name)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        for (int offset = 0; offset <= text.Length - name.Length;)
        {
            int start = text.IndexOf(name, offset, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return false;
            int end = start + name.Length;
            if ((start == 0 || !char.IsLetterOrDigit(text[start - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end]))) return true;
            offset = start + 1;
        }
        return false;
    }
}
