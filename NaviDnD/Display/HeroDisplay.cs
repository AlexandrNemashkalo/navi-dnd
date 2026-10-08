using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

public class HeroDisplay
{
    private readonly WorldState _settings;
    private readonly DisplayConfig _display;

    // Пример при активной вкладке "inv": "  → ИНВЕНТАРЬ ←    [F7]СПОСОБНОСТИ    [F8]СОСТОЯНИЯ      ◄ ►"
    // Каждая вкладка занимает ФИКСИРОВАННУЮ ширину (SubTabPrefixLen + label + SubTabSuffixLen)
    // независимо от активности — меняется только сам декоратор ("[Fn]"/"  " ↔ "  → "/" ←"), а не его
    // длина, поэтому название вкладки никогда не сдвигается при переключении между ними.
    private const int SubTabIndent = 3;
    private const int SubTabGap = 6;
    private const int SubTabPrefixLen = 4; // "[Fn]" или "  → "
    private const int SubTabSuffixLen = 2; // "  " (неактивна) или " ←" (активна)
    private const int SubTabBarContentLen = 137; // до "◄ N/M ►"

    private static (string Key, string Label, string FKey)[] _subTabs =>
    [
        ("inv",  L.T("ИНВЕНТАРЬ"),   "F6"),
        ("abil", L.T("СПОСОБНОСТИ"), "F7"),
        ("eff",  L.T("СОСТОЯНИЯ"),   "F8"),
        ("spell",L.T("ЗАКЛИНАНИЯ"),  "F9"),
    ];

    private BorderDrawer? _cachedBorderDrawer;
    private int _subTabTitleY = -1;
    private int _subTabContentStart = -1;
    private bool _canScrollLeft;
    private bool _canScrollRight;
    private int _subTabPage;
    private int _subTabPageCount = 1;
    private bool _leftArrowHovered;
    private bool _rightArrowHovered;
    private string? _hoveredSubTab; // "inv", "abil", "eff", or null

    private int _invContentStartY = -1;
    private int _invRows = 0;
    private int _invSplitX = -1;
    private int[] _invCol1RowToItem = [];
    private int[] _invCol2RowToItem = [];

    private int _spellContentStartY = -1;
    private int _spellRows = 0;
    private int[] _spellSplitX = []; // 3 границы между 4 колонками (абсолютные экранные X)
    private int[][] _spellColRowToItem = [];

    // Нет ни одного не удалённого заклинания у героя — вкладка "ЗАКЛИНАНИЯ" показывается серой и
    // некликабельной (SubTabInfo не отдаёт под неё зону клика/наведения), чтобы не вести на пустую
    // страницу без заклинаний. Public — Program.cs проверяет то же самое перед [F9].
    // Сколько герой видит сейчас (как обзор на карте, MapObjectsProvider): под открытым небом — по свету дня
    // (ночью — тёмное зрение или 10 фт), под крышей — своё зрение или «по свету» (факелы, светящиеся предметы).
    // Раньше в карточке стояло поле visionFt как есть — пустое у большинства героев и не то, что на карте.
    private string SightText(Hero hero)
    {
        if (hero.VisionFt == 0) return L.T("слеп");
        int dv = hero.DarkvisionFt ?? 0;
        string dark = dv > 0 ? " · " + L.F("тёмн. {0}", dv) : "";
        if (hero.Position is { Count: >= 2 } p && _settings.Map.TerrainAt(p[0], p[1]) is { Indoor: false })
        {
            int light = MovementCalculator.DaylightFt(_settings, p[0], p[1]) ?? Math.Max(dv, 10);
            int ft = hero.VisionFt is > 0 and var v ? Math.Min(v, light) : light;
            return L.F("{0} фт", ft) + (_settings.Time.PartOfDay == PartsOfDay.Night ? " " + L.T("(ночь)") : "");
        }
        return (hero.VisionFt is > 0 and var own ? L.F("{0} фт", own) : L.T("по свету")) + dark;
    }

    public bool HasSpells => _settings.Hero?.Spells?.Any(s => s.Deleted != true) == true;

    private static string ActiveKey(CharacterSubTab tab) => tab switch
    {
        CharacterSubTab.Inventory => "inv",
        CharacterSubTab.Abilities => "abil",
        CharacterSubTab.Spells => "spell",
        _ => "eff",
    };

    // *StartX..*EndX — clickable zone for the inactive "[Fn]Название" label (-1,-1 if that tab is active)
    // leftX, rightX — positions of ◄ and ►
    public (int invStartX, int invEndX, int abilStartX, int abilEndX, int effStartX, int effEndX,
            int spellStartX, int spellEndX,
            int leftX, int rightX, int titleY, bool canScrollLeft, bool canScrollRight) SubTabInfo
    {
        get
        {
            if (_subTabTitleY < 0 || _subTabContentStart < 0)
                return (-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, false, false);

            int cs = _subTabContentStart;
            string activeKey = ActiveKey(_display.CharacterSubTab);
            var zones = new Dictionary<string, (int start, int end)>();
            int pos = SubTabIndent;
            for (int i = 0; i < _subTabs.Length; i++)
            {
                var (key, label, _) = _subTabs[i];
                bool active = key == activeKey;
                int slotWidth = SubTabPrefixLen + label.Length + SubTabSuffixLen;
                bool clickable = key != "spell" || HasSpells;
                // Кликабельная зона — сам "[Fn]Название" без хвостовых пробелов-заполнителя.
                // Без подсказки «[Fn]»: наведение на неё не подсвечивает вкладку и не меняет курсор.
                if (!active && clickable) zones[key] = (cs + pos + SubTabPrefixLen, cs + pos + SubTabPrefixLen + label.Length);
                pos += slotWidth;
                if (i < _subTabs.Length - 1) pos += SubTabGap;
            }

            (int s, int e) Z(string k) => zones.TryGetValue(k, out var v) ? v : (-1, -1);
            var inv = Z("inv"); var abil = Z("abil"); var eff = Z("eff"); var spell = Z("spell");

            int leftX = cs + SubTabBarContentLen;
            int rightX = leftX + 3 + SubTabPageText().Length;
            return (inv.s, inv.e, abil.s, abil.e, eff.s, eff.e, spell.s, spell.e,
                    leftX, rightX,
                    _subTabTitleY, _canScrollLeft, _canScrollRight);
        }
    }

    public (int startY, int rows, int splitX, int[] col1Map, int[] col2Map) InventoryHitInfo =>
        (_invContentStartY, _invRows, _invSplitX, _invCol1RowToItem, _invCol2RowToItem);

    // splitX — 3 границы между 4 колонками; colMaps[col][row] — глобальный индекс в SortedSpells(hero),
    // или -1, если в этой ячейке нет заклинания (аналог InventoryHitInfo, но на 4 колонки).
    public (int startY, int rows, int[] splitX, int[][] colMaps) SpellsHitInfo =>
        (_spellContentStartY, _spellRows, _spellSplitX, _spellColRowToItem);

    public void SetSubTabArrowHover(bool leftHovered, bool rightHovered)
    {
        if (leftHovered == _leftArrowHovered && rightHovered == _rightArrowHovered) return;
        _leftArrowHovered = leftHovered;
        _rightArrowHovered = rightHovered;
        RedrawSubTabTitle();
    }

    public void SetSubTabLabelHover(string? hovered) // "inv", "abil", "eff", or null
    {
        if (hovered == _hoveredSubTab) return;
        _hoveredSubTab = hovered;
        RedrawSubTabTitle();
    }

    // Текст "текущая/всего" между стрелками ◄/► — одно место, чтобы формула здесь и в SubTabInfo
    // (позиция ► зависит от длины этого текста) не разъехались.
    private string SubTabPageText() => $"{_subTabPage + 1}/{_subTabPageCount}";

    private void RedrawSubTabTitle()
    {
        if (_cachedBorderDrawer == null || _subTabTitleY < 0) return;
        ColorHelper.SetBackgroundColor(_display.MainBackground);
        ColorHelper.SetForegroundColor(_display.MainForeground);
        Console.CursorVisible = false;
        int savedLeft = Console.CursorLeft, savedTop = Console.CursorTop;
        Console.SetCursorPosition(0, _subTabTitleY);
        DrawSubTabTitle(_display.CharacterSubTab);
        Console.SetCursorPosition(savedLeft, savedTop);
    }

    private void DrawSubTabTitle(CharacterSubTab active)
    {
        string activeKey = ActiveKey(active);
        var dim = _display.MainForeground;
        var hoverCol = ColorHelper.Pale(_display.MainForeground, 0.75);
        var arrowHi  = ColorHelper.Pale(_display.MainForeground, 0.75);
        var arrowInactive = ColorHelper.Darker(_display.MainForeground, 0.5);
        // Ввод временно недоступен (кубик крутится) — [Fn]-префиксы и обе стрелки затемнены,
        // независимо от того, реально ли можно листать/наводить прямо сейчас.
        bool disabled = _display.InputDisabled;
        var leftColor  = disabled ? arrowInactive : !_canScrollLeft  ? arrowInactive : (_leftArrowHovered  ? arrowHi : _display.MainForeground);
        var rightColor = disabled ? arrowInactive : !_canScrollRight ? arrowInactive : (_rightArrowHovered ? arrowHi : _display.MainForeground);
        string pageText = SubTabPageText();

        _cachedBorderDrawer!.DrawTitleLine(() =>
        {
            _subTabContentStart = Console.CursorLeft;
            Console.Write(new string(' ', SubTabIndent));
            int written = SubTabIndent;
            for (int i = 0; i < _subTabs.Length; i++)
            {
                var (key, label, fkey) = _subTabs[i];
                bool active2 = key == activeKey;
                bool noSpells = key == "spell" && !HasSpells;
                var color = noSpells ? arrowInactive : active2 ? hoverCol : _hoveredSubTab == key ? hoverCol : dim;
                // Декоратор всегда SubTabPrefixLen/SubTabSuffixLen символов — меняется его вид,
                // не длина, поэтому название вкладки не сдвигается при переключении.
                string prefix = active2 ? "  → " : $"[{fkey}]";
                string suffix = active2 ? " ←" : "  ";
                var prefixColor = disabled && !active2 ? arrowInactive : active2 ? color : MouseUiHelper.KeyHintColor(_display);
                if (!active2 && !disabled) MouseUiHelper.WriteKeyHint(prefix, _display);
                else ColorHelper.WriteColored(prefix, prefixColor);
                ColorHelper.WriteColored(label + suffix, color);
                written += SubTabPrefixLen + label.Length + SubTabSuffixLen;
                if (i < _subTabs.Length - 1)
                {
                    Console.Write(new string(' ', SubTabGap));
                    written += SubTabGap;
                }
            }
            int filler = Math.Max(0, SubTabBarContentLen - written);
            ColorHelper.WriteColored(new string(' ', filler), _display.MainForeground);
            ColorHelper.WriteColored("◄", leftColor);
            Console.Write(" ");
            Console.Write(pageText);
            Console.Write(" ");
            ColorHelper.WriteColored("►", rightColor);
        }, SubTabBarContentLen + pageText.Length + 4);
    }

    public HeroDisplay(WorldState settings, DisplayConfig display)
    {
        _settings = settings;
        _display = display;
    }

    private class ColoredLine
    {
        public string Prefix { get; set; }
        public string ColoredText { get; set; }
        public string Suffix { get; set; }
        public List<int>? Color { get; set; }
        public bool HasColor => Color != null && Color.Count == 3;
        public string? FullText { get; set; } // значение не влезло и обрезано с «...» — полный текст для бегущей строки
    }

    // ── Обрезанные значения карточки и бегущая строка при наведении ─────────────
    // Значение, не влезающее в колонку, рисуется обрезанным с «...» (без переноса строки); при наведении
    // мыши оно через паузу прокручивается, как новостная лента (UpdateMarquee из опроса мыши).
    private readonly List<(int y, int x, int width, string text, List<int>? color)> _truncated = [];
    private int _marqueeIdx = -1;
    private DateTime _marqueeStart;
    private int _marqueeStep = -1;
    private const int MarqueeStepMs = 160;
    private const int MarqueePauseSteps = 4;
    private const string MarqueeGap = "   ";

    private static ColoredLine FitLine(string prefix, string text, int width, List<int>? color)
    {
        int available = Math.Max(1, width - prefix.Length);
        bool truncated = text.Length > available;
        string shown = truncated ? (available > 3 ? text[..(available - 3)] + "..." : text[..available]) : text;
        return new ColoredLine
        {
            Prefix = prefix,
            ColoredText = shown,
            Suffix = new string(' ', Math.Max(0, width - (prefix.Length + shown.Length))),
            Color = color,
            FullText = truncated ? text : null,
        };
    }

    public void UpdateMarquee((short x, short y)? mouse)
    {
        if (mouse is { } m)
        {
            int idx = _truncated.FindIndex(t => t.y == m.y && m.x >= t.x && m.x < t.x + t.width);
            if (idx != _marqueeIdx)
            {
                if (_marqueeIdx >= 0 && _marqueeIdx < _truncated.Count) DrawMarqueeFrame(_marqueeIdx, -1);
                _marqueeIdx = idx;
                _marqueeStart = DateTime.UtcNow;
                _marqueeStep = -1;
            }
        }
        if (_marqueeIdx < 0 || _marqueeIdx >= _truncated.Count) return;

        int step = Math.Max(0, (int)((DateTime.UtcNow - _marqueeStart).TotalMilliseconds / MarqueeStepMs) - MarqueePauseSteps);
        if (step == _marqueeStep) return;
        _marqueeStep = step;
        DrawMarqueeFrame(_marqueeIdx, step);
    }

    // step < 0 — исходный обрезанный вид с «...»; иначе окно ленты «текст   текст» со сдвигом step.
    private void DrawMarqueeFrame(int idx, int step)
    {
        var (y, x, width, text, color) = _truncated[idx];
        string frame;
        if (step < 0)
            frame = width > 3 ? text[..(width - 3)] + "..." : text[..width];
        else
        {
            string loop = text + MarqueeGap;
            int offset = step % loop.Length;
            frame = (loop + loop)[offset..(offset + width)];
        }

        bool vis = Console.CursorVisible;
        int sl = Console.CursorLeft, st = Console.CursorTop;
        Console.CursorVisible = false;
        Console.SetCursorPosition(x, y);
        if (color is { Count: 3 }) ColorHelper.WriteColored(frame, color);
        else Console.Write(frame);
        Console.SetCursorPosition(sl, st);
        Console.CursorVisible = vis;
    }

    private readonly List<(int y, int[] items, Action draw)> _selectionRows = [];
    private (int inv, int invPin, int spell, int spellPin) _selectionState;

    public void RefreshSelection()
    {
        var next = (_display.SelectedInventoryIndex, _display.PinnedInventoryIndex,
            _display.SelectedSpellIndex, _display.PinnedSpellIndex);
        if (next == _selectionState) return;
        int[] changed = [_selectionState.inv, _selectionState.invPin, _selectionState.spell,
            _selectionState.spellPin, next.Item1, next.Item2, next.Item3, next.Item4];
        int x = Console.CursorLeft, y = Console.CursorTop;
        bool visible = Console.CursorVisible;
        try
        {
            Console.CursorVisible = false;
            foreach (var row in _selectionRows)
                if (row.items.Any(i => i >= 0 && changed.Contains(i)))
                {
                    Console.SetCursorPosition(0, row.y);
                    row.draw();
                }
            _selectionState = next;
        }
        finally { Console.SetCursorPosition(x, y); Console.CursorVisible = visible; }
    }

    public void DrawHeroCard()
    {
        _selectionRows.Clear();
        _selectionState = (_display.SelectedInventoryIndex, _display.PinnedInventoryIndex, _display.SelectedSpellIndex, _display.PinnedSpellIndex);
        var settings = _settings;
        var hero = settings.Hero;
        if (hero == null) return;

        int startTop = Console.CursorTop;

        ColorHelper.SetBackgroundColor(_display.MainBackground);
        ColorHelper.SetForegroundColor(_display.MainForeground);

        int innerWidth = _display.InnerWidth(_display.ViewCols(settings.Map));
        var borderDrawer = new BorderDrawer(settings, _display);
        _cachedBorderDrawer = borderDrawer;

        borderDrawer.DrawSeparatorTop();

        int leftKeyMax = hero.EquipmentSlots.Max(s => s.Length);
        leftKeyMax = Math.Max(leftKeyMax, L.T("Вдохновение").Length);
        leftKeyMax = Math.Max(leftKeyMax, hero.Resources?.Count > 0 ? hero.Resources.Max(s => s.Name.Length) : 0);

        // Раса/Класс — обычные hero.stats (как Опыт/Уровень и т.п.), но показываются не в колонке
        // статов, а сразу под именем (это база персонажа) — вытаскиваются из статов отдельно,
        // до сборки middleItems, чтобы не задваивались.
        string? StatValue(string key) => hero.Stat(key)?.Value;
        string? heroRace = StatValue(StatKeys.Race);
        string? heroClass = StatValue(StatKeys.Class);

        // Заклинательные статы — та же логика: обычные hero.stats, но показываются не в колонке
        // статов, а в динамическом блоке колонки 1 (только на подвкладке Заклинания), перед ячейками
        // заклинаний (когда те появятся) и ресурсами категории "Заклинания".
        // Подготовлено/Известно — стат хранит только МАКСИМУМ (число, по правилам класса/уровня),
        // текущее число всегда считается тут же из hero.spells (не заговоры — Level > 0), а не
        // держится отдельной цифрой в статах: раньше ИИ должен был вручную пересчитывать N при
        // каждой подготовке/снятии заклинания и не поспевал — число расходилось с реальным списком.
        int preparedSpellCount = hero.Spells?.Count(s => s.Deleted != true && s.Level > 0 && s.Prepared == true) ?? 0;
        int knownSpellCount    = hero.Spells?.Count(s => s.Deleted != true && s.Level > 0) ?? 0;
        string? preparedMax = StatValue(StatKeys.SpellsPrepared);
        string? knownMax    = StatValue(StatKeys.SpellsKnown);

        (string label, string? value)[] spellStatLines =
        [
            (L.T("Заклинательная х-ка"), StatValue(StatKeys.SpellAbility)),
            (L.T("Спасбросок закл."), StatValue(StatKeys.SpellSave)),
            (L.T("Атака заклинанием"), StatValue(StatKeys.SpellAttack)),
            (L.T("Подготовлено закл."), preparedMax != null ? $"{preparedSpellCount}/{preparedMax}" : null),
            (L.T("Известно закл."), knownMax != null ? $"{knownSpellCount}/{knownMax}" : null),
        ];
        leftKeyMax = Math.Max(leftKeyMax, spellStatLines.Max(l => l.label.Length));

        var leftSlots = new List<(string slotName, string itemName, List<int> itemColor)>
        {
            (L.T("Имя").PadRight(leftKeyMax) + "   ", $"{hero.Symbol} {hero.Name}", hero.Color),
        };
        if (!string.IsNullOrEmpty(heroRace))
            leftSlots.Add((L.T("Раса").PadRight(leftKeyMax) + "   ", heroRace, null));
        if (!string.IsNullOrEmpty(heroClass))
            leftSlots.Add((L.T("Класс").PadRight(leftKeyMax) + "   ", heroClass, null));
        leftSlots.Add(("HP".PadRight(leftKeyMax) + "   ", $"{hero.Hp} ❤", null));
        leftSlots.Add((L.T("Обзор").PadRight(leftKeyMax) + "   ", SightText(hero), null));
        leftSlots.Add((L.T("Вдохновение").PadRight(leftKeyMax) + "   ", hero.Inspiration == true ? L.T("Да") : "—", null));

        // Разделитель сразу после базового блока — дальше идёт контекстная часть колонки 1, разная
        // под каждую подвкладку: Инвентарь — слоты экипировки, Способности/Заклинания — их ресурсы
        // (Ярость/Ки/Кости Хитрости или ячейки и т.п., HeroResource.Category), Состояния — ничего
        // (там своя информация снизу). Слоты экипировки раньше показывались всегда — теперь только
        // на своей подвкладке, как и было задумано изначально.
        leftSlots.Add(("".PadRight(leftKeyMax) + "   ", "", null));

        switch (_display.CharacterSubTab)
        {
            case CharacterSubTab.Inventory:
                if (hero.EquipmentSlots != null && hero.EquipmentSlots.Any())
                {
                    var equipped = hero.Inventory?
                        .Where(i => i.EquipmentedSlot?.Count > 0)
                        .SelectMany(i => i.EquipmentedSlot!.Select(slot => (slot, i.Name, i.Color)))
                        .GroupBy(x => x.slot)
                        .ToDictionary(g => g.Key, g => (g.Last().Name, g.Last().Color)) ?? new();

                    foreach (var slot in hero.EquipmentSlots)
                    {
                        string prefixBase = $"{slot.PadRight(leftKeyMax)}   ";
                        if (equipped.TryGetValue(slot, out var itemInfo))
                            leftSlots.Add((prefixBase, itemInfo.Name, itemInfo.Color));
                        else
                            leftSlots.Add((prefixBase, "—", null));
                    }
                }
                else
                {
                    leftSlots.Add(("", L.T("(нет слотов)"), null));
                }
                break;

            case CharacterSubTab.Abilities:
                AddResourcesByCategory(ResourceCategories.Abilities);
                break;

            case CharacterSubTab.Spells:
                // Заклинательные статы — перед ячейками заклинаний (когда появятся) и ресурсами.
                foreach (var (label, value) in spellStatLines)
                    if (!string.IsNullOrEmpty(value))
                        leftSlots.Add((label.PadRight(leftKeyMax) + "   ", value, null));
                AddResourcesByCategory(ResourceCategories.Spells);
                break;
        }

        void AddResourcesByCategory(string category)
        {
            if (hero.Resources == null) return;
            foreach (var resource in hero.Resources.Where(r => r.Deleted != true
                && r.Category == category))
                leftSlots.Add((resource.Name.PadRight(leftKeyMax) + "   ", resource.Value ?? "", null));
        }

        var middleItems = new List<(string key, string value, List<int> valueColor)>();
        if (hero.Stats != null && hero.Stats.Any())
        {
            // Раса/Класс/заклинательные статы уже показаны в колонке 1 (leftSlots) — не дублируем.
            string[] shownElsewhere = [StatKeys.Race, StatKeys.Class, StatKeys.SpellAbility, StatKeys.SpellSave,
                StatKeys.SpellAttack, StatKeys.SpellsPrepared, StatKeys.SpellsKnown];
            foreach (var stat in hero.Stats.Where(s => s.Deleted != true && !shownElsewhere.Contains(s.Key)))
                middleItems.Add((stat.Name, stat.Value, null));
        }
        if (middleItems.Count == 0)
        {
            middleItems.Add(("", L.T("(нет характеристик)"), null));
        }

        var rightItems = new List<(string key, string value, List<int> valueColor)>();
        if (hero.Skills != null && hero.Skills.Any())
        {
            foreach (var skill in hero.Skills.Where(s => s.Deleted != true))
                rightItems.Add((skill.Name, skill.Value, null));
        }
        if (rightItems.Count == 0)
        {
            rightItems.Add(("", L.T("(нет навыков)"), null));
        }

        // -3: отступ слева в начале каждой из 3 колонок (Console.Write(" ") ниже).
        int colWidth = (innerWidth - 2 - 3) / 3;
        int leftWidth = colWidth;
        int middleWidth = colWidth;
        int rightWidth = innerWidth - 2 - 3 - leftWidth - middleWidth;

        int middleKeyMax = middleItems.Max(r => r.key.Length);
        int rightKeyMax  = rightItems.Max(r => r.key.Length);

        // Значения не переносятся: не влезло — обрезается с «...», полный текст — бегущей строкой при наведении.
        var leftLines = leftSlots.Select(slot => FitLine(slot.slotName, slot.itemName, leftWidth, slot.itemColor)).ToList();
        var middleLines = middleItems.Select(item => FitLine(item.key.PadRight(middleKeyMax) + "   ", item.value, middleWidth, item.valueColor)).ToList();
        var rightLines = rightItems.Select(item => FitLine(item.key.PadRight(rightKeyMax) + "   ", item.value, rightWidth, item.valueColor)).ToList();

        int maxLinesCount = Math.Max(leftLines.Count, Math.Max(middleLines.Count, rightLines.Count));
        while (leftLines.Count   < maxLinesCount) leftLines.Add(new ColoredLine   { Prefix = "", ColoredText = "", Suffix = new string(' ', leftWidth),   Color = null });
        while (middleLines.Count < maxLinesCount) middleLines.Add(new ColoredLine { Prefix = "", ColoredText = "", Suffix = new string(' ', middleWidth), Color = null });
        while (rightLines.Count  < maxLinesCount) rightLines.Add(new ColoredLine  { Prefix = "", ColoredText = "", Suffix = new string(' ', rightWidth),  Color = null });

        _truncated.Clear();
        _marqueeIdx = -1;
        // Начало текста колонок в строке: отступ + рамка + " " (колонка 1), далее «│ » между колонками.
        int leftX   = DisplayConfig.LeftMargin + 2;
        int middleX = leftX + leftWidth + 2;
        int rightX  = middleX + middleWidth + 2;

        for (int i = 0; i < maxLinesCount; i++)
        {
            var left   = leftLines[i];
            var middle = middleLines[i];
            var right  = rightLines[i];

            int rowY = Console.CursorTop;
            if (left.FullText != null)   _truncated.Add((rowY, leftX + left.Prefix.Length, left.ColoredText.Length, left.FullText, left.Color));
            if (middle.FullText != null) _truncated.Add((rowY, middleX + middle.Prefix.Length, middle.ColoredText.Length, middle.FullText, middle.Color));
            if (right.FullText != null)  _truncated.Add((rowY, rightX + right.Prefix.Length, right.ColoredText.Length, right.FullText, right.Color));

            borderDrawer.DrawContentLine(() =>
            {
                Console.Write(" ");
                if (left.HasColor) { Console.Write(left.Prefix); ColorHelper.WriteColored(left.ColoredText, left.Color!); Console.Write(left.Suffix); }
                else               { Console.Write(left.Prefix); Console.Write(left.ColoredText); Console.Write(left.Suffix); }
                ColorHelper.WriteColored("│", MouseUiHelper.FrameColor(_display));
                Console.Write(" ");
                if (middle.HasColor) { Console.Write(middle.Prefix); ColorHelper.WriteColored(middle.ColoredText, middle.Color!); Console.Write(middle.Suffix); }
                else                 { Console.Write(middle.Prefix); Console.Write(middle.ColoredText); Console.Write(middle.Suffix); }
                ColorHelper.WriteColored("│", MouseUiHelper.FrameColor(_display));
                Console.Write(" ");
                if (right.HasColor) { Console.Write(right.Prefix); ColorHelper.WriteColored(right.ColoredText, right.Color!); Console.Write(right.Suffix); }
                else                { Console.Write(right.Prefix); Console.Write(right.ColoredText); Console.Write(right.Suffix); }
            });
        }

        borderDrawer.DrawSeparatorBottom();

        int dialogReserve = _display.MaxHistoryLines + 7;
        int availableForContent = Math.Max(1, Console.WindowHeight - Console.CursorTop - dialogReserve - 1);
        int statsDrawn = Console.CursorTop - startTop;
        int predictedNeeded = Math.Max(0, _display.MaxHigh - (statsDrawn + availableForContent - 2));
        int totalContent = availableForContent + predictedNeeded;

        switch (_display.CharacterSubTab)
        {
            case CharacterSubTab.Inventory:
                DrawInventorySection(hero, innerWidth, totalContent, borderDrawer);
                break;
            case CharacterSubTab.Abilities:
                DrawAbilitiesSection(hero, innerWidth, totalContent, borderDrawer);
                break;
            case CharacterSubTab.Spells:
                DrawSpellsSection(hero, innerWidth, totalContent, borderDrawer);
                break;
            default:
                DrawEffectsSection(hero, innerWidth, totalContent, borderDrawer);
                break;
        }

        int currentHeight = Console.CursorTop - startTop;
        _display.HeroHigh = currentHeight;
        int needed = _display.MaxHigh - currentHeight;

        if (_display.CharacterSubTab == CharacterSubTab.Spells)
        {
            var (w1, w2, w3, w4) = SpellColumnWidths(innerWidth);
            if (needed > 0)
            {
                for (int i = 0; i < needed; i++)
                    borderDrawer.DrawContentLine4Columns(() => {}, () => {}, () => {}, () => {}, w1, w2, w3, w4);
            }
            borderDrawer.DrawSeparatorWith4Parts('─', w1, w2, w3, w4);
        }
        else
        {
            int col1W = TwoColumnDividerX(innerWidth);
            int col2W = innerWidth - 1 - col1W;
            if (needed > 0)
            {
                for (int i = 0; i < needed; i++)
                    borderDrawer.DrawContentLine2Columns(() => {}, () => {}, col1W, col2W);
            }
            borderDrawer.DrawSeparatorWith2Parts('─', col1W, col2W);
        }
    }

    // Горизонтальные линии над/под колонками — без ┬ ┴ ┼: вертикальный разделитель упирается в сплошную линию.

    // Единая точка правды для позиции разделителя в 2-колоночных секциях (Инвентарь/Способности/
    // Состояния) — используется и там, и в SpellColumnWidths для 2-й линии, чтобы они не могли
    // разъехаться при будущих правках одной без другой.
    private static int TwoColumnDividerX(int innerWidth) => (innerWidth - 1) / 2 ;

    // 2-я вертикальная линия обязана совпадать с общим разделителем Инвентаря/Способностей/Состояний
    // (тот же innerWidth, та же формула), 3-я линия остаётся на месте (не двигается) — разницу
    // между ними забирает 3-я колонка, а не 1-я/4-я, чтобы их позиции тоже не поехали.
    private static (int w1, int w2, int w3, int w4) SpellColumnWidths(int innerWidth)
    {
        int available = innerWidth - 3; // 3 внутренних разделителя между 4 колонками
        int baseW = available / 4;
        int remainder = available - baseW * 4;

        int w1 = baseW + remainder - 2; // 1-я линия на 1 символ левее — div2/div3 при этом не двигаются
        int w4 = baseW + 1;
        int div2Target = TwoColumnDividerX(innerWidth) - 1;
        int div3Target = w1 + baseW + (baseW - 1);  // прежняя (правильная) позиция 3-й линии
        int w2 = div2Target - w1;
        int w3 = div3Target - div2Target + 2;

        return (w1, w2, w3, w4);
    }

    private record struct InvLine(int ItemIdx, bool IsFirst, string Marker, string Name, List<int>? Color, string Rest);

    private void DrawInventorySection(Hero hero, int innerWidth, int preTitleRows, BorderDrawer borderDrawer)
    {
        int col1W = TwoColumnDividerX(innerWidth);
        int col2W = innerWidth - 1 - col1W;
        int selIdx = _display.SelectedInventoryIndex;

        var flat = new List<InvLine>();
        var inventory = hero.Inventory ?? [];
        for (int i = 0; i < inventory.Count; i++)
        {
            var item = inventory[i];
            string marker = item.EquipmentedSlot?.Count > 0 ? "•" : " ";
            string name = item.Quantity is > 1 ? $"{item.Name} ×{item.Quantity}" : item.Name;
            int prefixLen = marker.Length + name.Length + 2;
            // -1: резерв под стрелку "←" при наведении/выборе — без него описание могло впритык
            // заполнить всю ширину колонки (written == colWidth), и стрелке не оставалось места
            // (условие "written < colWidth" в WriteInvLine строгое, silently её не рисовало).
            int firstLineWidth = Math.Max(1, col1W - prefixLen - 1);
            // Строки-продолжения (marker=" ", без имени) не стеснены названием предмета — им
            // доступна вся ширина колонки за вычетом только маркера, а не узкий бюджет первой строки.
            int continuationWidth = Math.Max(1, col1W - 1);
            string desc = string.IsNullOrEmpty(item.Description) ? "" : $"  {item.Description}";
            var descLines = TextWrapper.WrapText(desc, continuationWidth, firstLineWidth);

            flat.Add(new InvLine(i, true, marker, name, item.Color,
                ": " + (descLines.Count > 0 ? descLines[0] : "")));
            for (int d = 1; d < descLines.Count; d++)
                flat.Add(new InvLine(i, false, " ", "", null, descLines[d]));
        }

        if (flat.Count == 0)
            flat.Add(new InvLine(-1, false, "", "", null, L.T("(пусто)")));

        int rows = Math.Max(1, preTitleRows - 4);
        int pageSize = rows * 2;
        int pageCount = Math.Max(1, (int)Math.Ceiling((double)flat.Count / pageSize));
        int page = Math.Min(_display.InventoryScrollOffset, pageCount - 1);
        _display.InventoryScrollOffset = page;

        _canScrollLeft  = page > 0;
        _canScrollRight = page < pageCount - 1;
        _subTabPage = page;
        _subTabPageCount = pageCount;

        _subTabTitleY = Console.CursorTop;
        DrawSubTabTitle(CharacterSubTab.Inventory);
        borderDrawer.DrawSeparatorWith2Parts('─', col1W, col2W);

        int startIdx = page * pageSize;
        var col1 = flat.Skip(startIdx).Take(rows).ToList();
        var col2 = flat.Skip(startIdx + rows).Take(rows).ToList();

        _invContentStartY = Console.CursorTop;
        _invRows = rows;
        // Отступ + рамка(1) до начала col1, затем col1W до разделителя-колонки.
        _invSplitX = col1W + 1 + DisplayConfig.LeftMargin;
        _invCol1RowToItem = Enumerable.Range(0, rows).Select(r => r < col1.Count ? col1[r].ItemIdx : -1).ToArray();
        _invCol2RowToItem = Enumerable.Range(0, rows).Select(r => r < col2.Count ? col2[r].ItemIdx : -1).ToArray();

        var visible = col1.Concat(col2).ToList();
        var firstVisible = flat.Skip(startIdx).FirstOrDefault(e => e.IsFirst);
        _display.InventoryPageFirstItemIdx = firstVisible.IsFirst ? firstVisible.ItemIdx : -1;
        _display.InventoryPageLastItemIdx = visible.Count > 0 ? visible.Max(e => e.ItemIdx) : -1;
        int nextStart = startIdx + pageSize;
        var nextFirst = flat.Skip(nextStart).FirstOrDefault(e => e.IsFirst);
        _display.InventoryPageFirstNextItemIdx = nextFirst.IsFirst ? nextFirst.ItemIdx : -1;
        int prevStart = startIdx - pageSize;
        var prevFirst = page > 0 ? flat.Skip(prevStart).FirstOrDefault(e => e.IsFirst) : default;
        _display.InventoryPageFirstPrevItemIdx = page > 0 && prevFirst.IsFirst ? prevFirst.ItemIdx : -1;

        int pinIdx = _display.PinnedInventoryIndex;
        for (int row = 0; row < rows; row++)
        {
            bool h1 = row < col1.Count;
            bool h2 = row < col2.Count;
            var e1 = h1 ? col1[row] : default;
            var e2 = h2 ? col2[row] : default;
            void DrawRow() => borderDrawer.DrawContentLine2Columns(
                () => { if (h1) WriteInvLine(e1, col1W, e1.ItemIdx == _display.SelectedInventoryIndex,
                    e1.ItemIdx == (_display.PinnedInventoryIndex >= 0 ? _display.PinnedInventoryIndex : _display.SelectedInventoryIndex)); },
                () => { if (h2) WriteInvLine(e2, col2W, e2.ItemIdx == _display.SelectedInventoryIndex,
                    e2.ItemIdx == (_display.PinnedInventoryIndex >= 0 ? _display.PinnedInventoryIndex : _display.SelectedInventoryIndex)); },
                col1W, col2W);
            _selectionRows.Add((Console.CursorTop, [h1 ? e1.ItemIdx : -1, h2 ? e2.ItemIdx : -1], DrawRow));
            DrawRow();
        }

        int fillerRows = Math.Max(0, preTitleRows - 4 - rows);
        for (int i = 0; i < fillerRows; i++)
            borderDrawer.DrawContentLine2Columns(() => {}, () => {}, col1W, col2W);
    }

    private void DrawAbilitiesSection(Hero hero, int innerWidth, int preTitleRows, BorderDrawer borderDrawer)
    {
        int col1W = TwoColumnDividerX(innerWidth);
        int col2W = innerWidth - 1 - col1W;

        var flat = new List<InvLine>();
        var abilities = hero.Abilities ?? [];
        for (int i = 0; i < abilities.Count; i++)
        {
            var ability = abilities[i];
            string marker = " ";
            string name = ability.Name;
            int prefixLen = marker.Length + name.Length + 2;
            // -1: резерв под стрелку "←" при наведении/выборе — без него описание могло впритык
            // заполнить всю ширину колонки (written == colWidth), и стрелке не оставалось места
            // (условие "written < colWidth" в WriteInvLine строгое, silently её не рисовало).
            int firstLineWidth = Math.Max(1, col1W - prefixLen - 1);
            int continuationWidth = Math.Max(1, col1W - 1);
            string desc = string.IsNullOrEmpty(ability.Description) ? "" : $"  {ability.Description}";
            var descLines = TextWrapper.WrapText(desc, continuationWidth, firstLineWidth);

            flat.Add(new InvLine(i, true, marker, name, ability.Color,
                ": " + (descLines.Count > 0 ? descLines[0] : "")));
            for (int d = 1; d < descLines.Count; d++)
                flat.Add(new InvLine(i, false, " ", "", null, descLines[d]));
        }

        if (flat.Count == 0)
            flat.Add(new InvLine(-1, false, "", "", null, L.T("(нет способностей)")));

        int rows = Math.Max(1, preTitleRows - 4);
        int pageSize = rows * 2;
        int pageCount = Math.Max(1, (int)Math.Ceiling((double)flat.Count / pageSize));
        int page = Math.Min(_display.AbilityPageOffset, pageCount - 1);
        _display.AbilityPageOffset = page;

        _canScrollLeft  = page > 0;
        _canScrollRight = page < pageCount - 1;
        _subTabPage = page;
        _subTabPageCount = pageCount;

        _invContentStartY = -1;
        _invRows = 0;

        _subTabTitleY = Console.CursorTop;
        DrawSubTabTitle(CharacterSubTab.Abilities);
        borderDrawer.DrawSeparatorWith2Parts('─', col1W, col2W);

        int startIdx = page * pageSize;
        var col1 = flat.Skip(startIdx).Take(rows).ToList();
        var col2 = flat.Skip(startIdx + rows).Take(rows).ToList();

        for (int row = 0; row < rows; row++)
        {
            bool h1 = row < col1.Count;
            bool h2 = row < col2.Count;
            var e1 = h1 ? col1[row] : default;
            var e2 = h2 ? col2[row] : default;

            borderDrawer.DrawContentLine2Columns(
                () => { if (h1) WriteInvLine(e1, col1W, false); },
                () => { if (h2) WriteInvLine(e2, col2W, false); },
                col1W, col2W
            );
        }

        int fillerRows = Math.Max(0, preTitleRows - 4 - rows);
        for (int i = 0; i < fillerRows; i++)
            borderDrawer.DrawContentLine2Columns(() => {}, () => {}, col1W, col2W);
    }

    private static string EffectDuration(StatusEffect e) => e.ExpiresAtRound.HasValue
        ? L.F("до раунда {0}", e.ExpiresAtRound)
        : e.UntilLongRest == true ? L.T("до долгого отдыха") : L.T("бессрочно");

    private void DrawEffectsSection(Hero hero, int innerWidth, int preTitleRows, BorderDrawer borderDrawer)
    {
        int col1W = TwoColumnDividerX(innerWidth);
        int col2W = innerWidth - 1 - col1W;

        var flat = new List<InvLine>();
        var effects = hero.Effects?.Where(e => e.Deleted != true).ToList() ?? [];
        for (int i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            string marker = " ";
            string name = effect.Name;
            int prefixLen = marker.Length + name.Length + 2;
            // -1: резерв под стрелку "←" при наведении/выборе — без него описание могло впритык
            // заполнить всю ширину колонки (written == colWidth), и стрелке не оставалось места
            // (условие "written < colWidth" в WriteInvLine строгое, silently её не рисовало).
            int firstLineWidth = Math.Max(1, col1W - prefixLen - 1);
            int continuationWidth = Math.Max(1, col1W - 1);
            string duration = EffectDuration(effect);
            string desc = string.IsNullOrEmpty(effect.Description)
                ? $"  {duration}"
                : $"  {duration} — {effect.Description}";
            var descLines = TextWrapper.WrapText(desc, continuationWidth, firstLineWidth);

            flat.Add(new InvLine(i, true, marker, name, effect.Color,
                ": " + (descLines.Count > 0 ? descLines[0] : "")));
            for (int d = 1; d < descLines.Count; d++)
                flat.Add(new InvLine(i, false, " ", "", null, descLines[d]));
        }

        int rows = Math.Max(1, preTitleRows - 4);
        int pageSize = rows * 2;
        int pageCount = Math.Max(1, (int)Math.Ceiling((double)flat.Count / pageSize));
        int page = Math.Min(_display.EffectsPageOffset, pageCount - 1);
        _display.EffectsPageOffset = page;

        _canScrollLeft  = page > 0;
        _canScrollRight = page < pageCount - 1;
        _subTabPage = page;
        _subTabPageCount = pageCount;

        _invContentStartY = -1;
        _invRows = 0;

        _subTabTitleY = Console.CursorTop;
        DrawSubTabTitle(CharacterSubTab.Effects);
        borderDrawer.DrawSeparatorWith2Parts('─', col1W, col2W);

        int startIdx = page * pageSize;
        var col1 = flat.Skip(startIdx).Take(rows).ToList();
        var col2 = flat.Skip(startIdx + rows).Take(rows).ToList();

        for (int row = 0; row < rows; row++)
        {
            bool h1 = row < col1.Count;
            bool h2 = row < col2.Count;
            var e1 = h1 ? col1[row] : default;
            var e2 = h2 ? col2[row] : default;

            borderDrawer.DrawContentLine2Columns(
                () => { if (h1) WriteInvLine(e1, col1W, false); },
                () => { if (h2) WriteInvLine(e2, col2W, false); },
                col1W, col2W
            );
        }

        int fillerRows = Math.Max(0, preTitleRows - 4 - rows);
        for (int i = 0; i < fillerRows; i++)
            borderDrawer.DrawContentLine2Columns(() => {}, () => {}, col1W, col2W);
    }

    // В D&D 5e нет "0-го" уровня в римской нотации — заговор (level 0) обозначается отдельным заголовком.
    private static string LevelToRoman(int level) => level switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V",
        6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX",
        _ => level.ToString(),
    };

    private static string SpellColumnHeader(int level) => level == 0 ? L.T("ЗАГОВОРЫ") : L.F("{0} КРУГ", LevelToRoman(level));

    // Единая точка правды для порядка/фильтра заклинаний — использует и отрисовка (группировка по
    // кругам), и Program.cs (Tab/Shift+Tab), чтобы оба места ссылались на один и тот же индекс.
    public static List<HeroSpell> SortedSpells(Hero hero) =>
        (hero.Spells ?? []).Where(s => s.Deleted != true).OrderBy(s => s.Level).ThenBy(s => s.Name).ToList();

    // 4 колонки, каждая — свой круг заклинаний. Круг, не уместившийся в одну колонку, занимает
    // несколько колонок подряд (тот же заголовок повторяется), а следующие круги сдвигаются вправо —
    // поэтому колонки строятся как плоский список (level, chunk, глобальный индекс первого элемента
    // в SortedSpells) и уже потом режутся по 4 на страницу.
    private void DrawSpellsSection(Hero hero, int innerWidth, int preTitleRows, BorderDrawer borderDrawer)
    {
        var (col1W, col2W, col3W, col4W) = SpellColumnWidths(innerWidth);

        int rows = Math.Max(1, preTitleRows - 4);
        int itemsPerCol = Math.Max(1, rows - 2); // заголовок круга + разделитель под ним

        var sorted = SortedSpells(hero);
        var columns = new List<(string Header, List<HeroSpell> Items, int GlobalStart)>();
        int globalOffset = 0;
        foreach (var group in sorted.GroupBy(s => s.Level).OrderBy(g => g.Key))
        {
            var items = group.ToList();
            for (int i = 0; i < items.Count; i += itemsPerCol)
                columns.Add((SpellColumnHeader(group.Key), items.Skip(i).Take(itemsPerCol).ToList(), globalOffset + i));
            globalOffset += items.Count;
        }
        if (columns.Count == 0)
            columns.Add(("", [], -1));

        int pageCount = Math.Max(1, (int)Math.Ceiling(columns.Count / 4.0));
        int page = Math.Min(_display.SpellsPageOffset, pageCount - 1);
        _display.SpellsPageOffset = page;

        _canScrollLeft  = page > 0;
        _canScrollRight = page < pageCount - 1;
        _subTabPage = page;
        _subTabPageCount = pageCount;

        _invContentStartY = -1;
        _invRows = 0;

        _subTabTitleY = Console.CursorTop;
        DrawSubTabTitle(CharacterSubTab.Spells);
        borderDrawer.DrawSeparatorWith4Parts('─', col1W, col2W, col3W, col4W);

        var pageColumns = columns.Skip(page * 4).Take(4).ToList();
        var visible = pageColumns.ToList();
        while (visible.Count < 4)
            visible.Add(("", [], -1));

        // Границы Tab/Shift+Tab перелистывания страниц — по аналогии с InventoryPageFirst*/Last*.
        _display.SpellsPageFirstItemIdx = pageColumns.Count > 0 && pageColumns[0].Items.Count > 0
            ? pageColumns[0].GlobalStart : -1;
        var lastCol = pageColumns.LastOrDefault(c => c.Items.Count > 0);
        _display.SpellsPageLastItemIdx = lastCol.Items is { Count: > 0 }
            ? lastCol.GlobalStart + lastCol.Items.Count - 1 : -1;
        _display.SpellsPageFirstNextItemIdx = page < pageCount - 1 && columns[(page + 1) * 4].Items.Count > 0
            ? columns[(page + 1) * 4].GlobalStart : -1;
        _display.SpellsPageFirstPrevItemIdx = page > 0 ? columns[(page - 1) * 4].GlobalStart : -1;

        static string CenterHeader(string text, int width)
        {
            if (text.Length >= width) return text[..width];
            int pad = width - text.Length;
            int left = pad / 2;
            return new string(' ', left) + text + new string(' ', pad - left);
        }

        borderDrawer.DrawContentLine4Columns(
            () => ColorHelper.WriteColored(CenterHeader(visible[0].Header, col1W), _display.MainForeground),
            () => ColorHelper.WriteColored(CenterHeader(visible[1].Header, col2W), _display.MainForeground),
            () => ColorHelper.WriteColored(CenterHeader(visible[2].Header, col3W), _display.MainForeground),
            () => ColorHelper.WriteColored(CenterHeader(visible[3].Header, col4W), _display.MainForeground),
            col1W, col2W, col3W, col4W
        );
        borderDrawer.DrawInnerSeparatorWith4Parts(col1W, col2W, col3W, col4W);

        _spellContentStartY = Console.CursorTop;
        _spellRows = itemsPerCol;
        int leftContentX = DisplayConfig.LeftMargin + 1;
        _spellSplitX =
        [
            leftContentX + col1W,
            leftContentX + col1W + 1 + col2W,
            leftContentX + col1W + 1 + col2W + 1 + col3W,
        ];
        _spellColRowToItem = new int[4][];
        for (int c = 0; c < 4; c++)
        {
            var map = new int[itemsPerCol];
            for (int r = 0; r < itemsPerCol; r++)
                map[r] = r < visible[c].Items.Count ? visible[c].GlobalStart + r : -1;
            _spellColRowToItem[c] = map;
        }

        int selIdx = _display.SelectedSpellIndex;
        int pinIdx = _display.PinnedSpellIndex;

        for (int row = 0; row < itemsPerCol; row++)
        {
            int r = row;
            InvLine Line(int c)
            {
                var spell = visible[c].Items[r];
                string marker = spell.Prepared == true ? "•" : " ";
                return new InvLine(visible[c].GlobalStart + r, true, marker, spell.Name, spell.Color, "");
            }
            bool Selected(int c) => r < visible[c].Items.Count && visible[c].GlobalStart + r == _display.SelectedSpellIndex;
            bool Arrow(int c) => r < visible[c].Items.Count &&
                (_display.PinnedSpellIndex >= 0 ? visible[c].GlobalStart + r == _display.PinnedSpellIndex : Selected(c));

            void DrawRow() => borderDrawer.DrawContentLine4Columns(
                () => { if (r < visible[0].Items.Count) WriteInvLine(Line(0), col1W, Selected(0), Arrow(0)); },
                () => { if (r < visible[1].Items.Count) WriteInvLine(Line(1), col2W, Selected(1), Arrow(1)); },
                () => { if (r < visible[2].Items.Count) WriteInvLine(Line(2), col3W, Selected(2), Arrow(2)); },
                () => { if (r < visible[3].Items.Count) WriteInvLine(Line(3), col4W, Selected(3), Arrow(3)); },
                col1W, col2W, col3W, col4W
            );
            _selectionRows.Add((Console.CursorTop, Enumerable.Range(0, 4).Select(c => r < visible[c].Items.Count ? visible[c].GlobalStart + r : -1).ToArray(), DrawRow));
            DrawRow();
        }

        int fillerRows2 = Math.Max(0, preTitleRows - 4 - (itemsPerCol + 2));
        for (int i = 0; i < fillerRows2; i++)
            borderDrawer.DrawContentLine4Columns(() => {}, () => {}, () => {}, () => {}, col1W, col2W, col3W, col4W);
    }

    private void WriteInvLine(InvLine line, int colWidth, bool selected, bool? showArrow = null)
    {
        bool arrow = showArrow ?? selected;
        var selFg = selected ? ColorHelper.Pale(_display.MainForeground, 0.6) : null;
        int written = 0;
        if (selFg != null) ColorHelper.WriteColored(line.Marker, selFg); else Console.Write(line.Marker);
        written += line.Marker.Length;
        var nameColor = selected && line.Color != null ? ColorHelper.Pale(line.Color, 0.6) : line.Color;
        if (nameColor != null && nameColor.Count == 3)
            ColorHelper.WriteColored(line.Name, nameColor);
        else if (selFg != null)
            ColorHelper.WriteColored(line.Name, selFg);
        else
            Console.Write(line.Name);
        written += line.Name.Length;
        if (selFg != null) ColorHelper.WriteColored(line.Rest, selFg); else Console.Write(line.Rest);
        written += line.Rest.Length;
        if (arrow && line.IsFirst && written < colWidth)
        {
            int remaining = colWidth - written - 1;
            if (remaining > 0) Console.Write(new string(' ', remaining));
            ColorHelper.WriteColored("←", selFg ?? ColorHelper.Pale(_display.MainForeground, 0.6));
        }
    }
}
