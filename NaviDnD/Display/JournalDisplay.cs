using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

// Hero's journal ([F3]) - like the character screen: immediately below the sub-tab title on the left (Quests /
// Characters / Bestiary, F6–F8), below them there are two columns: on the left is a list of entries (selection
// - Tab/hover, marker "←", pages - ▲ ▼ at the edge), on the right are details of the selected one (pages - "◄
// 1/2 ►" below, ←→). The block of pictures is only the picture of the entry. Data - currentArc, plotThreads
// with steps, npcs, knownMonsters), they are maintained by AI.
public class JournalDisplay(WorldState settings, DisplayConfig display)
{
    public enum Section { Quests, People, Locations, Bestiary, Notes }

    private static (Section section, string label, string key)[] Sections =>
        [(Section.Quests, L.T("QUESTS"), "F6"), (Section.People, L.T("PEOPLE"), "F7"), (Section.Locations, L.T("LOCATIONS"), "F8"),
         (Section.Bestiary, L.T("BESTIARY"), "F9"), (Section.Notes, L.T("NOTES"), "F10")];

    private static string NotesHint => L.T("Text below + Enter — add a note");

    // Notes section: list entry → index in PlayerNotes.All (new ones on top).
    private List<int> _noteIndex = [];

    public Section Current { get; private set; } = Section.Quests;
    private readonly Dictionary<Section, int> _page = [];
    private readonly Dictionary<Section, int> _selected = [];
    // Pinned entry (mouse click / Tab, like inventory items): hover shows another one temporarily, the cursor
    // leaves the entry - pinned again.
    private readonly Dictionary<Section, int> _pinned = [];

    private sealed record Entry(string Title, string Marker, List<int> Color, List<(string text, List<int> color)> Details, string? Image, List<int>? ImageColor);

    private List<Entry> _entries = [];
    private int _startTop = -1, _listTop, _rows, _pageCount = 1, _leftW, _rightW;
    private readonly List<(int x0, int x1, Section section)> _tabSpans = [];
    private int _barRow;
    private int _hoveredTab = -1;
    // Scrolling through the list - ▲ (first line) / ▼ (last) at the right edge of the list column.
    private int _listArrowX = -1, _hoveredListArrow;
    // Detail pages - "◄ 1/2 ►" bottom center of right column.
    private int _detailPage, _detailPageCount = 1, _detailLeftX = -1, _detailRightX = -1, _hoveredDetailArrow;

    private List<int> Fg => display.MainForeground;
    private List<int> Bright => ColorHelper.Pale(display.MainForeground, 0.75);
    private List<int> Dim => ColorHelper.Darker(display.MainForeground, 0.45);

    // ── Section entries ──
    private List<Entry> BuildEntries(Section section)
    {
        var list = new List<Entry>();
        var n = settings.Narrative;
        switch (section)
        {
            case Section.Quests:
                // The current target is the first entry.
                if (n?.CurrentArc is { Length: > 0 } arc)
                    list.Add(new Entry(L.T("Current goal"), "»", Bright, [(L.T("Current goal"), Bright), ("", Fg), (arc, Fg)], "lorc/scroll-unfurled", null));
                // Only what the hero knows: hidden threads and notes from the GM (dmNotes) does not appear in the journal.
                var threads = (n?.PlotThreads ?? []).Where(t => t.Deleted != true && t.Hidden != true).ToList();
                foreach (var t in threads.Where(IsActive).Concat(threads.Where(t => !IsActive(t))))
                {
                    bool active = IsActive(t);
                    bool failed = IsFailed(t);
                    var details = new List<(string, List<int>)> { (t.Name ?? L.T("Quest"), Bright), (L.T("Status: ") + (t.Status ?? L.T("active")), Dim), ("", Fg) };
                    if (!string.IsNullOrWhiteSpace(t.Description)) details.Add((t.Description, Fg));
                    if (t.Steps is { Count: > 0 } steps)
                    {
                        details.Add(("", Fg));
                        details.Add((L.T("Progress:"), Dim));
                        details.AddRange(steps.Select(s => ("· " + s, Fg)));
                    }
                    list.Add(new Entry(t.Name ?? L.T("Quest"), active ? "▸" : failed ? "✗" : "✓", active ? Fg : Dim, details, "lorc/scroll-unfurled", null));
                }
                break;

            case Section.People:
                // Only those met by the hero (met or have conversation memory).
                foreach (var p in (n?.Npcs ?? []).Where(p => p.Deleted != true && (p.Met == true || p.Memory is { Count: > 0 })))
                {
                    var color = AttitudeColor(p.Attitude);
                    var onMap = (settings.Map.Entities ?? []).FirstOrDefault(e => e.Deleted != true && string.Equals(e.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                    var details = new List<(string, List<int>)> { (p.Name ?? "?", color) };
                    if (!string.IsNullOrWhiteSpace(p.Attitude)) details.Add((L.T("Attitude: ") + p.Attitude, Dim));
                    details.Add(("", Fg));
                    if (!string.IsNullOrWhiteSpace(p.Notes)) details.Add((p.Notes, Fg));
                    if (p.Memory is { Count: > 0 } memory)
                    {
                        details.Add(("", Fg));
                        details.Add((L.T("Remembered:"), Dim));
                        details.AddRange(memory.Select(m => ("· " + m, Fg)));
                    }
                    list.Add(new Entry(p.Name ?? "?", "●", color, details, onMap?.Image ?? "delapouite/person", onMap?.Color ?? color));
                }
                break;

            // Places on the world map known to the hero: where he is now - first, then where he has been, then which ones he has heard about.
            case Section.Locations:
                if (GameWorld.ForDisplay(settings) is { } world && settings.World is { } link)
                {
                    var places = world.Places
                        .OrderBy(p => p.Name == link.Place ? 0 : link.Visited.Contains(p.Name) ? 1 : 2)
                        .ThenBy(p => p.Type == WorldPlaceTypes.Capital ? 1 : 0).ThenBy(p => p.Name);
                    var hero = GameWorld.HeroTile(settings);
                    foreach (var p in places)
                    {
                        bool here = p.Name == link.Place, visited = link.Visited.Contains(p.Name);
                        var (icon, iconColor) = WorldMapView.Icon(p.Type);
                        string kingdom = p.Kingdom >= 0 && p.Kingdom < world.Kingdoms.Count ? L.F("kingdom of {0}", world.Kingdoms[p.Kingdom].Name) : L.T("no man's land");
                        var details = new List<(string, List<int>)>
                        {
                            (p.Name, Bright),
                            ($"{L.T(WorldPlaceTypes.Label(p.Type))} · {kingdom} · {L.T(MapGen.Generators.WorldAtlas.Compass(world, p.X, p.Y))}", Dim),
                            (here ? L.T("You are here") : visited ? L.T("You've been here") : L.T("Known by hearsay"), Dim),
                            ("", Fg),
                        };
                        if (!string.IsNullOrWhiteSpace(p.Description)) details.Add((p.Description, Fg));
                        if (!here && hero is { } h && MapGen.Generators.WorldAtlas.Route(world, h, (p.X, p.Y)) is { } r)
                        {
                            details.Add(("", Fg));
                            details.Add((L.T("From here: ") + $"{L.T(MapGen.Generators.WorldAtlas.Direction(p.X - h.x, p.Y - h.y))}, {r.Summary}", Dim));
                        }
                        list.Add(new Entry(p.Name, here ? "@" : visited ? icon.ToString() : "·", here || visited ? Fg : Dim, details,
                            WorldMapView.PlaceImage(p.Type), iconColor));
                    }
                }
                break;

            case Section.Bestiary:
                foreach (var key in n?.KnownMonsters ?? [])
                {
                    var info = MonsterDatabase.Find(key);
                    var onMap = (settings.Map.Entities ?? []).FirstOrDefault(e => string.Equals(e.MonsterKey, key, StringComparison.OrdinalIgnoreCase));
                    var details = new List<(string, List<int>)> { (info?.LocalizedName ?? key, Bright) };
                    if (info != null)
                    {
                        string kind = string.Join(", ", new[] { info.Size, info.Type, info.Alignment }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        if (kind.Length > 0) details.Add((kind, Dim));
                        details.Add(("", Fg));
                        details.Add((L.F("AC {0} · HP {1} · speed {2}", info.Ac, info.Hp, info.Speed), Fg));
                        details.Add((L.F("STR {0}  DEX {1}  CON {2}  INT {3}  WIS {4}  CHA {5}", info.Str, info.Dex, info.Con, info.Int, info.Wis, info.Cha), Fg));
                        if (!string.IsNullOrWhiteSpace(info.Senses)) details.Add((L.T("Senses: ") + info.Senses, Dim));
                        if (!string.IsNullOrWhiteSpace(info.Languages)) details.Add((L.T("Languages: ") + info.Languages, Dim));
                    }
                    list.Add(new Entry(info?.LocalizedName ?? key, "♦", Fg, details, onMap?.Image, onMap?.Color));
                }
                break;

            // The player’s own notes are from a separate file (PlayerNotes), they are not transferred to the GM.
            case Section.Notes:
                var notes = PlayerNotes.All;
                _noteIndex = Enumerable.Range(0, notes.Count).Reverse().ToList();
                foreach (int ni in _noteIndex)
                {
                    var note = notes[ni];
                    string when = L.F("Day {0}", note.Day) + (string.IsNullOrEmpty(note.PartOfDay) ? "" : " · " + GameTime.PartOfDayText(note.PartOfDay));
                    string firstLine = note.Text.Split('\n')[0];
                    if (note.ByMaster) when += " · " + L.T("written by the DM");
                    var details = new List<(string, List<int>)> { (when, Dim), ("", Fg), (note.Text, Fg), ("", Fg), (L.T("[Del] — delete the note"), Dim) };
                    list.Add(new Entry(firstLine, note.ByMaster ? "✦" : "✎", Fg, details, "lorc/quill-ink", null));
                }
                break;
        }
        return list;
    }

    // The GM writes the task status in words in the game language: “completed”, “failed”, “completed”, “failed”.
    private static bool StatusIs(PlotThread t, params string[] prefixes) =>
        t.Status is { } s && prefixes.Any(p => s.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsFailed(PlotThread t) => StatusIs(t, "провал", "fail");

    private static bool IsActive(PlotThread t) => !StatusIs(t, "заверш", "выполн", "complet", "done", "finish") && !IsFailed(t);

    private List<int> AttitudeColor(string? attitude)
    {
        string a = attitude?.ToLowerInvariant() ?? "";
        if (a.Contains("враж") || a.Contains("hostil") || a.Contains("enemy")) return [214, 110, 98];
        if (a.Contains("друж") || a.Contains("союз") || a.Contains("friend") || a.Contains("ally") || a.Contains("allied")) return [130, 196, 120];
        if (a.Contains("нейтр") || a.Contains("neutral")) return [206, 186, 128];
        return Bright;
    }

    // List Page Entries: In Notes, the last line is a hint on how to add a note.
    private int PerPage => Current == Section.Notes ? Math.Max(1, _rows - 1) : _rows;

    private string EmptyText => Current switch
    {
        Section.Quests => L.T("No quests yet."),
        Section.People => L.T("No acquaintances yet."),
        Section.Notes => L.T("No notes yet."),
        Section.Locations => L.T("No known places yet."),
        _ => L.T("No identified creatures yet."),
    };

    // ── Rendering ──
    private readonly ConsoleRegionBuffer _listRegion = new();
    private readonly ConsoleRegionBuffer _detailRegion = new();

    public void Draw() => DrawCore(true);

    private void DrawCore(bool rebuild)
    {
        _startTop = Term.CursorTop;
        int inner = display.InnerWidth(display.ViewCols(settings.Map));
        var border = new BorderDrawer(settings, display);
        if (rebuild) { _entries = BuildEntries(Current); _listRegion.Invalidate(); _detailRegion.Invalidate(); }
        _rows = Math.Max(3, display.MaxHigh - 3);
        _pageCount = Math.Max(1, (_entries.Count + PerPage - 1) / PerPage);
        int page = Math.Clamp(_page.GetValueOrDefault(Current), 0, _pageCount - 1);
        _page[Current] = page;
        int sel = _selected.GetValueOrDefault(Current, -1);
        if (sel >= _entries.Count) sel = _entries.Count - 1;
        if (sel < 0 && _entries.Count > 0) sel = page * PerPage;
        _selected[Current] = sel;

        border.DrawSeparator();
        _barRow = Term.CursorTop;
        border.DrawContentLine(DrawTabBar);

        _leftW = Math.Max(24, inner * 2 / 5);
        _rightW = inner - _leftW - 1;
        border.DrawSeparatorWith2Parts('─', _leftW, _rightW);
        _listTop = Term.CursorTop;
        _listArrowX = DisplayConfig.LeftMargin + 1 + _leftW - 2;

        // Details by page: did not fit - the last line of the column under “◄ 1/2 ►”.
        var all = BuildDetailLines(sel, _rightW - 4);
        int perPage = all.Count > _rows ? _rows - 1 : _rows;
        _detailPageCount = Math.Max(1, (all.Count + perPage - 1) / perPage);
        _detailPage = Math.Clamp(_detailPage, 0, _detailPageCount - 1);
        var details = all.Skip(_detailPage * perPage).Take(perPage).ToList();

        string Capture(Action draw)
        {
            var output = Console.Out;
            using var buffer = new StringWriter();
            try
            {
                Console.SetOut(buffer);
                ColorHelper.SetBackgroundColor(display.MainBackground);
                ColorHelper.SetForegroundColor(Fg);
                draw();
                return buffer.ToString();
            }
            finally { Console.SetOut(output); }
        }
        var left = new string[_rows];
        var right = new string[_rows];
        for (int r = 0; r < _rows; r++)
        {
            int idx = page * PerPage + r, row = r;
            left[r] = Capture(() => WriteListRow(idx, sel, row, page));
            right[r] = Capture(() =>
            {
                if (_detailPageCount > 1 && row == _rows - 1) WriteDetailArrows();
                else if (row < details.Count) { Console.Write("  "); ColorHelper.WriteColored(details[row].text, details[row].color); }
            });
            if (rebuild) border.DrawContentLine2Columns(() => {}, () => {}, _leftW, _rightW);
        }
        string bg = $"\x1b[48;2;{display.MainBackground[0]};{display.MainBackground[1]};{display.MainBackground[2]}m";
        string patch = _listRegion.Update(DisplayConfig.LeftMargin + 1, _listTop, _leftW, left, bg)
            + _detailRegion.Update(DisplayConfig.LeftMargin + 2 + _leftW, _listTop, _rightW, right, bg);
        Console.Write(AnsiColorRuns.Compact(patch));
        ColorHelper.SetBackgroundColor(display.MainBackground);
        ColorHelper.SetForegroundColor(Fg);
        Term.SetCursorPosition(0, _listTop + _rows);
        border.DrawSeparatorWith2Parts('─', _leftW, _rightW);
        ShowImage(sel);
    }

    private void WriteListRow(int idx, int sel, int row, int page)
    {
        if (Current == Section.Notes && row == _rows - 1 && (_pageCount <= 1))
        {
            ColorHelper.WriteColored(" " + Fit(NotesHint, _leftW - 2), Dim);
            return;
        }
        if (Current == Section.Notes && row >= PerPage) idx = int.MaxValue;
        // ▲ / ▼ - in the first and last line at the right edge, if there are more records than a page.
        string arrow = _pageCount <= 1 ? " " : row == 0 ? "▲" : row == _rows - 1 ? "▼" : " ";
        bool canGo = row == 0 ? page > 0 : page < _pageCount - 1;
        var arrowColor = !canGo ? ColorHelper.Darker(Fg, 0.5) : _hoveredListArrow == (row == 0 ? -1 : 1) ? Bright : Fg;
        if (_entries.Count == 0)
        {
            if (row == 0) ColorHelper.WriteColored(" " + EmptyText, Dim);
            return;
        }
        string text = "", marker = " ";
        var color = Fg;
        if (idx < _entries.Count)
        {
            var e = _entries[idx];
            bool selected = idx == sel;
            text = Fit($" {e.Marker} {e.Title}", _leftW - 5);
            color = selected ? Bright : e.Color;
            marker = selected ? "←" : " ";
        }
        ColorHelper.WriteColored(text.PadRight(_leftW - 4), color);
        ColorHelper.WriteColored(marker, Bright);
        Console.Write(" ");
        ColorHelper.WriteColored(arrow, arrowColor);
        Console.Write(" ");
    }

    // “◄ 1/2 ►” in the center of the right column.
    private void WriteDetailArrows()
    {
        string pageText = $" {_detailPage + 1}/{_detailPageCount} ";
        int len = 2 + pageText.Length;
        int pad = Math.Max(0, (_rightW - len) / 2);
        Console.Write(new string(' ', pad));
        var off = ColorHelper.Darker(Fg, 0.5);
        _detailLeftX = DisplayConfig.LeftMargin + 2 + _leftW + pad;
        ColorHelper.WriteColored("◄", _detailPage > 0 ? (_hoveredDetailArrow == -1 ? Bright : Fg) : off);
        ColorHelper.WriteColored(pageText, Dim);
        _detailRightX = _detailLeftX + 1 + pageText.Length;
        ColorHelper.WriteColored("►", _detailPage < _detailPageCount - 1 ? (_hoveredDetailArrow == 1 ? Bright : Fg) : off);
    }

    private List<(string text, List<int> color)> BuildDetailLines(int sel, int width)
    {
        var lines = new List<(string, List<int>)> { ("", Fg) };
        if (sel < 0 || sel >= _entries.Count) return lines;
        foreach (var (text, color) in _entries[sel].Details)
        {
            if (text.Length == 0) { lines.Add(("", color)); continue; }
            foreach (var part in TextWrapper.WrapText(text, Math.Max(10, width))) lines.Add((part, color));
        }
        return lines;
    }

    // Sub-tabs - on the left with an indentation, like a character: “→ TASKS ← [F7]CHARACTERS [F8]BESTIARY".
    private void DrawTabBar()
    {
        // Indentation and spacing are the same as for header tabs (" " before each): [F6] is located exactly below [F1].
        const int gap = 3;
        _tabSpans.Clear();
        var stamps = SectionStamps(settings);
        var seen = SeenSections(stamps);
        seen[(int)Current] = stamps[(int)Current];   // the open subtab is visible
        Console.Write(new string(' ', gap));
        for (int i = 0; i < Sections.Length; i++)
        {
            var (section, label, key) = Sections[i];
            bool active = section == Current;
            int x0 = Term.CursorLeft + 4; // without "[Fn]" - hovering over the key tip does not highlight anything
            var color = active || _hoveredTab == i ? Bright : Fg;
            if (active) ColorHelper.WriteColored($"  → {label} ←", color);
            else
            {
                MouseUiHelper.WriteKeyHint($"[{key}]", display);
                ColorHelper.WriteColored(stamps[i] != seen[i] ? $"{label}• " : $"{label}  ", color);
            }
            _tabSpans.Add((x0, Term.CursorLeft - 1, section));
            if (i < Sections.Length - 1) Console.Write(new string(' ', gap));
        }
    }

    // Image block - only the image of the selected entry (without caption: details are in the right column).
    private void ShowImage(int sel)
    {
        display.NotePage = 0;
        display.NotePageCount = 0;
        if (sel < 0 || sel >= _entries.Count || _entries[sel].Image is not { } image) { MouseUiHelper.CancelPreparedPicture(display); display.SelectedImageLines = null; return; }
        if (!MouseUiHelper.PreparePicture(display, image, () => ShowImage(sel))) return;
        MouseUiHelper.SetSelectedImage(display, image, "", _entries[sel].ImageColor);
    }

    private static string Fit(string s, int w) => s.Length <= w ? s : s[..Math.Max(0, w - 1)] + "…";

    public void Redraw(bool rebuild = true)
    {
        if (_startTop < 0) return;
        bool vis = Term.CursorVisible;
        int sl = Term.CursorLeft, st = Term.CursorTop;
        Term.CursorVisible = false;
        Term.SetCursorPosition(0, _startTop);
        DrawCore(rebuild);
        Term.SetCursorPosition(sl, st);
        Term.CursorVisible = vis;
    }

    // ── Control ── List page (▲ ▼): select - first entry of a new page.
    public void FlipList(int delta)
    {
        int page = _page.GetValueOrDefault(Current), to = Math.Clamp(page + delta, 0, _pageCount - 1);
        if (to == page) return;
        Sound.PlayClick();
        _page[Current] = to;
        _selected[Current] = to * PerPage;
        _pinned[Current] = to * PerPage;
        _detailPage = 0;
        Redraw();
    }

    // Details page (←→, ◄ ►).
    public void Flip(int delta)
    {
        int to = Math.Clamp(_detailPage + delta, 0, _detailPageCount - 1);
        if (to == _detailPage) return;
        Sound.PlayClick();
        _detailPage = to;
        Redraw();
    }

    public void SetSection(Section section)
    {
        if (section == Current) return;
        Current = section;
        _detailPage = 0;
        Redraw();
    }

    // Notes: text from the input field (Enter in the “Notes” section) goes here, not to the wizard.
    public bool TryAddNote(string text)
    {
        if (Current != Section.Notes || string.IsNullOrWhiteSpace(text)) return false;
        PlayerNotes.Add(text, settings.Time.Day, settings.Time.PartOfDay);
        _selected[Current] = 0;
        _pinned[Current] = 0;
        _page[Current] = 0;
        _detailPage = 0;
        Sound.PlayClick();
        Redraw();
        return true;
    }

    // [Del] if input is empty—delete the selected note.
    public void DeleteSelectedNote()
    {
        if (Current != Section.Notes) return;
        int sel = _selected.GetValueOrDefault(Current, -1);
        if (sel < 0 || sel >= _noteIndex.Count) return;
        PlayerNotes.RemoveAt(_noteIndex[sel]);
        _detailPage = 0;
        Sound.PlayClick();
        Redraw();
    }

    public void SetSectionSilently(Section section) { Current = section; _detailPage = 0; }

    // Tab/Shift+Tab - next/previous record (the list page scrolls to it).
    public void SelectNext(int delta)
    {
        if (_entries.Count == 0) return;
        int sel = _selected.GetValueOrDefault(Current, -1);
        sel = sel < 0 ? 0 : (sel + delta + _entries.Count) % _entries.Count;
        _selected[Current] = sel;
        _pinned[Current] = sel;
        _page[Current] = sel / PerPage;
        _detailPage = 0;
        Redraw();
    }

    // Mouse: sub-tabs, ▲ ▼ list, ◄ ► details, hover over entry. true - the picture has changed.
    public bool HandleMouse((short x, short y)? move, (short x, short y)? click)
    {
        if (_startTop < 0) return false;
        int lastRow = _listTop + _rows - 1;
        if (click is { } c)
        {
            if (c.y == _barRow)
            {
                int t = _tabSpans.FindIndex(s => c.x >= s.x0 && c.x <= s.x1);
                if (t >= 0 && _tabSpans[t].section != Current) { Sound.PlayClick(); SetSection(_tabSpans[t].section); return true; }
            }
            if (c.x == _listArrowX && c.y == _listTop) { FlipList(-1); return true; }
            if (c.x == _listArrowX && c.y == lastRow) { FlipList(1); return true; }
            if (_detailPageCount > 1 && c.y == lastRow && c.x == _detailLeftX) { Flip(-1); return false; }
            if (_detailPageCount > 1 && c.y == lastRow && c.x == _detailRightX) { Flip(1); return false; }
            // Click on an entry to pin it.
            int crow = c.y - _listTop, cx0 = DisplayConfig.LeftMargin + 1;
            int cidx = _page.GetValueOrDefault(Current) * PerPage + crow;
            if (crow >= 0 && crow < PerPage && c.x >= cx0 && c.x < cx0 + _leftW - 3 && cidx < _entries.Count)
            {
                Sound.PlayClick();
                _pinned[Current] = cidx;
                if (_selected.GetValueOrDefault(Current, -1) != cidx) { _selected[Current] = cidx; _detailPage = 0; Redraw(false); return true; }
                return false;
            }
        }
        if (move is not { } m) return false;
        int ht = m.y == _barRow ? _tabSpans.FindIndex(s => m.x >= s.x0 && m.x <= s.x1) : -1;
        int hl = _pageCount > 1 && m.x == _listArrowX ? (m.y == _listTop ? -1 : m.y == lastRow ? 1 : 0) : 0;
        int hd = _detailPageCount > 1 && m.y == lastRow ? (m.x == _detailLeftX ? -1 : m.x == _detailRightX ? 1 : 0) : 0;
        bool changed = false;
        if (ht != _hoveredTab || hl != _hoveredListArrow || hd != _hoveredDetailArrow)
        {
            _hoveredTab = ht; _hoveredListArrow = hl; _hoveredDetailArrow = hd;
            Redraw(false);
        }
        // Record under the cursor (left column, except arrows).
        int row = m.y - _listTop, leftX0 = DisplayConfig.LeftMargin + 1;
        int idx = _page.GetValueOrDefault(Current) * PerPage + row;
        bool overEntry = hl == 0 && row >= 0 && row < PerPage && m.x >= leftX0 && m.x < leftX0 + _leftW - 3 && idx < _entries.Count;
        // Hover—show the entry under the cursor; left the posts - return the pinned one.
        int want = overEntry ? idx : _pinned.GetValueOrDefault(Current, -1);
        if (want >= 0 && want < _entries.Count && want != _selected.GetValueOrDefault(Current, -1))
        {
            _selected[Current] = want;
            _page[Current] = want / PerPage;
            _detailPage = 0;
            Redraw(false);
            changed = true;
        }
        ConsoleMouseReader.SetCursorShape(ht >= 0 || hl != 0 || hd != 0 || overEntry);
        return changed;
    }

    // ── New in the journal (label “JOURNAL•” in the tab and “•” in the subtab) ──
    public static string Stamp(WorldState ws) => string.Join("|", SectionStamps(ws).Take(4));

    // Fingerprint of the contents of each subtab (the order of Sections): has changed - there is something new.
    private static string[] SectionStamps(WorldState ws)
    {
        var n = ws.Narrative;
        return
        [
            n == null ? "" : n.CurrentArc + "#" + string.Join(";", (n.PlotThreads ?? []).Where(t => t.Deleted != true && t.Hidden != true).Select(t => $"{t.Name}:{t.Status}:{t.Steps?.Count ?? 0}")),
            n == null ? "" : string.Join(";", (n.Npcs ?? []).Where(p => p.Deleted != true && (p.Met == true || p.Memory is { Count: > 0 })).Select(p => $"{p.Name}:{p.Memory?.Count ?? 0}")),
            string.Join(";", ws.World?.Known ?? []) + "#" + string.Join(";", ws.World?.Visited ?? []),
            n == null ? "" : string.Join(";", n.KnownMonsters ?? []),
            PlayerNotes.All.Count(x => x.ByMaster).ToString(),   // the note was added by the GM
        ];
    }

    // What you have already seen in the sub-tabs - about the game (save location); the first show of the magazine - everything is “seen”.
    private static readonly Dictionary<int, string[]> _seenBySlot = [];

    private string[] SeenSections(string[] now)
    {
        if (!_seenBySlot.TryGetValue(Storage.ActiveSlot, out var seen) || seen.Length != now.Length)
            _seenBySlot[Storage.ActiveSlot] = seen = (string[])now.Clone();
        return seen;
    }
}
