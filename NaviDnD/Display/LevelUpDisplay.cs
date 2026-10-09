using NaviDnD.Clients;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

// «Новый уровень» — на вкладке «Персонаж» вместо диалогового окна (F10 / кнопка на карточке). Сверху остаётся карточка
// героя: она показывает героя после повышения, изменения — золотым (HeroDisplay.Preview), подвкладки F6–F9 листаются.
// Внизу слева — выбор (хиты, улучшение характеристик, варианты мастера), справа в блоке картинки — карточка заклинания
// из справочника или описание пункта под курсором, иначе портрет героя. Строка ввода — просьба к мастеру о своём
// варианте: мастер добавляет его в группу («★») или отказывает — героя напрямую никто не правит. «Принять» — мастер
// вносит выбранное в лист (LevelUp.Apply). Отмена ничего не меняет; выбор и бросок хитов помнятся до принятия.
public class LevelUpDisplay
{
    private readonly WorldState _settings;
    private readonly DisplayConfig _display;
    private readonly GameAiClient _ai;
    private readonly Storage _storage;
    private readonly BorderDrawer _border;
    private LevelUp? _session;
    private Task<string?>? _planTask;   // варианты мастера для _session (вышел и вернулся, пока он думает, — тот же запрос)

    private readonly List<(int y, int x, int w, string id)> _hits = [];
    private string? _hovered, _titleHover;
    private bool _frozen;

    private List<int> Fg => _display.MainForeground;
    private List<int> Bright => ColorHelper.Pale(_display.MainForeground, 0.75);
    private List<int> Dim => ColorHelper.Darker(_display.MainForeground, 0.5);
    private static readonly List<int> Gold = [235, 190, 80];

    public LevelUpDisplay(WorldState settings, DisplayConfig display, GameAiClient ai, Storage storage)
    {
        _settings = settings;
        _storage = storage;
        _display = display;
        _ai = ai;
        _border = new BorderDrawer(settings, display);
    }

    // Строка списка: заголовок раздела или пояснение (не выбираются) либо пункт; Activate — Enter/клик;
    // Spell — заклинание справочника (справа его карточка).
    private sealed record Row(string Text, List<int> Color, string? Description = null, Action? Activate = null,
        bool Header = false, HeroSpell? Spell = null);

    // Содержимое блока картинки: строки, цвет (первой строки или всех), текст — с верха, картинка — по центру.
    private sealed record Panel(string[] Lines, List<int>? Color = null, bool ColorAll = false, bool Top = false);

    // null — назад на карточку героя; иначе команда заголовка («F1»…«F5», «Esc»).
    public async Task<string?> Run(string title, HeroDisplay heroDisplay)
    {
        var hero = _settings.Hero;
        if (hero == null || !LevelUp.Available(_settings)) return null;
        if (_session is not { } s || s.Hero != hero || s.From != LevelRules.LevelOf(hero))
        {
            _session = s = new LevelUp(hero, LevelRules.Current);
            _planTask = null;
        }

        MouseUiHelper.SetDefaultImage(_display, hero.Image, hero.Color);
        int startTop = Console.CursorTop, panelTop = startTop;
        if (!s.PlanLoaded && _planTask == null) _planTask = _ai.LevelUpOptions(s);
        Task<string?>? customTask = null;
        Task<Hero?>? applyTask = null;
        string message = "", request = "";
        string? masterText = null;   // ответ мастера на просьбу — целиком в блоке картинки, пока не выбрал другое
        int focus = 0, scroll = 0, spin = 0, notePage = 0;
        int? lastHoverRow = null;
        string? describedKey = null;
        var pages = new Dictionary<string, int>();   // страница карточки заклинания/записки — у каждой своя
        string? pinnedCard = null, shownCard = null;   // предмет/заклинание карточки («inv:3», «spell:1») — в блоке картинки
        bool follow = true, toAccept = false, cardDirty = true;
        bool inputActive = false;   // строка ввода в фокусе (клик по ней или печать) — тогда в ней курсор

        try
        {
            while (true)
            {
                // ── Ответы мастера ──
                if (_planTask is { IsCompleted: true } done)
                {
                    if (!done.IsCompletedSuccessfully || done.Result is not { } plan || !s.LoadPlan(plan)) message = SetupError(done) ?? L.T("Мастер не ответил — нажми «Повторить».");
                    else SaveDraft(s);
                    _planTask = null;
                }
                if (customTask is { IsCompleted: true })
                {
                    message = customTask.IsCompletedSuccessfully && customTask.Result is { } json
                        ? s.MergeCustom(json) ?? SaveDraft(s, L.T("Мастер добавил вариант — он отмечен ★."))
                        : SetupError(customTask) ?? L.T("Мастер не ответил — попробуй ещё раз.");
                    masterText = message;
                    customTask = null;
                }
                if (applyTask is { IsCompleted: true })
                {
                    if (applyTask.IsCompletedSuccessfully && applyTask.Result is { } leveled)
                    {
                        Accept(s, leveled);
                        return null;
                    }
                    message = SetupError(applyTask) ?? L.T("Мастер не ответил — попробуй ещё раз.");
                    applyTask = null;
                }
                // Пока мастер вносит выбор или думает над просьбой — ничего не нажимается; пока готовит варианты —
                // хиты и характеристики уже можно.
                _frozen = applyTask != null || customTask != null;
                bool loading = _planTask != null;

                // ── Карточка героя — после повышения, изменения золотым ──
                if (cardDirty)
                {
                    heroDisplay.PreviewBase = hero;
                    heroDisplay.Preview = s.Preview();
                    Console.CursorVisible = false;
                    Console.SetCursorPosition(0, startTop);
                    heroDisplay.DrawHeroCard();
                    panelTop = Console.CursorTop;
                    cardDirty = false;
                }

                // ── Панель ──
                int innerW = _display.InnerWidth(_display.ViewCols(_settings.Map));
                int rightW = _display.DialogRightPanelWidth, leftW = rightW > 0 ? innerW - 1 - rightW : innerW;
                int listH = Math.Max(3, _display.MaxHistoryLines - 2);   // последние строки — кнопки
                var rows = ChoiceRows(s, loading, spin, leftW - 4);
                var focusable = Enumerable.Range(0, rows.Count).Where(i => rows[i].Activate != null).ToList();
                var buttons = new List<(string id, string text, bool enabled)>
                {
                    s.PlanLoaded || loading ? ("accept", L.T("ПРИНЯТЬ"), s.Ready) : ("retry", L.T("ПОВТОРИТЬ"), true),
                    ("cancel", L.T("ОТМЕНА"), true),
                };
                int focusCount = focusable.Count + buttons.Count;
                if (toAccept) { focus = focusable.Count; toAccept = false; }
                focus = Math.Clamp(focus, 0, Math.Max(0, focusCount - 1));
                int? focusRow = focus < focusable.Count ? focusable[focus] : null;
                if (follow && focusRow is int fr)
                {
                    if (fr < scroll) scroll = Math.Max(0, fr - 1);
                    if (fr >= scroll + listH) scroll = fr - listH + 1;
                }
                scroll = Math.Clamp(scroll, 0, Math.Max(0, rows.Count - listH));

                // Блок картинки: под мышью — то, что под ней (пункт выбора, предмет или заклинание карточки); мышь ни на
                // чём (ушла листать ◄ ►) — закреплённое: ответ мастера, предмет карточки по клику или выбранный пункт.
                int? hoveredRow = _hovered?.StartsWith("row:") == true ? int.Parse(_hovered[4..]) : null;
                string? hoveredCard = _hovered is { } hv && (hv.StartsWith("inv:") || hv.StartsWith("spell:")) ? hv : null;
                if (hoveredRow != null && hoveredRow != lastHoverRow) masterText = null;
                lastHoverRow = hoveredRow;
                bool showMaster = masterText != null && hoveredRow == null && hoveredCard == null;
                string? cardItem = hoveredRow != null || showMaster ? null : hoveredCard ?? pinnedCard;
                var described = hoveredRow is int hrw && hrw < rows.Count ? rows[hrw] : focusRow is int frw ? rows[frw] : null;
                string? shownKey = showMaster ? "master" : cardItem ?? described?.Text;
                if (shownKey != describedKey)
                {
                    if (describedKey != null) pages[describedKey] = notePage;
                    describedKey = shownKey;
                    notePage = shownKey != null ? pages.GetValueOrDefault(shownKey) : 0;
                }
                if (cardItem != null && $"{cardItem}#{notePage}" != shownCard)
                {
                    shownCard = $"{cardItem}#{notePage}";
                    ShowCardItem(cardItem, heroDisplay, notePage);
                    notePage = _display.NotePage;
                }
                if (cardItem == null && shownCard != null)
                {
                    shownCard = null;
                    _display.SelectedInventoryIndex = _display.SelectedSpellIndex = -1;
                    heroDisplay.RefreshSelection();
                }
                // Мастер думает — в строке ввода (печатать нельзя): готовит варианты или отвечает на просьбу.
                string? busy = applyTask != null ? Spinner.Frames[spin % Spinner.Frames.Length] + " " + L.T("Мастер вносит изменения в лист героя…")
                    : customTask != null ? Spinner.Frames[spin % Spinner.Frames.Length] + " " + L.T("Мастер думает над твоей просьбой…")
                    : loading ? Spinner.Frames[spin % Spinner.Frames.Length] + " " + L.T("Мастер готовит варианты…") : null;
                string status = applyTask != null ? ""
                    : message.Length > 0 ? message
                    : !s.Ready && s.PlanLoaded ? L.F("Осталось выбрать: {0}", string.Join(", ", Missing(s))) : "";
                var right = cardItem != null ? new Panel(_display.SelectedImageLines ?? _display.DefaultImageLines ?? [],
                        _display.SelectedImageColor, !_display.SelectedImageColorTitleOnly)
                    : showMaster ? MasterPanel(masterText!, rightW) : RightPanel(described, rightW, notePage);
                if (cardItem == null && !showMaster && described?.Spell != null) notePage = _display.NotePage;
                DrawPanel(panelTop, s, rows, scroll, listH, focusRow, focus - focusable.Count, buttons, status,
                    right, request, busy, inputActive, leftW, rightW);

                // ── Ввод ──
                var input = await WaitInput(_frozen || loading ? 120 : 0, title, heroDisplay);
                if (_frozen || loading) spin++;
                if (input == null) continue;
                var (key, click) = input.Value;
                string? act = null;
                if (click != null)
                {
                    if (click.StartsWith("title:")) return click[6..];
                    if (click.StartsWith("card:")) { if (!_frozen) cardDirty = CardCommand(click[5..], heroDisplay); continue; }
                    if (click == "input") { if (busy == null) inputActive = true; continue; }
                    if (click.StartsWith("pin:"))
                    {
                        string item = click[4..];
                        if (pinnedCard != item) Sound.PlayClick();
                        pinnedCard = item;
                        continue;
                    }
                    if (click.StartsWith("note:"))
                    {
                        Sound.PlayClick();
                        notePage = Math.Max(0, notePage + int.Parse(click[5..]));
                        continue;
                    }
                    if (click.StartsWith("wheel:"))
                    {
                        scroll = Math.Max(0, scroll + Math.Sign(int.Parse(click.Split(':')[1])) * -3);
                        follow = false;
                        continue;
                    }
                    inputActive = false;
                    if (click.StartsWith("row:") && int.Parse(click[4..]) is int ci && focusable.IndexOf(ci) is int fi and >= 0)
                    {
                        // Выбранный кликом пункт — закреплён: уведёшь мышь, его описание останется справа.
                        pinnedCard = null;
                        masterText = null;
                        focus = fi;
                        act = "row";
                    }
                    else act = click;
                }
                else if (key is { } k)
                {
                    follow = true;
                    if (_frozen && k.Key != ConsoleKey.Escape) continue;
                    if (k.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Tab)
                    {
                        // Клавиши ведут выбор сами: стрелка и описание — у выбранного, а не у пункта под мышью
                        // (до следующего движения мыши).
                        _hovered = null;
                        masterText = null;
                        pinnedCard = null;
                        inputActive = false;
                    }
                    switch (k.Key)
                    {
                        case ConsoleKey.Escape when inputActive:
                            inputActive = false;
                            continue;
                        case ConsoleKey.PageUp: notePage = Math.Max(0, notePage - 1); continue;
                        case ConsoleKey.PageDown: notePage++; continue;
                        case ConsoleKey.Escape:
                            if (_frozen) continue;
                            Sound.PlayClick();
                            return null;
                        case ConsoleKey.UpArrow: focus = (focus - 1 + focusCount) % Math.Max(1, focusCount); continue;
                        case ConsoleKey.DownArrow:
                        case ConsoleKey.Tab: focus = (focus + 1) % Math.Max(1, focusCount); continue;
                        case ConsoleKey.LeftArrow: cardDirty = CardCommand("Left", heroDisplay); continue;
                        case ConsoleKey.RightArrow: cardDirty = CardCommand("Right", heroDisplay); continue;
                        case >= ConsoleKey.F6 and <= ConsoleKey.F9: cardDirty = CardCommand(k.Key.ToString(), heroDisplay); continue;
                        case ConsoleKey.Backspace:
                            if (inputActive && request.Length > 0) request = request[..^1];
                            continue;
                        case ConsoleKey.Enter when inputActive && request.Trim().Length > 0:
                            act = "ask";
                            break;
                        case ConsoleKey.Enter:
                        case ConsoleKey.Spacebar when !inputActive:
                            act = focus < focusable.Count ? "row" : buttons[focus - focusable.Count].id;
                            break;
                        default:
                            if (busy != null) continue;   // мастер думает — строка ввода занята
                            inputActive = true;           // печать — в строку ввода
                            if (ClipboardText.IsPasteKey(k))
                                request += ClipboardText.Get().Replace("\r", " ").Replace("\n", " ");
                            else if (!char.IsControl(k.KeyChar) && request.Length < 200)
                                request += k.KeyChar;
                            continue;
                    }
                }
                if (act == null || _frozen) continue;

                // ── Действие ──
                if (act == "row" && focus < focusable.Count && rows[focusable[focus]].Activate is { } activate)
                {
                    Sound.PlayClick();
                    message = "";
                    activate();
                    cardDirty = true;
                    toAccept = s.Ready;   // всё выбрано — фокус на «Принять»
                    continue;
                }
                if (act == "ask")
                {
                    if (!s.PlanLoaded) continue;
                    Sound.PlayClick();
                    message = "";
                    customTask = _ai.LevelUpCustom(s, request);
                    request = "";
                    continue;
                }
                var button = buttons.FirstOrDefault(b => b.id == act);
                if (button.id == null || !button.enabled) continue;
                Sound.PlayClick();
                message = "";
                switch (act)
                {
                    case "cancel": return null;
                    case "retry": _planTask = _ai.LevelUpOptions(s); break;
                    case "accept": applyTask = _ai.LevelUpApply(s); break;
                }
            }
        }
        finally
        {
            heroDisplay.Preview = null;
            heroDisplay.PreviewBase = null;
            _display.SelectedImageLines = null;
            _display.SelectedImageColor = null;
            _display.NotePage = 0;
            _display.NotePageCount = 0;
        }
    }

    // Новый герой вместо старого; в историю — что получено.
    private void Accept(LevelUp s, Hero leveled)
    {
        var got = new List<string> { L.F("хиты +{0}", s.HpGain) };
        got.AddRange(s.AsiPicks.Distinct().Select(a => $"{AbilityNames.Find(s.Hero, a)?.Name ?? a} {s.NewScore(a)}"));
        got.AddRange(s.Gains.Select(g => g.Name));
        got.AddRange(s.VisibleChoices.SelectMany(c => c.Picked.Order().Select(i => c.Options[i].Name)));
        _settings.Hero = leveled;
        _settings.History ??= [];
        _settings.History.Add(new DialogMessage { Text = L.WF("[Новый уровень: {0} {1} — {2}]", s.ClassName, s.To, string.Join(", ", got)) });
        _storage.Save();
        _session = null;
        _planTask = null;
    }

    // Варианты и бросок — в сохранение игры (LevelUp.SaveDraft): отмена и перезапуск их не теряют.
    private string? SaveDraft(LevelUp s, string? result = null)
    {
        s.SaveDraft();
        _storage.Save();
        return result;
    }

    private static string? SetupError(Task task) =>
        task.Exception?.InnerException is AiSetupException setup ? setup.Message : null;

    // Подвкладки и страницы карточки (F6–F9, ←→, клик по названию) — как на вкладке «Персонаж». true — перерисовать.
    private bool CardCommand(string command, HeroDisplay heroDisplay)
    {
        var d = _display;
        void Tab(CharacterSubTab tab)
        {
            if (d.CharacterSubTab != tab) Sound.PlayClick();
            d.CharacterSubTab = tab;
            d.InventoryScrollOffset = d.AbilityPageOffset = d.EffectsPageOffset = d.SpellsPageOffset = 0;
            d.SelectedInventoryIndex = d.SelectedSpellIndex = d.PinnedInventoryIndex = d.PinnedSpellIndex = -1;
        }
        switch (command)
        {
            case "F6": Tab(CharacterSubTab.Inventory); return true;
            case "F7": Tab(CharacterSubTab.Abilities); return true;
            case "F8": Tab(CharacterSubTab.Effects); return true;
            case "F9": if (!heroDisplay.HasSpells && heroDisplay.Preview?.Spells is not { Count: > 0 }) return false; Tab(CharacterSubTab.Spells); return true;
            case "Left" or "Right":
                var si = heroDisplay.SubTabInfo;
                bool left = command == "Left";
                if (left ? !si.canScrollLeft : !si.canScrollRight) return false;
                Sound.PlayClick();
                int delta = left ? -1 : 1;
                switch (d.CharacterSubTab)
                {
                    case CharacterSubTab.Inventory: d.InventoryScrollOffset = Math.Max(0, d.InventoryScrollOffset + delta); break;
                    case CharacterSubTab.Abilities: d.AbilityPageOffset = Math.Max(0, d.AbilityPageOffset + delta); break;
                    case CharacterSubTab.Spells: d.SpellsPageOffset = Math.Max(0, d.SpellsPageOffset + delta); break;
                    default: d.EffectsPageOffset = Math.Max(0, d.EffectsPageOffset + delta); break;
                }
                return true;
        }
        return false;
    }

    // ── Отрисовка панели ────────────────────────────────────────────────────

    private void DrawPanel(int top, LevelUp s, List<Row> rows, int scroll, int listH, int? focusRow, int focusButton,
        List<(string id, string text, bool enabled)> buttons, string status, Panel right,
        string request, string? busy, bool inputActive, int leftW, int rightW)
    {
        _hits.Clear();
        Console.CursorVisible = false;
        Console.SetCursorPosition(0, top);
        int leftX = DisplayConfig.LeftMargin + 1;
        int rowsN = _display.MaxHistoryLines;

        string title = L.F("НОВЫЙ УРОВЕНЬ · {0} {1} → {2}", s.ClassName, s.From, s.To);
        _border.DrawTitleLine(() => ColorHelper.WriteColored(title, Gold), title.Length);
        if (rightW > 0) _border.DrawSeparatorWith2Parts('┬', leftW, rightW); else _border.DrawSeparator();

        bool hasBar = rows.Count > listH;
        var barMarks = hasBar ? TextArea.Bar(listH, rows.Count, scroll) : null;
        int vOffset = right.Top ? 1 : Math.Max(0, (rowsN - right.Lines.Length) / 2);
        for (int r = 0; r < rowsN; r++)
        {
            int y = top + 2 + r;
            Action leftContent = () => { };
            if (r < listH)
            {
                int i = scroll + r;
                if (i < rows.Count)
                {
                    var row = rows[i];
                    // Стрелка — у одного пункта: под мышью, а без неё — выбранного клавишами.
                    bool mouseOnRow = _hovered?.StartsWith("row:") == true;
                    bool on = row.Activate != null && (mouseOnRow ? _hovered == $"row:{i}" : focusRow == i);
                    if (row.Activate != null) _hits.Add((y, leftX, leftW - (hasBar ? 1 : 0), $"row:{i}"));
                    string text = Fit(row.Text, leftW - 4 - (hasBar ? 1 : 0));
                    var color = on ? Bright : row.Color;
                    leftContent = () =>
                    {
                        Console.Write(" ");
                        ColorHelper.WriteColored(on ? "→ " : "  ", Gold);
                        ColorHelper.WriteColored(text, color);
                    };
                }
                // Полоса прокрутки — у правого края списка.
                if (barMarks != null)
                {
                    var inner = leftContent;
                    bool thumb = barMarks[r];
                    leftContent = () =>
                    {
                        int start = Console.CursorLeft;
                        inner();
                        int pad = leftW - 1 - (Console.CursorLeft - start);
                        if (pad > 0) Console.Write(new string(' ', pad));
                        ColorHelper.WriteColored(thumb ? "┃" : "│", thumb ? Bright : Dim);
                    };
                }
            }
            else if (r == rowsN - 1)
            {
                // Кнопки-плашки и статус.
                int bx = leftX + 2;
                var plan = new List<(string text, List<int> fg, List<int> bg)>();
                for (int b = 0; b < buttons.Count; b++)
                {
                    var (id, text, enabled) = buttons[b];
                    bool disabled = !enabled || _frozen;
                    bool onB = !disabled && (focusButton == b || _hovered == id);
                    string t = $"  {text}  ";
                    plan.Add((t, disabled ? Dim : onB ? ColorHelper.Pale(Fg, 0.85) : Fg,
                        ColorHelper.MixWith(_display.MainBackground, _display.MainForeground, onB ? 0.42 : 0.16)));
                    if (!disabled) _hits.Add((y, bx, t.Length, id));
                    bx += t.Length + 2;
                }
                string st = Fit(status, Math.Max(0, leftW - (bx - leftX) - 3));
                leftContent = () =>
                {
                    Console.Write(" ");
                    foreach (var (t, fg, bg) in plan)
                    {
                        Console.Write(" ");
                        ColorHelper.WriteColored(t, fgColor: fg, bgColor: bg);
                        Console.Write(" ");
                    }
                    if (st.Length > 0) ColorHelper.WriteColored("  " + st, Gold);
                };
            }

            if (rightW > 0)
            {
                int li = r - vOffset;
                string line = li >= 0 && li < right.Lines.Length ? right.Lines[li] ?? "" : "";
                if (line.Length > rightW) line = line[..rightW];
                int pad = Math.Max(0, (rightW - line.Length) / 2);
                var lineColor = right.Color != null && (right.ColorAll || li == 0) ? right.Color : Fg;
                // «◄ 1/2 ►» внизу карточки заклинания — листание.
                string trimmed = line.Trim();
                if (li == right.Lines.Length - 1 && trimmed.StartsWith('◄') && trimmed.EndsWith('►'))
                {
                    int rx = DisplayConfig.LeftMargin + 1 + leftW + 1 + pad;
                    _hits.Add((y, rx + line.IndexOf('◄'), 2, "note:-1"));
                    _hits.Add((y, rx + line.IndexOf('►') - 1, 2, "note:1"));
                }
                _border.DrawContentLine2Columns(leftContent, () =>
                {
                    Console.Write(new string(' ', pad));
                    ColorHelper.WriteColored(line, lineColor);
                }, leftW, rightW);
            }
            else _border.DrawContentLine(leftContent);
        }

        if (rightW > 0) _border.DrawSeparatorWith2Parts('┴', leftW, rightW); else _border.DrawSeparator();
        // Строка ввода: просьба мастеру о своём варианте; пока мастер думает — его статус, печатать нельзя.
        string prompt = " " + L.T("Ввод: ");
        int inputW = _display.InnerWidth(_display.ViewCols(_settings.Map)) - prompt.Length - 2;
        int inputY = Console.CursorTop;
        _hits.Add((inputY, DisplayConfig.LeftMargin + 1, _display.InnerWidth(_display.ViewCols(_settings.Map)), "input"));
        string visible = request.Length > inputW - 1 ? "…" + request[^(inputW - 2)..] : request;
        _border.DrawContentLine(() =>
        {
            Console.Write(prompt);
            if (busy != null) ColorHelper.WriteColored(Fit(busy, inputW), Gold);
            else if (request.Length == 0 && !inputActive)
                ColorHelper.WriteColored(Fit(L.T("попроси мастера о своём варианте — например, «хочу Туманный шаг вместо Щита»"), inputW), Dim);
            else
                ColorHelper.WriteColored(visible, Bright);
        });
        _border.DrawBottomBorder();
        // Курсор печати — в строке ввода, когда она в фокусе (клик по ней или печать).
        bool typing = inputActive && busy == null && !_frozen;
        if (typing) Console.SetCursorPosition(DisplayConfig.LeftMargin + 1 + prompt.Length + visible.Length, inputY);
        Console.CursorVisible = typing;
    }

    // Блок картинки: заклинание — карточка из справочника, пункт с описанием — название и текст, иначе портрет героя.
    private Panel RightPanel(Row? row, int width, int page)
    {
        if (width <= 0) return new([]);
        if (row?.Spell is { } spell)
        {
            MouseUiHelper.SetSelectedSpell(_display, spell, page);
            if (_display.SelectedImageLines is { Length: > 0 } card) return new(card, Gold);   // карточка во всю высоту, ◄ ► внизу
        }
        if (row?.Description is { Length: > 0 } desc)
        {
            int w = Math.Max(1, width - 2);
            var lines = new List<string> { Fit(CleanName(row.Text), w).ToUpperInvariant().PadRight(w), new string('─', w) };
            lines.AddRange(Wrap(desc, w).Select(l => l.PadRight(w)));
            return new(lines.Take(_display.MaxHistoryLines - 1).ToArray(), Gold, Top: true);
        }
        return new(_display.DefaultImageLines ?? [], _display.DefaultImageColor, ColorAll: true);
    }

    // Предмет инвентаря или заклинание героя с карточки — как на вкладке «Персонаж» (картинка, записка, карточка
    // заклинания); строка на карточке — со стрелкой выбора.
    private void ShowCardItem(string item, HeroDisplay heroDisplay, int page)
    {
        var h = heroDisplay.Preview ?? _settings.Hero;
        if (h == null) return;
        int idx = int.Parse(item[(item.IndexOf(':') + 1)..]);
        _display.SelectedInventoryIndex = _display.SelectedSpellIndex = -1;
        if (item.StartsWith("inv:") && idx < h.Inventory.Count)
        {
            _display.SelectedImageLines = null;   // картинка готовится в фоне — пока портрет
            _display.SelectedInventoryIndex = idx;
            MouseUiHelper.SetSelectedInventoryItem(_display, h.Inventory[idx], page);
        }
        else if (item.StartsWith("spell:") && HeroDisplay.SortedSpells(h) is var spells && idx < spells.Count)
        {
            _display.SelectedSpellIndex = idx;
            MouseUiHelper.SetSelectedSpell(_display, spells[idx], page);
        }
        heroDisplay.RefreshSelection();
    }

    // Ответ мастера на просьбу — целиком (в строке статуса не помещается).
    private Panel MasterPanel(string text, int width)
    {
        if (width <= 0) return new([]);
        int w = Math.Max(1, width - 2);
        var lines = new List<string> { L.T("ОТВЕТ МАСТЕРА").PadRight(w), new string('─', w) };
        lines.AddRange(Wrap(text, w).Select(l => l.PadRight(w)));
        return new(lines.Take(_display.MaxHistoryLines - 1).ToArray(), Gold, Top: true);
    }

    // «( ) Оборона ★» → «Оборона».
    private static string CleanName(string text) => text.TrimStart(' ', '(', '•', ')', '[', 'x', ']').Replace(" ★", "").Trim();

    // Что ещё не выбрано — подсказка у кнопки «Принять».
    private static IEnumerable<string> Missing(LevelUp s)
    {
        if (s.HpRoll == null) yield return L.T("хиты");
        if (!s.AsiDone) yield return L.T("улучшение характеристик");
        foreach (var c in s.VisibleChoices.Where(c => !c.Done)) yield return c.Title.ToLowerInvariant();
    }

    // ── Список выбора ───────────────────────────────────────────────────────

    private List<Row> ChoiceRows(LevelUp s, bool loading, int spin, int width)
    {
        var rows = new List<Row>();
        string Radio(bool on) => on ? "(•) " : "( ) ";
        string Check(bool on) => on ? "[x] " : "[ ] ";
        void Section(string title)
        {
            if (rows.Count > 0) rows.Add(new("", Fg, Header: true));
            rows.Add(new(title, Bright, Header: true));
        }
        void Note(string text, int indent = 0)
        {
            foreach (string line in Wrap(text, Math.Max(10, width - indent))) rows.Add(new(new string(' ', indent) + line, Dim, Header: true));
        }
        HeroSpell? SpellOf(string name) => SpellDatabase.Find(name) is { } info ? new HeroSpell { Name = name, Level = info.Level } : null;

        int con = AbilityNames.Modifier(s.Score(AbilityNames.Con));
        Section(L.T("ХИТЫ"));
        Note(L.F("Кость хитов d{0} + Телосложение {1}. Бросок — один раз, без повтора.", s.HitDie, AbilityNames.Signed(con)));
        string hpDesc = L.T("Хиты за уровень: кость хитов класса + модификатор Телосложения (не меньше 1). Среднее — гарантированно, бросок — один раз, без повтора.");
        bool rolled = s.HpRoll == true;
        rows.Add(new(Radio(s.HpRoll == false) + L.F("Среднее: +{0}", s.HpAverage), rolled ? Dim : s.HpRoll == false ? Gold : Fg, hpDesc,
            rolled ? null : s.ChooseAverage));
        if (s.HpDie is int die)
            rows.Add(new(Radio(true) + L.F("Бросок d{0}: выпало {1} → +{2}", s.HitDie, die, Math.Max(1, die + con)), Gold, hpDesc, () => { }));
        else
            rows.Add(new(Radio(false) + L.F("Бросить d{0}{1}", s.HitDie, AbilityNames.Signed(con)), Fg, hpDesc, () => { RollHp(s); SaveDraft(s); }));

        if (s.IsAsi)
        {
            Section(L.T("УЛУЧШЕНИЕ ХАРАКТЕРИСТИК"));
            string asiDesc = L.F("+2 к одной характеристике или +1 к двум (не выше {0}) — либо черта вместо улучшения.", s.Rules.MaxScore);
            Note(asiDesc);
            rows.Add(new(Radio(s.Asi == LevelUp.AsiMode.Plus2) + L.T("+2 к одной"), s.Asi == LevelUp.AsiMode.Plus2 ? Gold : Fg, asiDesc, () => s.SetAsi(LevelUp.AsiMode.Plus2)));
            rows.Add(new(Radio(s.Asi == LevelUp.AsiMode.Plus1x2) + L.T("+1 к двум"), s.Asi == LevelUp.AsiMode.Plus1x2 ? Gold : Fg, asiDesc, () => s.SetAsi(LevelUp.AsiMode.Plus1x2)));
            if (s.HasFeats)
                rows.Add(new(Radio(s.Asi == LevelUp.AsiMode.Feat) + L.T("Черта"), s.Asi == LevelUp.AsiMode.Feat ? Gold : Fg, asiDesc, () => s.SetAsi(LevelUp.AsiMode.Feat)));
            if (s.Asi is LevelUp.AsiMode.Plus2 or LevelUp.AsiMode.Plus1x2)
                foreach (string a in AbilityNames.All)
                {
                    string name = AbilityNames.Find(s.Hero, a)?.Name ?? a;
                    bool picked = s.AsiPicks.Contains(a);
                    bool maxed = s.Score(a) >= s.Rules.MaxScore;
                    rows.Add(new("    " + Check(picked) + $"{name} {s.Score(a)}" + (picked ? $" → {s.NewScore(a)}" : ""),
                        maxed ? Dim : picked ? Gold : Fg, asiDesc, maxed ? null : () => s.ToggleAsi(a)));
                }
        }

        if (loading || !s.PlanLoaded) return rows;

        if (s.Gains.Count > 0)
        {
            Section(L.T("ПОЛУЧЕНО"));
            // Пункт для просмотра (описание и карточка заклинания справа), не выбор.
            foreach (var g in s.Gains)
                rows.Add(new("• " + g.Name, Fg, g.Description, () => { }, Spell: SpellOf(g.Name)));
        }
        foreach (var c in s.VisibleChoices)
        {
            Section($"{c.Title.ToUpperInvariant()}  " + L.F("({0}/{1})", c.Picked.Count, c.Needed));
            for (int i = 0; i < c.Options.Count; i++)
            {
                int index = i;
                var option = c.Options[i];
                bool picked = c.Picked.Contains(i);
                rows.Add(new((c.Needed == 1 ? Radio(picked) : Check(picked)) + option.Name + (option.Custom ? " ★" : ""),
                    picked ? Gold : Fg, option.Description, () => c.Toggle(index), Spell: SpellOf(option.Name)));
            }
        }
        if (s.Gains.Count == 0 && !s.VisibleChoices.Any())
        {
            Section(L.T("ПОЛУЧЕНО"));
            Note(L.T("Новых умений на этом уровне нет."));
        }
        return rows;
    }

    // Бросок кости хитов — один раз, со звуком кубика.
    private static void RollHp(LevelUp s)
    {
        s.RollHp(Random.Shared);
        _ = Sound.PlayDiceRoll();
    }

    // ── Ввод ────────────────────────────────────────────────────────────────

    private async Task<(ConsoleKeyInfo? key, string? click)?> WaitInput(int timeoutMs, string title, HeroDisplay heroDisplay)
    {
        var started = DateTime.UtcNow;
        var tabs = MouseUiHelper.ComputeTitleTabs(title);
        while (true)
        {
            var (move, click, _) = ConsoleMouseReader.DrainMouseEvents();
            if (move is { } m)
            {
                string? h = HitAt(m.x, m.y) ?? (_frozen ? null : CardItemAt(m, heroDisplay));
                string? tab = _frozen ? null : MouseUiHelper.GetHoveredTabKey(m.x, m.y, tabs);
                if (tab != _titleHover)
                {
                    if (_titleHover != null) MouseUiHelper.SetTabHighlight(title, tabs, _titleHover, false, _display);
                    if (tab != null) MouseUiHelper.SetTabHighlight(title, tabs, tab, true, _display);
                    _titleHover = tab;
                }
                ConsoleMouseReader.SetCursorShape(h != null || tab != null || CardClick(m, heroDisplay) != null);
                bool changed = h != _hovered;
                _hovered = h;
                if (changed && click == null) return null;
            }
            if (click is { } c)
            {
                if (c.y is >= 0 and <= 2)
                {
                    if (!_frozen && MouseUiHelper.GetHoveredTabKey(c.x, c.y, tabs) is { } tk)
                    {
                        Sound.PlayClick();
                        return (null, "title:" + tk);
                    }
                    ConsoleMouseReader.StartWindowDrag();
                    continue;
                }
                if (HitAt(c.x, c.y) is { } id) return (null, id);
                if (CardClick(c, heroDisplay) is { } cmd) return (null, "card:" + cmd);
                if (CardItemAt(c, heroDisplay) is { } item) return (null, "pin:" + item);
            }
            if (ConsoleMouseReader.TakeWheel(out _) is int notches and not 0) return (null, $"wheel:{notches}");
            if (MouseUiHelper.ApplyPreparedPicture(_display)) return null;   // картинка предмета готова — перерисовать
            if (ConsoleMouseReader.TryReadKey() is { } key)
            {
                if (!_frozen && key.Key is >= ConsoleKey.F1 and <= ConsoleKey.F5) return (null, "title:" + key.Key);
                return (key, null);
            }
            if (timeoutMs > 0 && (DateTime.UtcNow - started).TotalMilliseconds >= timeoutMs) return null;
            await Task.Delay(15);
        }
    }

    // Предмет инвентаря или заклинание на карточке под точкой: «inv:N» / «spell:N» (индекс как у HeroDisplay).
    private string? CardItemAt((short x, short y) p, HeroDisplay heroDisplay)
    {
        if (_display.CharacterSubTab == CharacterSubTab.Inventory)
        {
            var hi = heroDisplay.InventoryHitInfo;
            int off = p.y - hi.startY;
            if (hi.startY < 0 || off < 0 || off >= hi.rows) return null;
            int[] map = p.x < hi.splitX ? hi.col1Map : hi.col2Map;
            return off < map.Length && map[off] >= 0 ? $"inv:{map[off]}" : null;
        }
        if (_display.CharacterSubTab == CharacterSubTab.Spells)
        {
            var si = heroDisplay.SpellsHitInfo;
            int off = p.y - si.startY;
            if (si.startY < 0 || off < 0 || off >= si.rows) return null;
            int col = p.x < si.splitX[0] ? 0 : p.x < si.splitX[1] ? 1 : p.x < si.splitX[2] ? 2 : 3;
            return off < si.colMaps[col].Length && si.colMaps[col][off] >= 0 ? $"spell:{si.colMaps[col][off]}" : null;
        }
        return null;
    }

    // Клик по названию подвкладки карточки или стрелкам её страниц.
    private static string? CardClick((short x, short y) p, HeroDisplay heroDisplay)
    {
        var si = heroDisplay.SubTabInfo;
        if (si.titleY < 0 || p.y != si.titleY) return null;
        if (p.x >= si.invStartX && p.x < si.invEndX) return "F6";
        if (p.x >= si.abilStartX && p.x < si.abilEndX) return "F7";
        if (p.x >= si.effStartX && p.x < si.effEndX) return "F8";
        if (p.x >= si.spellStartX && p.x < si.spellEndX) return "F9";
        if (p.x == si.leftX && si.canScrollLeft) return "Left";
        if (p.x == si.rightX && si.canScrollRight) return "Right";
        return null;
    }

    private string? HitAt(short x, short y)
    {
        foreach (var (hy, hx, w, id) in _hits)
            if (hy == y && x >= hx && x < hx + w) return id;
        return null;
    }

    private static string Fit(string text, int width) =>
        width <= 0 ? "" : text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";

    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var cur = "";
            foreach (var word in para.Split(' '))
            {
                if (cur.Length > 0 && cur.Length + 1 + word.Length > width) { lines.Add(cur); cur = ""; }
                string w = word;
                while (w.Length > width) { lines.Add(w[..width]); w = w[width..]; }
                cur = cur.Length == 0 ? w : cur + " " + w;
            }
            if (cur.Length > 0) lines.Add(cur);
        }
        return lines;
    }
}
