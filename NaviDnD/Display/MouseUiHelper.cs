using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;
using NaviDnD.Helpers;

namespace NaviDnD;

internal static class MouseUiHelper
{
    internal readonly record struct TitleTab(int StartX, int EndX, string Key);

    internal static void HandleDialogArrowClick((short x, short y)? clickPos, DialogDisplay? dialog)
    {
        if (clickPos == null || dialog == null) return;
        var ar = dialog.ArrowPositions;
        if (clickPos.Value.y != ar.titleY) return;
        if (clickPos.Value.x == ar.upX && ar.canScrollUp)
        {
            dialog.TryScrollUp();
            dialog.SetArrowHover(dialog.ArrowPositions.canScrollUp, false);
            Sound.PlayClick();
        }
        else if (clickPos.Value.x == ar.downX && ar.canScrollDown)
        {
            dialog.TryScrollDown();
            dialog.SetArrowHover(false, dialog.ArrowPositions.canScrollDown);
            Sound.PlayClick();
        }
    }

    // Колесо мыши над диалоговым окном (ниже его заголовка) — прокрутка истории по строкам: от себя — к более
    // ранним, на себя — к новым. true — колесо ушло диалогу.
    internal static bool HandleDialogWheel(DialogDisplay? dialog, int notches, (short x, short y) pos)
    {
        if (dialog == null || notches == 0 || dialog.ArrowPositions.titleY <= 0 || pos.y <= dialog.ArrowPositions.titleY) return false;   // диалог ещё не нарисован — не его колесо
        dialog.ScrollLines(notches * 2);   // по 2 строки на щелчок колеса
        return true;
    }

    // Колесо, накопленное с прошлого опроса, — диалогу, если крутили над ним (экраны без своего колеса).
    internal static void HandleDialogWheel(DialogDisplay? dialog)
    {
        int notches = ConsoleMouseReader.TakeWheel(out var pos);
        HandleDialogWheel(dialog, notches, pos);
    }

    internal static (bool isOnUp, bool isOnDown) DialogArrowHover(
        (short x, short y) mousePos, DialogDisplay? dialog)
    {
        if (dialog == null) return (false, false);
        var ar = dialog.ArrowPositions;
        if (mousePos.y != ar.titleY) return (false, false);
        return (mousePos.x == ar.upX && ar.canScrollUp,
                mousePos.x == ar.downX && ar.canScrollDown);
    }

    internal static (bool isOnLeft, bool isOnRight) SubTabArrowHover(
        (short x, short y) mousePos, HeroDisplay? hero)
    {
        if (hero == null) return (false, false);
        var si = hero.SubTabInfo;
        if (si.titleY < 0 || mousePos.y != si.titleY) return (false, false);
        return (mousePos.x == si.leftX && si.canScrollLeft,
                mousePos.x == si.rightX && si.canScrollRight);
    }

    // disabled=true — затемнить сегменты "[Fx]" (тем же ColorHelper.Darker, что и остальные
    // неактивные стрелки в проекте): визуально показывает, что кнопки сейчас некликабельны
    // (например пока крутится кубик и ввод намеренно игнорируется).
    // Ширина строки заголовка (внутри рамки) — для «[Esc]…» у правого края (BorderDrawer задаёт при создании).
    internal static int TitleWidth;

    // «[Esc]МЕНЮ» / «[Esc]НАЗАД» — всегда у правого края заголовка на любом экране. Та же раскладка и для
    // отрисовки, и для зон наведения/клика (ComputeTitleTabs, SetTabHighlight).
    internal static string AlignTitle(string title)
    {
        if (TitleWidth < WindowControls.Buttons.Length + 3) return title;
        if (title.EndsWith(WindowControls.Buttons, StringComparison.Ordinal)) return title;
        int available = TitleWidth - WindowControls.Buttons.Length;
        int e = title.LastIndexOf("[Esc]", StringComparison.Ordinal);
        if (e < 0) return title.TrimEnd().PadRight(available)[..available] + WindowControls.Buttons;
        string token = title[e..].TrimEnd();
        string left = title[..e].TrimEnd();
        int gap = available - 1 - left.Length - token.Length;
        string aligned = gap < 3 ? title.PadRight(available)[..available] : left + new string(' ', gap) + token + " ";
        return aligned + WindowControls.Buttons;
    }

    internal static void WriteColoredTitle(string title, DisplayConfig display, bool disabled = false)
    {
        title = AlignTitle(title);
        var bright = ColorHelper.Pale(display.MainForeground, 0.75);
        var dim = disabled ? ColorHelper.Darker(display.MainForeground, 0.5) : KeyHintColor(display);
        int aStart = title.IndexOf('→');
        int aEnd   = aStart >= 0 ? title.IndexOf('←', aStart) : -1;

        // Название окна (« МОИ ИГРЫ»… — до первой тройки пробелов) — ярким текстом, без подложки.
        int i = 0;
        int nameEnd = title.IndexOf("   ", StringComparison.Ordinal);
        if (title.StartsWith(' ') && nameEnd > 1 && (aStart < 0 || nameEnd < aStart))
        {
            ColorHelper.WriteColored(title[..(nameEnd + 1)], bright);
            i = nameEnd + 1;
        }
        // Шаги новой игры (ScreenRegistry.NewGameTitle): приглушённо, текущий «…» — ярко (кавычки — пробелами).
        int stepsStart = title.IndexOf("1 МИР", StringComparison.Ordinal);
        int stepsEnd = stepsStart >= 0 ? title.IndexOf("ПРИКЛЮЧЕНИЕ", stepsStart, StringComparison.Ordinal) : -1;
        if (stepsEnd >= 0) stepsEnd += "ПРИКЛЮЧЕНИЕ".Length + (stepsEnd + 11 < title.Length && title[stepsEnd + 11] == '»' ? 1 : 0);
        if (stepsStart > 0 && title[stepsStart - 1] == '«') stepsStart--;
        while (i < title.Length)
        {
            if (title.EndsWith(WindowControls.Buttons, StringComparison.Ordinal)
                && i == title.Length - WindowControls.Buttons.Length)
            {
                WindowControls.Draw(display);
                break;
            }
            if (stepsEnd > 0 && i == stepsStart)
            {
                var stepDim = ColorHelper.Darker(display.MainForeground, 0.45);
                bool cur = false;
                for (; i < stepsEnd; i++)
                {
                    if (title[i] == '«') cur = true;
                    ColorHelper.WriteColored(title[i] is '«' or '»' ? " " : title[i].ToString(), cur ? bright : stepDim);
                    if (title[i] == '»') cur = false;
                }
                continue;
            }
            if (aStart >= 0 && aEnd > aStart && i == aStart)
            {
                ColorHelper.WriteColored(title[aStart..(aEnd + 1)], bright);
                i = aEnd + 1;
            }
            // Недоступная вкладка «{F1}ИМЯ» — подсказка клавиши как обычно «[F1]», название тёмным (почти фон).
            else if (title[i] == '{' && title.IndexOf('}', i) is int ce and > 0)
            {
                WriteKeyHint("[" + title[(i + 1)..ce] + "]", display);
                int ne = title.IndexOf(' ', ce);
                if (ne < 0) ne = title.Length;
                ColorHelper.WriteColored(title[(ce + 1)..ne], DisabledTabColor(display));
                i = ne;
            }
            // «›» между шагами новой игры — цветом названий вкладок; перед недоступным шагом «{Fn}» — тёмная, как он.
            else if (title[i] == '›')
            {
                int next = i + 1;
                while (next < title.Length && title[next] == ' ') next++;
                ColorHelper.WriteColored("›", next < title.Length && title[next] == '{' ? DisabledTabColor(display) : display.MainForeground);
                i++;
            }
            else if (title[i] == '[')
            {
                int end = title.IndexOf(']', i);
                if (end >= 0)
                {
                    if (disabled) ColorHelper.WriteColored(title[i..(end + 1)], dim);
                    else WriteKeyHint(title[i..(end + 1)], display);
                    i = end + 1;
                }
                else
                {
                    Console.Write(title[i]);
                    i++;
                }
            }
            else
            {
                Console.Write(title[i]);
                i++;
            }
        }
    }

    // Подсказки клавиш «[F1]», «[Esc]» — приглушённым цветом, чтобы не спорили с названиями вкладок.
    // Подсказка клавиши: приглушённо, а если подсказки выключены в настройках — пробелы той же длины.
    internal static void WriteKeyHint(string hint, DisplayConfig display)
    {
        if (display.ShowKeyHints) ColorHelper.WriteColored(hint, KeyHintColor(display));
        else Console.Write(new string(' ', hint.Length));
    }

    // Линии рамок — приглушённый основной цвет (как рамки блоков новой игры): текст на их фоне читается лучше.
    internal static List<int> FrameColor(DisplayConfig display) => ColorHelper.Darker(display.MainForeground, 0.5);

    // Недоступная вкладка/шаг — почти фон.
    internal static List<int> DisabledTabColor(DisplayConfig display) => ColorHelper.MixWith(display.MainForeground, display.MainBackground, 0.68);

    internal static List<int> KeyHintColor(DisplayConfig display) => ColorHelper.MixWith(display.MainForeground, display.MainBackground, 0.9);

    internal static List<TitleTab> ComputeTitleTabs(string title)
    {
        title = AlignTitle(title);
        var tabs = new List<TitleTab>();
        foreach (var (prefix, key) in new (string, string)[] { ("[F1]","F1"), ("[F2]","F2"), ("[F3]","F3"), ("[F4]","F4"), ("[F5]","F5"), ("[Esc]","Esc") })
        {
            int idx = title.IndexOf(prefix);
            if (idx < 0) continue;
            int end = idx + prefix.Length;
            while (end < title.Length && title[end] != ' ') end++;
            // Отступ слева + рамка(1) до начала текста заголовка. Зона наведения/клика — только название:
            // подсказка клавиши «[F1]» не подсвечивается и курсор над ней не меняется.
            int titleStart = DisplayConfig.LeftMargin + 1;
            tabs.Add(new TitleTab(titleStart + idx + prefix.Length, titleStart + end, key));
        }
        return tabs;
    }

    internal static string? GetHoveredTabKey(short mouseX, short mouseY, List<TitleTab> tabs)
    {
        if (mouseY != 1) return null;
        foreach (var tab in tabs)
            if (mouseX >= tab.StartX && mouseX < tab.EndX)
                return tab.Key;
        return null;
    }

    internal static void SetTabHighlight(string title, List<TitleTab> tabs, string key, bool highlighted, DisplayConfig display)
    {
        var tab = tabs.Find(t => t.Key == key);
        if (tab.Key == null) return;
        title = AlignTitle(title);
        bool vis = Console.CursorVisible;
        Console.CursorVisible = false;
        int sl = Console.CursorLeft, st = Console.CursorTop;
        Console.SetCursorPosition(tab.StartX, 1);
        // tab.StartX/EndX — экранные колонки (уже с учётом отступа); title — исходная строка без
        // отступа, поэтому для индекса в title его нужно вычесть обратно.
        string tabText = title.Substring(tab.StartX - 1 - DisplayConfig.LeftMargin, tab.EndX - tab.StartX);
        var fg = highlighted ? ColorHelper.Pale(display.MainForeground, 0.75) : display.MainForeground;
        int keyEnd = tabText.StartsWith('[') ? tabText.IndexOf(']') + 1 : 0;
        if (keyEnd > 0)
            ColorHelper.WriteColored(tabText[..keyEnd], fgColor: KeyHintColor(display), bgColor: display.MainBackground); // клавиша не подсвечивается
        ColorHelper.WriteColored(tabText[keyEnd..], fgColor: fg, bgColor: display.MainBackground);
        Console.SetCursorPosition(sl, st);
        Console.CursorVisible = vis;
    }

    internal static Action MakeCharacterPollAction(
        DialogDisplay dialog, HeroDisplay heroDisplay,
        int drawStartTop, Action draw,
        Action? onScrollLeft, Action? onScrollRight,
        string title, List<TitleTab> titleTabs, DisplayConfig display,
        Action<int, int>? onInventorySelect = null,
        Action<int, int>? onSpellSelect = null)
    {
        string? hoveredTabKey = null;
        string? hoveredSubTabLabel = null; // "inv", "abil", or null
        int hoveredInvItem = -1;
        int hoveredSpellItem = -1;

        // x < splitX[0] → колонка 0, x < splitX[1] → колонка 1, x < splitX[2] → колонка 2, иначе 3.
        static int SpellColumnIndex(short x, int[] splitX)
        {
            if (x < splitX[0]) return 0;
            if (x < splitX[1]) return 1;
            if (x < splitX[2]) return 2;
            return 3;
        }

        void RedrawAll()
        {
            int savedLeft = Console.CursorLeft, savedTop = Console.CursorTop;
            Console.CursorVisible = false;
            heroDisplay.RefreshSelection();
            dialog.RerenderRightPanel();
            Console.SetCursorPosition(savedLeft, savedTop);
        }

        return () =>
        {
            if (ApplyPreparedPicture(display)) dialog.RerenderRightPanel();
            var activeTitle = display.StreamingTabTitle ?? title;
            var activeTabs  = display.StreamingTabTitle != null ? ComputeTitleTabs(activeTitle) : titleTabs;

            var (mousePos, clickPos, clickCount) = ConsoleMouseReader.DrainMouseEvents();

            // Обрезанное значение карточки под мышью — бегущая строка (тикает и без движения мыши).
            heroDisplay?.UpdateMarquee(mousePos);

            HandleDialogArrowClick(clickPos, dialog);
            HandleDialogWheel(dialog);

            if (clickPos.HasValue)
            {
                var clickedTabKey = GetHoveredTabKey(clickPos.Value.x, clickPos.Value.y, activeTabs);
                if (clickedTabKey != null) { Sound.PlayClick(); display.PendingCommand = clickedTabKey; return; }
                if (clickPos.Value.y is >= 0 and <= 2) { ConsoleMouseReader.StartWindowDrag(); return; }

                var si = heroDisplay.SubTabInfo;
                if (si.titleY >= 0 && clickPos.Value.y == si.titleY)
                {
                    // Sub-tab label clicks → switch sub-tab (звук только если реально переключает —
                    // клик по УЖЕ активной вкладке ничего не меняет, как и недоступная стрелка)
                    if (clickPos.Value.x >= si.invStartX && clickPos.Value.x < si.invEndX)
                        { if (display.CharacterSubTab != CharacterSubTab.Inventory) Sound.PlayClick(); display.PendingCommand = "F6"; return; }
                    if (clickPos.Value.x >= si.abilStartX && clickPos.Value.x < si.abilEndX)
                        { if (display.CharacterSubTab != CharacterSubTab.Abilities) Sound.PlayClick(); display.PendingCommand = "F7"; return; }
                    if (clickPos.Value.x >= si.effStartX && clickPos.Value.x < si.effEndX)
                        { if (display.CharacterSubTab != CharacterSubTab.Effects) Sound.PlayClick(); display.PendingCommand = "F8"; return; }
                    if (clickPos.Value.x >= si.spellStartX && clickPos.Value.x < si.spellEndX)
                        { if (display.CharacterSubTab != CharacterSubTab.Spells) Sound.PlayClick(); display.PendingCommand = "F9"; return; }

                    bool handled = false;
                    if (clickPos.Value.x == si.leftX && si.canScrollLeft && onScrollLeft != null)
                    {
                        heroDisplay.SetSubTabArrowHover(false, false);
                        onScrollLeft();
                        handled = true;
                    }
                    else if (clickPos.Value.x == si.rightX && si.canScrollRight && onScrollRight != null)
                    {
                        heroDisplay.SetSubTabArrowHover(false, false);
                        onScrollRight();
                        handled = true;
                    }
                    if (handled)
                    {
                        Sound.PlayClick();
                        int savedLeft = Console.CursorLeft, savedTop = Console.CursorTop;
                        Console.CursorVisible = false;
                        Console.SetCursorPosition(0, drawStartTop);
                        draw();
                        Console.SetCursorPosition(savedLeft, savedTop);
                        if (mousePos != null)
                        {
                            var (isL, isR) = SubTabArrowHover(mousePos.Value, heroDisplay);
                            heroDisplay.SetSubTabArrowHover(isL, isR);
                            ConsoleMouseReader.SetCursorShape(isL || isR);
                        }
                        return;
                    }
                }

                // Клик по стрелкам страниц записки (◄ N/M ►) в панели картинки. Не return — тот же
                // DrainMouseEvents() иногда отдаёт clickPos ВМЕСТЕ со свежим mousePos (клик и
                // следующее движение мыши успевают встать в очередь до одного poll-тика); ранний
                // return здесь молча терял mousePos этого тика — наведение переставало отслеживаться
                // до следующего клика.
                bool clickHandled = false;
                var na = dialog.NotePageArrowPositions;
                if (na.arrowY >= 0 && clickPos.Value.y == na.arrowY)
                {
                    bool isSpellTab = display.CharacterSubTab == CharacterSubTab.Spells;
                    int effectiveIdx = isSpellTab
                        ? (hoveredSpellItem >= 0 ? hoveredSpellItem : display.PinnedSpellIndex)
                        : (hoveredInvItem >= 0 ? hoveredInvItem : display.PinnedInventoryIndex);
                    var select = isSpellTab ? onSpellSelect : onInventorySelect;
                    if (effectiveIdx >= 0 && select != null)
                    {
                        // clickCount: быстрые повторные клики по стрелке успевают встать в очередь
                        // между poll-тиками — DrainMouseEvents() отдаёт только последнюю позицию клика,
                        // но считает все нажатия, иначе часть кликов молча терялась бы (пролистывало на 1
                        // вместо N при быстром клике).
                        int steps = Math.Max(1, clickCount);
                        if (clickPos.Value.x == na.leftX)
                        {
                            if (display.NotePage > 0) Sound.PlayClick();
                            select(effectiveIdx, display.NotePage - steps);
                            RedrawAll();
                            clickHandled = true;
                        }
                        else if (clickPos.Value.x == na.rightX)
                        {
                            if (display.NotePage < display.NotePageCount - 1) Sound.PlayClick();
                            select(effectiveIdx, display.NotePage + steps);
                            RedrawAll();
                            clickHandled = true;
                        }
                    }
                }

                // Клик по строке инвентаря — закрепить предмет (жёлтая стрелка, переживает уход мыши)
                if (!clickHandled && display.CharacterSubTab == CharacterSubTab.Inventory && onInventorySelect != null)
                {
                    var hi = heroDisplay.InventoryHitInfo;
                    if (hi.startY >= 0 && hi.rows > 0)
                    {
                        int rowOffset = clickPos.Value.y - hi.startY;
                        if (rowOffset >= 0 && rowOffset < hi.rows)
                        {
                            int clickedIdx = clickPos.Value.x < hi.splitX
                                ? (rowOffset < hi.col1Map.Length ? hi.col1Map[rowOffset] : -1)
                                : (rowOffset < hi.col2Map.Length ? hi.col2Map[rowOffset] : -1);
                            if (clickedIdx >= 0)
                            {
                                // Клик по уже закреплённому предмету ничего не меняет — звука не даёт.
                                if (display.PinnedInventoryIndex != clickedIdx) Sound.PlayClick();
                                display.SelectedInventoryIndex = clickedIdx;
                                display.PinnedInventoryIndex = clickedIdx;
                                hoveredInvItem = clickedIdx;
                                onInventorySelect(clickedIdx, 0);
                                RedrawAll();
                            }
                        }
                    }
                }

                // Клик по строке заклинания — закрепить (аналог клика по инвентарю, но на 4 колонки).
                if (!clickHandled && display.CharacterSubTab == CharacterSubTab.Spells && onSpellSelect != null)
                {
                    var shi = heroDisplay.SpellsHitInfo;
                    if (shi.startY >= 0 && shi.rows > 0)
                    {
                        int rowOffset = clickPos.Value.y - shi.startY;
                        if (rowOffset >= 0 && rowOffset < shi.rows)
                        {
                            int col = SpellColumnIndex(clickPos.Value.x, shi.splitX);
                            int clickedIdx = rowOffset < shi.colMaps[col].Length ? shi.colMaps[col][rowOffset] : -1;
                            if (clickedIdx >= 0)
                            {
                                if (display.PinnedSpellIndex != clickedIdx) Sound.PlayClick();
                                display.SelectedSpellIndex = clickedIdx;
                                display.PinnedSpellIndex = clickedIdx;
                                hoveredSpellItem = clickedIdx;
                                onSpellSelect(clickedIdx, 0);
                                RedrawAll();
                            }
                        }
                    }
                }
            }

            if (mousePos == null) return;

            var newTabKey = GetHoveredTabKey(mousePos.Value.x, mousePos.Value.y, activeTabs);
            if (newTabKey != hoveredTabKey)
            {
                if (hoveredTabKey != null) SetTabHighlight(activeTitle, activeTabs, hoveredTabKey, false, display);
                hoveredTabKey = newTabKey;
                if (hoveredTabKey != null) SetTabHighlight(activeTitle, activeTabs, hoveredTabKey, true, display);
            }
            if (hoveredTabKey != null) { ConsoleMouseReader.SetCursorShape(true); return; }

            // Sub-tab label hover → highlight + hand cursor
            var subInfo = heroDisplay.SubTabInfo;
            string? newHoveredSubTab = null;
            if (subInfo.titleY >= 0 && mousePos.Value.y == subInfo.titleY)
            {
                if (mousePos.Value.x >= subInfo.invStartX   && mousePos.Value.x < subInfo.invEndX)   newHoveredSubTab = "inv";
                if (mousePos.Value.x >= subInfo.abilStartX  && mousePos.Value.x < subInfo.abilEndX)  newHoveredSubTab = "abil";
                if (mousePos.Value.x >= subInfo.effStartX   && mousePos.Value.x < subInfo.effEndX)   newHoveredSubTab = "eff";
                if (mousePos.Value.x >= subInfo.spellStartX && mousePos.Value.x < subInfo.spellEndX) newHoveredSubTab = "spell";
            }
            if (newHoveredSubTab != hoveredSubTabLabel)
            {
                hoveredSubTabLabel = newHoveredSubTab;
                heroDisplay.SetSubTabLabelHover(hoveredSubTabLabel);
            }

            // Inventory item hover
            int newHoveredInv = -1;
            if (display.CharacterSubTab == CharacterSubTab.Inventory)
            {
                var hi = heroDisplay.InventoryHitInfo;
                if (hi.startY >= 0 && hi.rows > 0)
                {
                    int rowOffset = mousePos.Value.y - hi.startY;
                    if (rowOffset >= 0 && rowOffset < hi.rows)
                        newHoveredInv = mousePos.Value.x < hi.splitX
                            ? (rowOffset < hi.col1Map.Length ? hi.col1Map[rowOffset] : -1)
                            : (rowOffset < hi.col2Map.Length ? hi.col2Map[rowOffset] : -1);
                }
            }
            if (newHoveredInv != hoveredInvItem)
            {
                bool needRedraw = false;
                if (newHoveredInv >= 0)
                {
                    display.SelectedInventoryIndex = newHoveredInv;
                    onInventorySelect?.Invoke(newHoveredInv, 0);
                    needRedraw = true;
                }
                else if (hoveredInvItem >= 0)
                {
                    if (display.PinnedInventoryIndex >= 0)
                    {
                        display.SelectedInventoryIndex = display.PinnedInventoryIndex;
                        onInventorySelect?.Invoke(display.PinnedInventoryIndex, 0);
                    }
                    else
                    {
                        CancelPreparedPicture(display);
                        display.SelectedInventoryIndex = -1;
                        display.SelectedImageLines = null;
                        display.SelectedImageColor = null;
                        display.NotePage = 0;
                        display.NotePageCount = 0;
                    }
                    needRedraw = true;
                }
                hoveredInvItem = newHoveredInv;
                if (needRedraw) RedrawAll();
            }

            // Spell item hover (аналог инвентаря, но на 4 колонки)
            int newHoveredSpell = -1;
            if (display.CharacterSubTab == CharacterSubTab.Spells)
            {
                var si = heroDisplay.SpellsHitInfo;
                if (si.startY >= 0 && si.rows > 0)
                {
                    int rowOffset = mousePos.Value.y - si.startY;
                    if (rowOffset >= 0 && rowOffset < si.rows)
                    {
                        int col = SpellColumnIndex(mousePos.Value.x, si.splitX);
                        newHoveredSpell = rowOffset < si.colMaps[col].Length ? si.colMaps[col][rowOffset] : -1;
                    }
                }
            }
            if (newHoveredSpell != hoveredSpellItem)
            {
                bool needRedrawSpell = false;
                if (newHoveredSpell >= 0)
                {
                    display.SelectedSpellIndex = newHoveredSpell;
                    onSpellSelect?.Invoke(newHoveredSpell, 0);
                    needRedrawSpell = true;
                }
                else if (hoveredSpellItem >= 0)
                {
                    if (display.PinnedSpellIndex >= 0)
                    {
                        display.SelectedSpellIndex = display.PinnedSpellIndex;
                        onSpellSelect?.Invoke(display.PinnedSpellIndex, 0);
                    }
                    else
                    {
                        display.SelectedSpellIndex = -1;
                        display.SelectedImageLines = null;
                        display.SelectedImageColor = null;
                        display.NotePage = 0;
                        display.NotePageCount = 0;
                    }
                    needRedrawSpell = true;
                }
                hoveredSpellItem = newHoveredSpell;
                if (needRedrawSpell) RedrawAll();
            }

            var (isOnUp, isOnDown) = DialogArrowHover(mousePos.Value, dialog);
            var (isOnLeft, isOnRight) = SubTabArrowHover(mousePos.Value, heroDisplay);
            var (isOnNoteLeft, isOnNoteRight) = NotePageArrowHover(mousePos.Value, dialog, display);
            bool isOnAny = isOnUp || isOnDown || isOnLeft || isOnRight || isOnNoteLeft || isOnNoteRight
                           || newHoveredSubTab != null || newHoveredInv >= 0 || newHoveredSpell >= 0;
            ConsoleMouseReader.SetCursorShape(isOnAny);
            dialog.SetArrowHover(isOnUp, isOnDown);
            heroDisplay.SetSubTabArrowHover(isOnLeft, isOnRight);
            dialog.SetNoteArrowHover(isOnNoteLeft, isOnNoteRight);
        };
    }

    internal static (bool isOnLeft, bool isOnRight) NotePageArrowHover(
        (short x, short y) mousePos, DialogDisplay? dialog, DisplayConfig display)
    {
        if (dialog == null) return (false, false);
        var na = dialog.NotePageArrowPositions;
        if (na.arrowY < 0 || mousePos.y != na.arrowY) return (false, false);
        // Недоступная (потускневшая) стрелка не должна давать курсор-руку — как и остальные
        // disabled-стрелки в проекте.
        bool onLeft  = mousePos.x == na.leftX  && display.NotePage > 0;
        bool onRight = mousePos.x == na.rightX && display.NotePage < display.NotePageCount - 1;
        return (onLeft, onRight);
    }

    // Журнал: вкладки заголовка и стрелки диалога — как у остальных экранов, плюс мышь по книге
    // (закладки, ◄ ►, наведение на запись → подробности в блоке картинок).
    internal static Action MakeJournalPollAction(DialogDisplay dialog, NaviDnD.Display.JournalDisplay journal, string title, DisplayConfig display)
    {
        var titleTabs = ComputeTitleTabs(title);
        string? hoveredTabKey = null;
        return () =>
        {
            if (ApplyPreparedPicture(display)) dialog.RerenderRightPanel();
            var activeTitle = display.StreamingTabTitle ?? title;
            var activeTabs  = display.StreamingTabTitle != null ? ComputeTitleTabs(activeTitle) : titleTabs;
            var (mousePos, clickPos, _) = ConsoleMouseReader.DrainMouseEvents();
            HandleDialogArrowClick(clickPos, dialog);
            HandleDialogWheel(dialog);
            if (clickPos.HasValue)
            {
                var clickedTabKey = GetHoveredTabKey(clickPos.Value.x, clickPos.Value.y, activeTabs);
                if (clickedTabKey != null) { Sound.PlayClick(); display.PendingCommand = clickedTabKey; return; }
                if (clickPos.Value.y is >= 0 and <= 2) { ConsoleMouseReader.StartWindowDrag(); return; }
            }
            if (journal.HandleMouse(mousePos, clickPos)) dialog.RerenderRightPanel();
            if (mousePos == null) return;
            var newTabKey = GetHoveredTabKey(mousePos.Value.x, mousePos.Value.y, activeTabs);
            if (newTabKey != hoveredTabKey)
            {
                if (hoveredTabKey != null) SetTabHighlight(activeTitle, activeTabs, hoveredTabKey, false, display);
                hoveredTabKey = newTabKey;
                if (hoveredTabKey != null) SetTabHighlight(activeTitle, activeTabs, hoveredTabKey, true, display);
            }
            if (hoveredTabKey != null) { ConsoleMouseReader.SetCursorShape(true); return; }
            var (isOnUp, isOnDown) = DialogArrowHover(mousePos.Value, dialog);
            if (isOnUp || isOnDown) ConsoleMouseReader.SetCursorShape(true);
            dialog.SetArrowHover(isOnUp, isOnDown);
        };
    }

    internal static Action MakeDialogArrowPollAction(DialogDisplay dialog, string title, List<TitleTab> titleTabs, DisplayConfig display)
    {
        string? hoveredTabKey = null;
        return () =>
        {
            if (ApplyPreparedPicture(display)) dialog.RerenderRightPanel();
            var activeTitle = display.StreamingTabTitle ?? title;
            var activeTabs  = display.StreamingTabTitle != null ? ComputeTitleTabs(activeTitle) : titleTabs;

            var (mousePos, clickPos, _) = ConsoleMouseReader.DrainMouseEvents();
            HandleDialogArrowClick(clickPos, dialog);
            HandleDialogWheel(dialog);
            if (clickPos.HasValue)
            {
                var clickedTabKey = GetHoveredTabKey(clickPos.Value.x, clickPos.Value.y, activeTabs);
                if (clickedTabKey != null) { Sound.PlayClick(); display.PendingCommand = clickedTabKey; return; }
                if (clickPos.Value.y is >= 0 and <= 2) { ConsoleMouseReader.StartWindowDrag(); return; }
            }
            if (mousePos == null) return;
            var newTabKey = GetHoveredTabKey(mousePos.Value.x, mousePos.Value.y, activeTabs);
            if (newTabKey != hoveredTabKey)
            {
                if (hoveredTabKey != null) SetTabHighlight(activeTitle, activeTabs, hoveredTabKey, false, display);
                hoveredTabKey = newTabKey;
                if (hoveredTabKey != null) SetTabHighlight(activeTitle, activeTabs, hoveredTabKey, true, display);
            }
            if (hoveredTabKey != null) { ConsoleMouseReader.SetCursorShape(true); return; }
            var (isOnUp, isOnDown) = DialogArrowHover(mousePos.Value, dialog);
            ConsoleMouseReader.SetCursorShape(isOnUp || isOnDown);
            dialog.SetArrowHover(isOnUp, isOnDown);
        };
    }

    // Заглушка d20 без картинки: кубик — по центру области НАД именем, имя — фиксированной
    // предпоследней строкой (совпадает с тем, где заканчивается footer у заглушки, если бы
    // была картинка). Раньше кубик стоял прямо в начале массива (строка 0) — короткая заглушка
    // визуально "прилипала" к верху панели вместо того чтобы стоять по центру над именем.
    private static List<string> BuildCenteredArtWithName(IReadOnlyList<string> art, string name, int totalLines)
    {
        int end = art.Count;
        while (end > 0 && string.IsNullOrEmpty(art[end - 1])) end--;

        var lines = new List<string>();
        // Центр считаем от ВСЕЙ высоты панели (totalLines), а не только от области над именем —
        // иначе имя+пустая строка внизу "перетягивают" центр кубика на 1 строку выше настоящей
        // середины панели.
        int artStart = Math.Max(0, (totalLines - end) / 2);
        for (int i = 0; i < artStart; i++) lines.Add("");
        for (int i = 0; i < end; i++) lines.Add(art[i]);
        while (lines.Count < totalLines - 2) lines.Add("");
        lines.Add(name);
        return lines;
    }

    // Без картинки (не задана или файл не найден) — заглушка d20 вместо пустой панели: иначе
    // BuildRightPanelLines молча провалился бы к DefaultImageLines (портрет ГЕРОЯ), что выглядело
    // бы так, будто у наведённого предмета есть картинка героя, а не "картинки нет".
    private static string[] BuildDefaultIconLines(string? name, int totalLines)
    {
        if (string.IsNullOrEmpty(name)) return DiceArt.D20.Split('\n');
        var lines = BuildCenteredArtWithName(DiceArt.D20.Split('\n'), name, totalLines);
        lines.Add(""); // последняя строка пустая — индикатора страниц здесь нет
        return lines.ToArray();
    }

    // Единая точка выбора элемента карты (мышь/Tab на MapScreenLoop) — известный вид монстра
    // (narrative.knownMonsters содержит его monsterKey) показывает карточку статов, иначе как
    // раньше — просто картинка+имя. Герой/объекты/неопознанные существа под это не попадают
    // (GetSelectedLivingEntity возвращает null для объектов, MonsterKey может быть не задан).
    // page — страница карточки монстра (листание ◄ ► на карте, MapScreenLoop).
    internal static void SetSelectedMapEntity(DisplayConfig display, LegendDisplay legendDisplay, WorldState settings, int page = 0)
    {
        var entity = legendDisplay.GetSelectedLivingEntity();
        bool known = entity?.MonsterKey != null
            && settings.Narrative?.KnownMonsters?.Contains(entity.MonsterKey, StringComparer.OrdinalIgnoreCase) == true;
        if (entity != null && known)
        {
            SetSelectedMonster(display, entity, MonsterDatabase.Find(entity.MonsterKey), page);
            return;
        }

        var (imgPath, imgName, imgColor) = legendDisplay.GetSelectedEntityImageAndName();
        SetSelectedImage(display, imgPath, imgName, imgColor);
        display.NotePage = 0;
        display.NotePageCount = 0;
    }

    // Картинка двери: своя (ИИ задаёт только особым дверям) или фиксированная — по состоянию и типу
    // местности блока, где дверь (рядовые двери нейронка не ищет через find_icon).
    internal static string DoorImage(NaviDnD.Data.Models.Door door, NaviDnD.Data.Models.MapConfig map)
    {
        if (!string.IsNullOrEmpty(door.Image)) return door.Image;
        string? theme = door.From is { Count: >= 2 } f ? map.ChunkAt(f[0], f[1])?.Theme : null;
        bool open = door.IsDoor != true || door.IsDoorOpen == true;
        if (door.IsWindow == true) return open ? "delapouite/window" : "delapouite/window-bars";
        if (door.IsWorldExit == true)
            return theme switch
            {
                "building" => "delapouite/house",
                "village" => "delapouite/village",
                "dungeon" => "delapouite/crypt-entrance",
                "cave" => "delapouite/cave-entrance",
                "forest" => "delapouite/forest-entrance",
                "hills" or "mountain" => "delapouite/mountain-road",
                "plains" or "swamp" => "delapouite/horizon-road",
                _ => "delapouite/exit-door",
            };
        if (NaviDnD.Data.Models.MapChunk.IsOutdoorTheme(theme))
            return open ? "delapouite/open-gate" : "delapouite/gate";
        if (theme == NaviDnD.Data.Models.MapChunk.Cave)
            return open ? "delapouite/underground-cave" : "delapouite/door";
        return open ? "lorc/doorway" : "delapouite/door";
    }

    // Подпись под картинкой в одну строку: длинное название не влезало в ширину панели и ломало рамку.
    private static string? FitPanelName(DisplayConfig display, string? name)
    {
        int width = Math.Max(4, display.DialogRightPanelWidth - 2);
        if (name == null || name.Length <= width) return name;
        return name[..(width - 1)].TrimEnd() + "…";
    }

    internal static void SetSelectedImage(DisplayConfig display, string? imagePath, string? name, List<int>? color = null)
    {
        name = FitPanelName(display, name);
        display.SelectedImageLines = string.IsNullOrEmpty(imagePath)
            ? BuildDefaultIconLines(name, display.MaxHistoryLines)
            : SvgToBrailleConverter.Convert(imagePath, name ?? "") ?? BuildDefaultIconLines(name, display.MaxHistoryLines);
        display.SelectedImageColor = color;
        display.SelectedImageColorTitleOnly = false;
    }

    internal static void SetDefaultImage(DisplayConfig display, string? imagePath, List<int>? color = null)
    {
        if (string.IsNullOrEmpty(imagePath))
        {
            display.DefaultImageLines = null;
            display.DefaultImageColor = null;
            return;
        }

        display.DefaultImageLines = SvgToBrailleConverter.Convert(imagePath, "");
        display.DefaultImageColor = color;
    }

    // Предмет с читаемым текстом (item.Text): страница 0 — картинка+название (как обычный предмет),
    // страницы 1..N — сам текст, порезанный на страницы под высоту панели. Без текста — тот же
    // SetSelectedImage, что и раньше, NotePageCount=0 (стрелок листания нет).
    private sealed class PendingPicture
    {
        public Task? Ready;
        public Action? Apply;
        public string[]? Previous;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DisplayConfig, PendingPicture> PendingPictures = new();

    internal static bool PreparePicture(DisplayConfig display, string? image, Action apply)
    {
        var pending = PendingPictures.GetOrCreateValue(display);
        pending.Apply = null;
        var ready = SvgToBrailleConverter.PrepareAsync(image);
        if (ready.IsCompleted) return true;
        pending.Ready = ready;
        pending.Previous = display.SelectedImageLines;
        pending.Apply = apply;
        return false;
    }

    internal static bool ApplyPreparedPicture(DisplayConfig display)
    {
        if (!PendingPictures.TryGetValue(display, out var pending) || pending.Ready?.IsCompleted != true || pending.Apply == null) return false;
        var apply = pending.Apply;
        pending.Apply = null;
        if (!ReferenceEquals(pending.Previous, display.SelectedImageLines)) return false;
        apply();
        return true;
    }

    internal static void CancelPreparedPicture(DisplayConfig display)
    {
        if (PendingPictures.TryGetValue(display, out var pending)) pending.Apply = null;
    }

    internal static void SetSelectedInventoryItem(DisplayConfig display, HeroInventory item, int page)
    {
        if (!PreparePicture(display, item.Image, () => SetSelectedInventoryItem(display, item, page))) return;
        if (string.IsNullOrEmpty(item.Text))
        {
            SetSelectedImage(display, item.Image, item.Name, item.Color);
            display.NotePage = 0;
            display.NotePageCount = 0;
            return;
        }

        int total = display.MaxHistoryLines;
        // Отступ 1 символ от рамки панели слева и справа — PadRight держит все строки текста одной
        // длины, поэтому центрирующий leftPad в BuildRightPanelLines у них у всех одинаковый (не
        // "по факту центр каждой строки", а ровный левый край с полями).
        int textWidth = Math.Max(1, display.DialogRightPanelWidth - 2);
        // Название и индикатор страниц всегда внизу (как на странице с картинкой) — текст начинается
        // с первой строки: область текста + пустая + название + индикатор.
        int textAreaLines = Math.Max(1, total - 3);
        var textPages = BuildNotePages(item.Text, textWidth, textAreaLines);
        int pageCount = 1 + textPages.Count;
        page = Math.Clamp(page, 0, pageCount - 1);

        List<string> lines;
        if (page == 0)
        {
            // Название всегда зафиксировано на предпоследней строке панели — высота самой картинки
            // (18 строк у настоящей иконки, 9 у заглушки d20) не должна на это влиять, иначе у
            // маленькой картинки название/индикатор всплывали бы выше нужного.
            var realArt = string.IsNullOrEmpty(item.Image) ? null : SvgToBrailleConverter.Convert(item.Image, "");
            if (realArt != null)
            {
                // Настоящая иконка занимает почти всю высоту панели — верхний паддинг не нужен,
                // центрирование заметно только у короткой заглушки d20 (см. BuildCenteredArtWithName).
                lines = [.. realArt];
                while (lines.Count < total - 2) lines.Add("");
                lines.Add(FitPanelName(display, item.Name) ?? "");
            }
            else
            {
                lines = BuildCenteredArtWithName(DiceArt.D20.Split('\n'), item.Name, total);
            }
        }
        else
        {
            lines = [];
            foreach (var l in textPages[page - 1]) lines.Add(l.PadRight(textWidth));
            while (lines.Count < textAreaLines) lines.Add("");
            lines.Add("");
            lines.Add(FitPanelName(display, item.Name) ?? "");
        }
        lines.Add(DialogDisplay.NotePageIndicatorText(page, pageCount));

        display.SelectedImageLines = lines.ToArray();
        display.SelectedImageColor = item.Color;
        display.SelectedImageColorTitleOnly = false;
        display.NotePage = page;
        display.NotePageCount = pageCount;
    }

    private static List<List<string>> BuildNotePages(string text, int width, int linesPerPage)
    {
        var wrapped = TextWrapper.WrapText(text, width);
        var pages = new List<List<string>>();
        for (int i = 0; i < wrapped.Count; i += linesPerPage)
            pages.Add(wrapped.Skip(i).Take(linesPerPage).ToList());
        if (pages.Count == 0) pages.Add([]);
        return pages;
    }

    // Карточка монстра — по раскладке как записка в инвентаре (SetSelectedInventoryItem): страница 0
    // — картинка+имя, дальше — статы постранично. В отличие от карточки заклинания (шапка
    // зафиксирована, листается только текст) тут шапки нет вообще — весь statblock просто один
    // длинный текст, порезанный на страницы, картинка всегда первая.
    // info == null — вид не опознан (или monsterKey не задан у гомбрю-существа): обычная картинка
    // без статов, как у любой другой сущности без текста.
    internal static void SetSelectedMonster(DisplayConfig display, LivingEntity entity, MonsterInfo? info, int page)
    {
        if (info == null)
        {
            SetSelectedImage(display, entity.Image, entity.Name, entity.Color);
            display.NotePage = 0;
            display.NotePageCount = 0;
            return;
        }

        int total = display.MaxHistoryLines;
        int textWidth = Math.Max(1, display.DialogRightPanelWidth - 2);
        int textAreaLines = Math.Max(1, total - 3);
        var palette = new MonsterCard.Palette(
            Dim: ColorHelper.Darker(display.MainForeground, 0.6),
            Accent: entity.Color ?? display.MainForeground,
            Good: [120, 205, 120],
            Warn: [230, 170, 80]);
        var textPages = MonsterCard.Paginate(MonsterCard.Build(info, textWidth, palette), textAreaLines);
        int pageCount = 1 + textPages.Count;
        page = Math.Clamp(page, 0, pageCount - 1);

        List<string> lines;
        List<(string, List<int>?)>?[]? segments = null;
        if (page == 0)
        {
            var realArt = string.IsNullOrEmpty(entity.Image) ? null : SvgToBrailleConverter.Convert(entity.Image, "");
            if (realArt != null)
            {
                lines = [.. realArt];
                while (lines.Count < total - 2) lines.Add("");
                lines.Add(entity.Name);
            }
            else
            {
                lines = BuildCenteredArtWithName(DiceArt.D20.Split('\n'), entity.Name, total);
            }
        }
        else
        {
            // Многоцветный текст статов: строки-заглушки той же ширины (для выравнивания панели) +
            // отрезки с цветами (DialogDisplay рисует их вместо заглушек). Имя внизу — цветом существа.
            lines = [];
            var segs = new List<List<(string, List<int>?)>?>();
            foreach (var l in textPages[page - 1])
            {
                lines.Add(l.Plain.PadRight(textWidth));
                segs.Add(l.Segments);
            }
            while (lines.Count < textAreaLines) { lines.Add(new string(' ', textWidth)); segs.Add(null); }
            lines.Add("");
            segs.Add(null);
            lines.Add(entity.Name);
            segs.Add([(entity.Name, palette.Accent)]);
            segments = [.. segs];
        }
        lines.Add(DialogDisplay.NotePageIndicatorText(page, pageCount));

        var lineArray = lines.ToArray();
        display.SelectedImageLines = lineArray;
        display.SelectedImageSegments = segments;
        display.SelectedImageSegmentsFor = segments != null ? lineArray : null;
        display.SelectedImageColor = entity.Color;
        display.SelectedImageColorTitleOnly = false;
        display.NotePage = page;
        display.NotePageCount = pageCount;
    }

    // Абзацы → перенос слов построчно, между абзацами пустая строка (как WrapSpellText, только без
    // разбора <br> — тут абзацы уже разложены заранее списком).
    private static List<string> WrapParagraphs(List<string> paragraphs, int width)
    {
        var lines = new List<string>();
        foreach (var raw in paragraphs)
        {
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            if (lines.Count > 0) lines.Add("");
            lines.AddRange(TextWrapper.WrapText(trimmed, width));
        }
        return lines.Count > 0 ? lines : [L.T("Нет данных.")];
    }

    // Ширина колонки лейблов в блоке время/дистанция/компоненты/длительность — все значения
    // выравниваются под самый длинный лейбл ("Длительность"), по аналогии с колонкой статов
    // персонажа (HeroDisplay.DrawHeroCard: key.PadRight(leftKeyMax) + разделитель).
    private static int SpellFieldLabelWidth =>
        new[] { L.T("Время"), L.T("Дистанция"), L.T("Компоненты"), L.T("Длительность") }.Max(l => l.Length);

    // Карточка заклинания: шапка (название, круг+школа, время/дистанция/компоненты/длительность)
    // остаётся неизменной на каждой странице, листается только текст описания — в отличие от
    // предмета с запиской (там первая страница — картинка, а текст с самого начала).
    // Круг/школа/время и т.д. берутся из SpellDatabase (файл GameData/DnD5e_spells_BD.dtn) по имени
    // заклинания; если имя не нашлось в базе (например, придуманное ИИ заклинание не из справочника),
    // показываем то немногое, что есть у самого героя (имя, круг), без остальных полей.
    internal static void SetSelectedSpell(DisplayConfig display, HeroSpell spell, int page)
    {
        CancelPreparedPicture(display);
        var info = SpellDatabase.Find(spell.Name)?.Localized;
        int total = display.MaxHistoryLines;
        int textWidth = Math.Max(1, display.DialogRightPanelWidth - 2);

        string levelText = spell.Level <= 0 ? L.T("Заговор") : L.F("{0} круг", LevelToRoman(spell.Level));
        string schoolText = Capitalize(info?.School);
        string subtitle = schoolText.Length == 0 ? levelText : $"{levelText}, {schoolText}";

        var header = new List<string>
        {
            spell.Name.ToUpperInvariant(),
            subtitle,
            new string('─', textWidth),
            SpellField(L.T("Время"), info?.CastingTime, textWidth),
            SpellField(L.T("Дистанция"), info?.Range, textWidth),
            SpellField(L.T("Компоненты"), info?.Components, textWidth),
            SpellField(L.T("Длительность"), info?.Duration, textWidth),
            new string('─', textWidth),
        };

        int textAreaLines = Math.Max(1, total - header.Count - 1);
        var wrapped = WrapSpellText(info?.Text, textWidth);
        var pages = new List<List<string>>();
        for (int i = 0; i < wrapped.Count; i += textAreaLines)
            pages.Add(wrapped.Skip(i).Take(textAreaLines).ToList());
        if (pages.Count == 0) pages.Add([]);
        int pageCount = pages.Count;
        page = Math.Clamp(page, 0, pageCount - 1);

        var lines = new List<string>(header);
        foreach (var l in pages[page]) lines.Add(l.PadRight(textWidth));
        while (lines.Count < header.Count + textAreaLines) lines.Add(new string(' ', textWidth));
        lines.Add(pageCount > 1 ? DialogDisplay.NotePageIndicatorText(page, pageCount) : "");

        display.SelectedImageLines = lines.ToArray();
        display.SelectedImageColor = spell.Color;
        display.SelectedImageColorTitleOnly = true;
        display.NotePage = page;
        display.NotePageCount = pageCount;
    }

    private static string SpellField(string label, string? value, int width)
    {
        string text = label.PadRight(SpellFieldLabelWidth) + "  " + (string.IsNullOrEmpty(value) ? "—" : value);
        if (text.Length > width) text = text[..width];
        return text.PadRight(width);
    }

    // "<br>" в тексте базы разделяет абзацы (не буквальный перенос строки) — раскладываем каждый
    // абзац отдельным TextWrapper.WrapText, между абзацами пустая строка.
    private static List<string> WrapSpellText(string? raw, int width)
    {
        if (string.IsNullOrEmpty(raw)) return [L.T("Нет описания.")];
        var paragraphs = raw.Replace("<br/>", "<br>").Replace("<br />", "<br>").Split("<br>");
        var lines = new List<string>();
        foreach (var p in paragraphs)
        {
            string trimmed = p.Trim();
            if (trimmed.Length == 0) continue;
            if (lines.Count > 0) lines.Add("");
            lines.AddRange(TextWrapper.WrapText(trimmed, width));
        }
        return lines.Count > 0 ? lines : [L.T("Нет описания.")];
    }

    private static string Capitalize(string? s) =>
        string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s[1..];

    private static string LevelToRoman(int level) => level switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V",
        6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX",
        _ => level.ToString(),
    };

    internal static async Task HandleGameScreen(
        WorldState settings, DisplayConfig display, Storage storage, ScreenConfig screen,
        Action draw, Func<string, DialogDisplay?, Task> onInput,
        string title,
        HeroDisplay? heroDisplay = null,
        Action? onScrollUp = null, Action? onScrollDown = null,
        Action? onScrollLeft = null, Action? onScrollRight = null,
        Action? onTab = null, Action? onShiftTab = null,
        Action? onF6 = null, Action? onF7 = null, Action? onF8 = null, Action? onF9 = null, Action? onF10 = null,
        Action<int, int>? onInventorySelect = null,
        Action<int, int>? onSpellSelect = null,
        Action<DialogDisplay?>? setActiveDialog = null,
        Func<DialogDisplay, Action>? pollFactory = null,
        Func<string, bool>? onTextInput = null,
        Action? onDelete = null)
    {
        var titleTabs = ComputeTitleTabs(title);
        int startTop = Console.CursorTop;
        while (true)
        {
            Console.CursorVisible = false;
            Console.SetCursorPosition(0, startTop);
            draw();
            var history = new DialogDisplay(settings, display, storage);
            if (pollFactory != null)
            {
                // Свой опрос мыши экрана (журнал): он же — во время ответа ИИ (DialogDisplay.SwitchTab).
                display.PollAction = pollFactory(history);
                display.JournalPollActionFactory = dlg => pollFactory((DialogDisplay)dlg);
                display.OnTabKey = onTab;
                display.OnShiftTabKey = onShiftTab;
                display.JournalOnTabKey = onTab;
                display.JournalOnShiftTabKey = onShiftTab;
            }
            else if (heroDisplay != null && (onScrollLeft != null || onScrollRight != null))
            {
                display.PollAction = MakeCharacterPollAction(history, heroDisplay, startTop, draw, onScrollLeft, onScrollRight, title, titleTabs, display, onInventorySelect, onSpellSelect);
                var chd = heroDisplay; var cst = startTop; var cdr = draw;
                var csl = onScrollLeft; var csr = onScrollRight;
                var cti = title; var ctb = titleTabs; var ciih = onInventorySelect; var cish = onSpellSelect;
                display.CharacterPollActionFactory = dlg => MakeCharacterPollAction(
                    (DialogDisplay)dlg, chd!, cst, cdr, csl, csr, cti, ctb, display, ciih, cish);
                display.OnTabKey = onTab;
                display.OnShiftTabKey = onShiftTab;
                display.CharacterOnTabKey = onTab;
                display.CharacterOnShiftTabKey = onShiftTab;
            }
            else
            {
                display.PollAction = MakeDialogArrowPollAction(history, title, titleTabs, display);
                var cti2 = title; var ctb2 = titleTabs;
                display.AbilitiesPollActionFactory = dlg => MakeDialogArrowPollAction(
                    (DialogDisplay)dlg, cti2, ctb2, display);
                display.OnTabKey = null;
                display.OnShiftTabKey = null;
            }
            display.RedrawCurrentContent = () =>
            {
                int sl = Console.CursorLeft, st = Console.CursorTop;
                Console.CursorVisible = false;
                Console.SetCursorPosition(0, startTop);
                draw();
                Console.SetCursorPosition(sl, st);
            };
            var input = history.Draw();
            ConsoleMouseReader.SetCursorShape(false);

            if (!string.IsNullOrEmpty(input) && screen.Commands.TryGetValue(input, out var screenAction))
            {
                screenAction();
                display.PollAction = null;
                return;
            }

            // Звук только если реально что-то листает — как у мыши (недоступная стрелка молчит).
            if (input == "Up" && onScrollUp != null) { display.PollAction = null; onScrollUp(); return; }
            if (input == "Down" && onScrollDown != null) { display.PollAction = null; onScrollDown(); return; }
            // Журнал листается анимацией на месте (звук — в самом перелистывании), экран не перестраивается.
            if (pollFactory != null && input is "Left" or "Right")
            {
                (input == "Left" ? onScrollLeft : onScrollRight)?.Invoke();
                continue;
            }
            if (input == "Left" && onScrollLeft != null)
            {
                if (heroDisplay == null || heroDisplay.SubTabInfo.canScrollLeft) Sound.PlayClick();
                display.PollAction = null; onScrollLeft(); return;
            }
            if (input == "Right" && onScrollRight != null)
            {
                if (heroDisplay == null || heroDisplay.SubTabInfo.canScrollRight) Sound.PlayClick();
                display.PollAction = null; onScrollRight(); return;
            }
            bool isSpellNote = display.CharacterSubTab == CharacterSubTab.Spells;
            var notePageSelect = isSpellNote ? onSpellSelect : onInventorySelect;
            int notePagePinnedIdx = isSpellNote ? display.PinnedSpellIndex : display.PinnedInventoryIndex;
            if (input == "NoteLeft"  && notePageSelect != null && notePagePinnedIdx >= 0)
            {
                if (display.NotePage > 0) Sound.PlayClick();
                notePageSelect(notePagePinnedIdx, display.NotePage - 1); continue;
            }
            if (input == "NoteRight" && notePageSelect != null && notePagePinnedIdx >= 0)
            {
                if (display.NotePage < display.NotePageCount - 1) Sound.PlayClick();
                notePageSelect(notePagePinnedIdx, display.NotePage + 1); continue;
            }
            if (input == "Tab"      && onTab      != null) { onTab();      while (Console.KeyAvailable) Console.ReadKey(intercept: true); continue; }
            if (input == "ShiftTab" && onShiftTab != null) { onShiftTab(); while (Console.KeyAvailable) Console.ReadKey(intercept: true); continue; }
            if (input == "F6" && onF6 != null) { onF6(); continue; }
            if (input == "F7" && onF7 != null) { onF7(); continue; }
            if (input == "F8" && onF8 != null) { onF8(); continue; }
            if (input == "F9" && onF9 != null) { onF9(); continue; }
            if (input == "F10" && onF10 != null) { onF10(); continue; }
            if (input == "Delete" && onDelete != null) { onDelete(); continue; }
            // Текст, который экран забирает себе (заметки журнала), мастеру не уходит.
            if (!string.IsNullOrEmpty(input) && onTextInput != null && onTextInput(input)) continue;
            // F-клавиша, которая на этом экране ничего не делает, — игнорируется, а не уходит мастеру текстом.
            if (input is not null && input.Length is 2 or 3 && input[0] == 'F' && char.IsDigit(input[1]) && input[1..].All(char.IsDigit)) continue;
            if (input is "Left" or "Right" or "Up" or "Down" or "Tab" or "ShiftTab" or "NoteLeft" or "NoteRight" or "Delete") continue;

            if (!string.IsNullOrEmpty(input))
            {
                settings.History ??= [];
                settings.History.Add(new NaviDnD.Data.Models.DialogMessage { Author = settings.Hero?.Name ?? "Hero", Text = input });
                display.DialogScrollOffset = 0;

                if (setActiveDialog != null)
                {
                    setActiveDialog(history);
                    history.PrepareStreaming();
                    await onInput(input, history); // PollAction stays set throughout streaming
                    history.FinalizeStreaming();
                    setActiveDialog(null);
                }
                else
                {
                    Console.WriteLine();
                    var aiTask = onInput(input, null);
                    await Helpers.Spinner.While(aiTask);
                    await aiTask;
                }
            }
            display.PollAction = null;
            return;
        }
    }
}
