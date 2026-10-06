using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

// Журнал героя ([F3]) — как экран персонажа: сразу под заголовком подвкладки слева (Задания / Персонажи /
// Бестиарий, F6–F8), под ними две колонки: слева список записей (выбор — Tab/наведение, маркер «←»,
// страницы — ▲ ▼ у края), справа подробности выбранной (страницы — «◄ 1/2 ►» внизу, ←→). Блок картинок — только картинка записи. Данные — narrative
// (currentArc, plotThreads со steps, npcs, knownMonsters), их ведёт ИИ.
public class JournalDisplay(WorldState settings, DisplayConfig display)
{
    public enum Section { Quests, People, Locations, Bestiary, Notes }

    private static readonly (Section section, string label, string key)[] Sections =
        [(Section.Quests, "ЗАДАНИЯ", "F6"), (Section.People, "ПЕРСОНАЖИ", "F7"), (Section.Locations, "ЛОКАЦИИ", "F8"),
         (Section.Bestiary, "БЕСТИАРИЙ", "F9"), (Section.Notes, "ЗАМЕТКИ", "F10")];

    private const string NotesHint = "Текст внизу + Enter — заметка";

    // Раздел «Заметки»: запись списка → индекс в PlayerNotes.All (новые сверху).
    private List<int> _noteIndex = [];

    public Section Current { get; private set; } = Section.Quests;
    private readonly Dictionary<Section, int> _page = [];
    private readonly Dictionary<Section, int> _selected = [];
    // Закреплённая запись (клик мышью / Tab, как у предметов инвентаря): наведение показывает другую
    // временно, курсор ушёл с записи — снова закреплённая.
    private readonly Dictionary<Section, int> _pinned = [];

    private sealed record Entry(string Title, string Marker, List<int> Color, List<(string text, List<int> color)> Details, string? Image, List<int>? ImageColor);

    private List<Entry> _entries = [];
    private int _startTop = -1, _listTop, _rows, _pageCount = 1, _leftW, _rightW;
    private readonly List<(int x0, int x1, Section section)> _tabSpans = [];
    private int _barRow;
    private int _hoveredTab = -1;
    // Листание списка — ▲ (первая строка) / ▼ (последняя) у правого края колонки списка.
    private int _listArrowX = -1, _hoveredListArrow;
    // Страницы подробностей — «◄ 1/2 ►» внизу по центру правой колонки.
    private int _detailPage, _detailPageCount = 1, _detailLeftX = -1, _detailRightX = -1, _hoveredDetailArrow;

    private List<int> Fg => display.MainForeground;
    private List<int> Bright => ColorHelper.Pale(display.MainForeground, 0.75);
    private List<int> Dim => ColorHelper.Darker(display.MainForeground, 0.45);

    // ── Записи раздела ──
    private List<Entry> BuildEntries(Section section)
    {
        var list = new List<Entry>();
        var n = settings.Narrative;
        switch (section)
        {
            case Section.Quests:
                // Текущая цель — первой записью.
                if (n?.CurrentArc is { Length: > 0 } arc)
                    list.Add(new Entry("Текущая цель", "»", Bright, [("Текущая цель", Bright), ("", Fg), (arc, Fg)], "lorc/scroll-unfurled", null));
                // Только то, что знает герой: скрытые нити и заметки мастера (dmNotes) в журнал не попадают.
                var threads = (n?.PlotThreads ?? []).Where(t => t.Deleted != true && t.Hidden != true).ToList();
                foreach (var t in threads.Where(IsActive).Concat(threads.Where(t => !IsActive(t))))
                {
                    bool active = IsActive(t);
                    bool failed = t.Status?.StartsWith("провал", StringComparison.OrdinalIgnoreCase) == true;
                    var details = new List<(string, List<int>)> { (t.Name ?? "Задание", Bright), ($"Статус: {t.Status ?? "активна"}", Dim), ("", Fg) };
                    if (!string.IsNullOrWhiteSpace(t.Description)) details.Add((t.Description, Fg));
                    if (t.Steps is { Count: > 0 } steps)
                    {
                        details.Add(("", Fg));
                        details.Add(("Ход задания:", Dim));
                        details.AddRange(steps.Select(s => ("· " + s, Fg)));
                    }
                    list.Add(new Entry(t.Name ?? "Задание", active ? "▸" : failed ? "✗" : "✓", active ? Fg : Dim, details, "lorc/scroll-unfurled", null));
                }
                break;

            case Section.People:
                // Только встреченные героем (met или есть память разговоров).
                foreach (var p in (n?.Npcs ?? []).Where(p => p.Deleted != true && (p.Met == true || p.Memory is { Count: > 0 })))
                {
                    var color = AttitudeColor(p.Attitude);
                    var onMap = (settings.Map.Entities ?? []).FirstOrDefault(e => e.Deleted != true && string.Equals(e.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                    var details = new List<(string, List<int>)> { (p.Name ?? "?", color) };
                    if (!string.IsNullOrWhiteSpace(p.Attitude)) details.Add(($"Отношение: {p.Attitude}", Dim));
                    details.Add(("", Fg));
                    if (!string.IsNullOrWhiteSpace(p.Notes)) details.Add((p.Notes, Fg));
                    if (p.Memory is { Count: > 0 } memory)
                    {
                        details.Add(("", Fg));
                        details.Add(("Запомнилось:", Dim));
                        details.AddRange(memory.Select(m => ("· " + m, Fg)));
                    }
                    list.Add(new Entry(p.Name ?? "?", "●", color, details, onMap?.Image ?? "delapouite/person", onMap?.Color ?? color));
                }
                break;

            // Места карты мира, известные герою: где он сейчас — первым, потом где бывал, потом о каких слышал.
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
                        string kingdom = p.Kingdom >= 0 && p.Kingdom < world.Kingdoms.Count ? $"королевство {world.Kingdoms[p.Kingdom].Name}" : "ничья земля";
                        var details = new List<(string, List<int>)>
                        {
                            (p.Name, Bright),
                            ($"{WorldPlaceTypes.Label(p.Type)} · {kingdom} · {MapGen.Generators.WorldAtlas.Compass(world, p.X, p.Y)}", Dim),
                            (here ? "Вы здесь" : visited ? "Бывали здесь" : "Знаете понаслышке", Dim),
                            ("", Fg),
                        };
                        if (!string.IsNullOrWhiteSpace(p.Description)) details.Add((p.Description, Fg));
                        if (!here && hero is { } h && MapGen.Generators.WorldAtlas.Route(world, h, (p.X, p.Y)) is { } r)
                        {
                            details.Add(("", Fg));
                            details.Add(($"Отсюда: {MapGen.Generators.WorldAtlas.Direction(p.X - h.x, p.Y - h.y)}, {r.Summary}", Dim));
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
                    var details = new List<(string, List<int>)> { (info?.Name ?? key, Bright) };
                    if (info != null)
                    {
                        string kind = string.Join(", ", new[] { info.Size, info.Type, info.Alignment }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        if (kind.Length > 0) details.Add((kind, Dim));
                        details.Add(("", Fg));
                        details.Add(($"КД {info.Ac} · ХП {info.Hp} · скорость {info.Speed}", Fg));
                        details.Add(($"СИЛ {info.Str}  ЛОВ {info.Dex}  ТЕЛ {info.Con}  ИНТ {info.Int}  МДР {info.Wis}  ХАР {info.Cha}", Fg));
                        if (!string.IsNullOrWhiteSpace(info.Senses)) details.Add(($"Чувства: {info.Senses}", Dim));
                        if (!string.IsNullOrWhiteSpace(info.Languages)) details.Add(($"Языки: {info.Languages}", Dim));
                    }
                    list.Add(new Entry(info?.Name ?? key, "♦", Fg, details, onMap?.Image, onMap?.Color));
                }
                break;

            // Свои заметки игрока — из отдельного файла (PlayerNotes), мастеру не передаются.
            case Section.Notes:
                var notes = PlayerNotes.All;
                _noteIndex = Enumerable.Range(0, notes.Count).Reverse().ToList();
                foreach (int ni in _noteIndex)
                {
                    var note = notes[ni];
                    string when = $"День {note.Day}" + (string.IsNullOrEmpty(note.PartOfDay) ? "" : $" · {note.PartOfDay}");
                    string firstLine = note.Text.Split('\n')[0];
                    if (note.ByMaster) when += " · записал мастер";
                    var details = new List<(string, List<int>)> { (when, Dim), ("", Fg), (note.Text, Fg), ("", Fg), ("[Del] — удалить заметку", Dim) };
                    list.Add(new Entry(firstLine, note.ByMaster ? "✦" : "✎", Fg, details, "lorc/quill-ink", null));
                }
                break;
        }
        return list;
    }

    private static bool IsActive(PlotThread t) =>
        t.Status is null || !(t.Status.StartsWith("заверш", StringComparison.OrdinalIgnoreCase)
            || t.Status.StartsWith("провал", StringComparison.OrdinalIgnoreCase));

    private List<int> AttitudeColor(string? attitude)
    {
        string a = attitude?.ToLowerInvariant() ?? "";
        if (a.Contains("враж")) return [214, 110, 98];
        if (a.Contains("друж") || a.Contains("союз")) return [130, 196, 120];
        if (a.Contains("нейтр")) return [206, 186, 128];
        return Bright;
    }

    // Записей на странице списка: в «Заметках» последняя строка — подсказка, как добавить заметку.
    private int PerPage => Current == Section.Notes ? Math.Max(1, _rows - 1) : _rows;

    private string EmptyText => Current switch
    {
        Section.Quests => "Заданий пока нет.",
        Section.People => "Знакомств пока нет.",
        Section.Notes => "Заметок пока нет.",
        Section.Locations => "Известных мест пока нет.",
        _ => "Опознанных существ пока нет.",
    };

    // ── Отрисовка ──
    public void Draw()
    {
        _startTop = Console.CursorTop;
        int inner = display.InnerWidth(display.ViewCols(settings.Map));
        var border = new BorderDrawer(settings, display);
        _entries = BuildEntries(Current);
        _rows = Math.Max(3, display.MaxHigh - 3);
        _pageCount = Math.Max(1, (_entries.Count + PerPage - 1) / PerPage);
        int page = Math.Clamp(_page.GetValueOrDefault(Current), 0, _pageCount - 1);
        _page[Current] = page;
        int sel = _selected.GetValueOrDefault(Current, -1);
        if (sel >= _entries.Count) sel = _entries.Count - 1;
        if (sel < 0 && _entries.Count > 0) sel = page * PerPage;
        _selected[Current] = sel;

        border.DrawSeparator();
        _barRow = Console.CursorTop;
        border.DrawContentLine(DrawTabBar);

        _leftW = Math.Max(24, inner * 2 / 5);
        _rightW = inner - _leftW - 1;
        border.DrawSeparatorWith2Parts('─', _leftW, _rightW);
        _listTop = Console.CursorTop;
        _listArrowX = DisplayConfig.LeftMargin + 1 + _leftW - 2;

        // Подробности по страницам: не влезли — последняя строка колонки под «◄ 1/2 ►».
        var all = BuildDetailLines(sel, _rightW - 4);
        int perPage = all.Count > _rows ? _rows - 1 : _rows;
        _detailPageCount = Math.Max(1, (all.Count + perPage - 1) / perPage);
        _detailPage = Math.Clamp(_detailPage, 0, _detailPageCount - 1);
        var details = all.Skip(_detailPage * perPage).Take(perPage).ToList();

        for (int r = 0; r < _rows; r++)
        {
            int idx = page * PerPage + r, row = r;
            border.DrawContentLine2Columns(
                () => WriteListRow(idx, sel, row, page),
                () =>
                {
                    if (_detailPageCount > 1 && row == _rows - 1) WriteDetailArrows();
                    else if (row < details.Count) { Console.Write("  "); ColorHelper.WriteColored(details[row].text, details[row].color); }
                },
                _leftW, _rightW);
        }
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
        // ▲ / ▼ — в первой и последней строке у правого края, если записей больше страницы.
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

    // «◄ 1/2 ►» по центру правой колонки.
    private void WriteDetailArrows()
    {
        string pageText = $" {_detailPage + 1}/{_detailPageCount} ";
        int len = 2 + pageText.Length;
        int pad = Math.Max(0, (_rightW - len) / 2);
        Console.Write(new string(' ', pad));
        var off = ColorHelper.Darker(Fg, 0.5);
        _detailLeftX = Console.CursorLeft;
        ColorHelper.WriteColored("◄", _detailPage > 0 ? (_hoveredDetailArrow == -1 ? Bright : Fg) : off);
        ColorHelper.WriteColored(pageText, Dim);
        _detailRightX = Console.CursorLeft;
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

    // Подвкладки — слева с отступом, как у персонажа: «  → ЗАДАНИЯ ←    [F7]ПЕРСОНАЖИ    [F8]БЕСТИАРИЙ».
    private void DrawTabBar()
    {
        // Отступ и промежутки — как у вкладок заголовка ("   " перед каждой): [F6] стоит ровно под [F1].
        const int gap = 3;
        _tabSpans.Clear();
        var stamps = SectionStamps(settings);
        var seen = SeenSections(stamps);
        seen[(int)Current] = stamps[(int)Current];   // открытую подвкладку видим
        Console.Write(new string(' ', gap));
        for (int i = 0; i < Sections.Length; i++)
        {
            var (section, label, key) = Sections[i];
            bool active = section == Current;
            int x0 = Console.CursorLeft + 4; // без «[Fn]» — наведение на подсказку клавиши ничего не подсвечивает
            var color = active || _hoveredTab == i ? Bright : Fg;
            if (active) ColorHelper.WriteColored($"  → {label} ←", color);
            else
            {
                MouseUiHelper.WriteKeyHint($"[{key}]", display);
                ColorHelper.WriteColored(stamps[i] != seen[i] ? $"{label}• " : $"{label}  ", color);
            }
            _tabSpans.Add((x0, Console.CursorLeft - 1, section));
            if (i < Sections.Length - 1) Console.Write(new string(' ', gap));
        }
    }

    // Блок картинок — только картинка выбранной записи (без подписи: подробности — в правой колонке).
    private void ShowImage(int sel)
    {
        display.NotePage = 0;
        display.NotePageCount = 0;
        if (sel < 0 || sel >= _entries.Count || _entries[sel].Image is not { } image) { display.SelectedImageLines = null; return; }
        MouseUiHelper.SetSelectedImage(display, image, "", _entries[sel].ImageColor);
    }

    private static string Fit(string s, int w) => s.Length <= w ? s : s[..Math.Max(0, w - 1)] + "…";

    public void Redraw()
    {
        if (_startTop < 0) return;
        bool vis = Console.CursorVisible;
        int sl = Console.CursorLeft, st = Console.CursorTop;
        Console.CursorVisible = false;
        Console.SetCursorPosition(0, _startTop);
        Draw();
        Console.SetCursorPosition(sl, st);
        Console.CursorVisible = vis;
    }

    // ── Управление ──
    // Страница списка (▲ ▼): выбор — первая запись новой страницы.
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

    // Страница подробностей (←→, ◄ ►).
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

    // Заметки: текст из поля ввода (Enter на разделе «Заметки») — сюда, а не мастеру.
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

    // [Del] при пустом вводе — удалить выбранную заметку.
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

    // Tab/Shift+Tab — следующая/предыдущая запись (страница списка листается к ней).
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

    // Мышь: подвкладки, ▲ ▼ списка, ◄ ► подробностей, наведение на запись. true — сменилась картинка.
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
            // Клик по записи — закрепить её.
            int crow = c.y - _listTop, cx0 = DisplayConfig.LeftMargin + 1;
            int cidx = _page.GetValueOrDefault(Current) * PerPage + crow;
            if (crow >= 0 && crow < PerPage && c.x >= cx0 && c.x < cx0 + _leftW - 3 && cidx < _entries.Count)
            {
                Sound.PlayClick();
                _pinned[Current] = cidx;
                if (_selected.GetValueOrDefault(Current, -1) != cidx) { _selected[Current] = cidx; _detailPage = 0; Redraw(); return true; }
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
            Redraw();
        }
        // Запись под курсором (левая колонка, кроме стрелок).
        int row = m.y - _listTop, leftX0 = DisplayConfig.LeftMargin + 1;
        int idx = _page.GetValueOrDefault(Current) * PerPage + row;
        bool overEntry = hl == 0 && row >= 0 && row < PerPage && m.x >= leftX0 && m.x < leftX0 + _leftW - 3 && idx < _entries.Count;
        // Наведение — показать запись под курсором; ушли с записей — вернуть закреплённую.
        int want = overEntry ? idx : _pinned.GetValueOrDefault(Current, -1);
        if (want >= 0 && want < _entries.Count && want != _selected.GetValueOrDefault(Current, -1))
        {
            _selected[Current] = want;
            _page[Current] = want / PerPage;
            _detailPage = 0;
            Redraw();
            changed = true;
        }
        ConsoleMouseReader.SetCursorShape(ht >= 0 || hl != 0 || hd != 0 || overEntry);
        return changed;
    }

    // ── Новое в журнале (метка «ЖУРНАЛ•» во вкладке и «•» у подвкладки) ──
    public static string Stamp(WorldState ws) => string.Join("|", SectionStamps(ws).Take(4));

    // Отпечаток содержимого каждой подвкладки (порядок Sections): изменился — там новое.
    private static string[] SectionStamps(WorldState ws)
    {
        var n = ws.Narrative;
        return
        [
            n == null ? "" : n.CurrentArc + "#" + string.Join(";", (n.PlotThreads ?? []).Where(t => t.Deleted != true && t.Hidden != true).Select(t => $"{t.Name}:{t.Status}:{t.Steps?.Count ?? 0}")),
            n == null ? "" : string.Join(";", (n.Npcs ?? []).Where(p => p.Deleted != true && (p.Met == true || p.Memory is { Count: > 0 })).Select(p => $"{p.Name}:{p.Memory?.Count ?? 0}")),
            string.Join(";", ws.World?.Known ?? []) + "#" + string.Join(";", ws.World?.Visited ?? []),
            n == null ? "" : string.Join(";", n.KnownMonsters ?? []),
            PlayerNotes.All.Count(x => x.ByMaster).ToString(),   // заметку дописал мастер
        ];
    }

    // Что в подвкладках уже видели — по игре (место сохранения); первый показ журнала — всё «видено».
    private static readonly Dictionary<int, string[]> _seenBySlot = [];

    private string[] SeenSections(string[] now)
    {
        if (!_seenBySlot.TryGetValue(Storage.ActiveSlot, out var seen) || seen.Length != now.Length)
            _seenBySlot[Storage.ActiveSlot] = seen = (string[])now.Clone();
        return seen;
    }
}
