using NaviDnD.Clients;
using NaviDnD.Data;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

// Анкета новой игры в стиле меню: слева поля (↑↓ — к любому полю, Enter — изменить, ←→ — листать
// цвет и портрет), справа живое превью героя (портрет его цветом, имя, символ как на карте).
// Текстовые поля заполняются подряд: Enter сохраняет поле и сразу открывает следующее (так же их заполняют
// UI-тесты). Раса и класс — листалки из списка правил (Prompts/{RuleSet}/heroOptions.json): смена — портрет под
// них, если есть подходящий. Портреты — готовый набор; под ним «ВЫБРАТЬ ФАЙЛ…» (окно Windows; PNG/JPG/BMP/GIF/SVG
// копируется в Storage/Portraits).
public class NewGameDisplay
{
    private enum Kind { Text, Color, Portrait, Actions, Button, Choice, AiButton, RandomButton }

    private sealed class Field(string label, Kind kind, int maxLength = 0, string? fixedValue = null)
    {
        public string Label = label;
        public Kind Kind = kind;
        public int MaxLength = maxLength;
        public string Value = fixedValue ?? "";
        public bool ReadOnly = fixedValue != null; // фиксированное значение — фокус и ввод его пропускают
        public List<string> Options = [];           // Choice: варианты (←→), выбран Index
        public int Index;
    }



    private sealed record Portrait(string Image, string Title);

    private static (string Name, List<int> Rgb)[] Palette =>
    [
        (L.T("ЯНТАРНЫЙ"), [220, 170, 60]), (L.T("АЛЫЙ"), [210, 70, 60]), (L.T("ИЗУМРУДНЫЙ"), [70, 190, 110]),
        (L.T("ЛАЗУРНЫЙ"), [80, 150, 230]), (L.T("АМЕТИСТОВЫЙ"), [160, 105, 220]), (L.T("БИРЮЗОВЫЙ"), [70, 200, 200]),
        (L.T("МЕДНЫЙ"), [205, 125, 75]), (L.T("СЕРЕБРЯНЫЙ"), [190, 195, 205]), (L.T("ЗОЛОТОЙ"), [240, 210, 90]),
        (L.T("РОЗОВЫЙ"), [230, 120, 170]), (L.T("ЛЕСНОЙ"), [120, 175, 70]), (L.T("ПЕПЕЛЬНЫЙ"), [155, 145, 135]),
    ];

    // Готовый набор портретов (game-icons) — листается после подобранных нейронкой.
    private static Portrait[] Gallery =>
    [
        new("delapouite/wizard-face", L.T("МАГ")), new("delapouite/woman-elf-face", L.T("ЭЛЬФИЙКА")),
        new("delapouite/dwarf-face", L.T("ДВАРФ")), new("delapouite/barbarian", L.T("ВАРВАР")),
        new("delapouite/monk-face", L.T("МОНАХ")), new("delapouite/bandit", L.T("РАЗБОЙНИК")),
        new("cathelineau/witch-face", L.T("ВЕДЬМА")), new("cathelineau/nun-face", L.T("ЖРИЦА")),
        new("darkzaitzev/ninja-head", L.T("НИНДЗЯ")), new("darkzaitzev/hooded-assassin", L.T("УБИЙЦА")),
        new("darkzaitzev/hooded-figure", L.T("СТРАННИК")), new("lorc/cowled", L.T("ОТШЕЛЬНИК")),
        new("lorc/visored-helm", L.T("РЫЦАРЬ")), new("delapouite/closed-barbute", L.T("ВОИН")),
        new("delapouite/black-knight-helm", L.T("ЧЁРНЫЙ РЫЦАРЬ")), new("delapouite/overlord-helm", L.T("ВЛАСТЕЛИН")),
        new("delapouite/viking-helmet", L.T("ВИКИНГ")), new("delapouite/spartan-helmet", L.T("ГОПЛИТ")),
        new("delapouite/samurai-helmet", L.T("САМУРАЙ")), new("kier-heyl/elf-helmet", L.T("ЭЛЬФ-ВОИН")),
        new("kier-heyl/dwarf-helmet", L.T("ДВАРФ-ВОИН")), new("kier-heyl/dwarf-king", L.T("КОРОЛЬ ДВАРФОВ")),
        new("delapouite/orc-head", L.T("ОРК")), new("delapouite/goblin-head", L.T("ГОБЛИН")),
        new("delapouite/kenku-head", L.T("КЕНКУ")), new("lorc/lizardman", L.T("ЯЩЕРОЛЮД")),
        new("faithtoken/dragon-head", L.T("ДРАКОНОРОЖДЁННЫЙ")), new("delapouite/vampire-dracula", L.T("ВАМПИР")),
        new("lorc/werewolf", L.T("ОБОРОТЕНЬ")), new("lorc/cultist", L.T("КУЛЬТИСТ")),
        new("delapouite/plague-doctor-profile", L.T("ЧУМНОЙ ДОКТОР")), new("delapouite/fairy", L.T("ФЕЯ")),
        new("delapouite/archer", L.T("ЛУЧНИК")), new("cathelineau/swordman", L.T("МЕЧНИК")),
    ];

    private static string[] PortraitActions => [L.T("ВЫБРАТЬ ФАЙЛ…")];

    // Расы и классы правил с подходящим портретом (слаг галереи) — Prompts/{RuleSet}/heroOptions.json.
    private sealed record HeroOption(string Name, string? Portrait);
    private readonly Dictionary<string, string?> _optionPortraits = [];
    private readonly List<string> _names = [];   // имена для «РАНДОМ»
    private static readonly Random _rnd = new();

    private void LoadHeroOptions(string ruleSet)
    {
        try
        {
            string path = Path.Combine(AppConfig.ProjectRoot, "Prompts", ruleSet, "heroOptions.json");
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
            foreach (var (key, field) in new[] { ("races", RaceField), ("classes", ClassField), ("levels", LevelField), ("alignments", AlignmentField),
                         ("backgrounds", BackgroundField) })
                foreach (var o in root?[key]?.AsArray() ?? [])
                {
                    // Вариант — строкой («1») или объектом с портретом ({"name","portrait"}).
                    bool plain = o is System.Text.Json.Nodes.JsonValue;
                    string name = plain ? o!.ToString() : (string?)o?["name"] ?? "";
                    if (name.Length == 0) continue;
                    _fields[field].Options.Add(name);
                    if (!plain) _optionPortraits[name] = (string?)o?["portrait"];
                }
            foreach (var n in root?["names"]?.AsArray() ?? [])
                if (n?.ToString() is { Length: > 0 } s) _names.Add(s);
        }
        catch { /* нет списка — листалки пустые */ }
        foreach (int f in ChoiceFields)
        {
            if (_fields[f].Options.Count == 0) _fields[f].Options.Add("—");
            _fields[f].Value = _fields[f].Options[0];
        }
    }

    // «РАНДОМ»: случайные имя (символ — его первые буквы), раса, класс, уровень, мировоззрение, предыстория и цвет;
    // портрет — под них.
    private void Randomize()
    {
        Sound.PlayClick();
        if (_fields[RandomField].ReadOnly) return;
        if (_editing) StopEditing();
        ArchiveNewHero();
        if (_names.Count > 0)
        {
            string name = _names[_rnd.Next(_names.Count)];
            _fields[NameField].Value = name;
            _fields[SymbolField].Value = name.ToUpperInvariant().PadRight(3, name.ToUpperInvariant()[^1])[..3];
        }
        foreach (int f in ChoiceFields)
        {
            var field = _fields[f];
            field.Index = _rnd.Next(field.Options.Count);
            field.Value = field.Options[field.Index];
        }
        _color = _rnd.Next(Palette.Length);
        PortraitFor(ClassField);
    }

    // Выбрать вариант листалки по значению (нет в списке — добавить: раса/класс героя из библиотеки).
    private void SetChoice(int field, string value)
    {
        var f = _fields[field];
        if (value.Length == 0) value = f.Options[0];
        int i = f.Options.IndexOf(value);
        if (i < 0) { f.Options.Add(value); i = f.Options.Count - 1; }
        f.Index = i;
        f.Value = value;
    }

    // Сменили расу/класс — портрет под них из галереи (своя картинка из файла не трогается). Нет подходящего у
    // выбранного — по другому полю.
    private void PortraitFor(int changed)
    {
        if (_portraits[_portrait].Image.StartsWith(SvgToBrailleConverter.PortraitsPrefix)) return;
        int other = changed == RaceField ? ClassField : RaceField;
        foreach (int f in new[] { changed, other })
            if (_optionPortraits.GetValueOrDefault(_fields[f].Value) is { } slug && _portraits.FindIndex(p => p.Image == slug) is int pi and >= 0)
            {
                _portrait = pi;
                return;
            }
    }
    private const int PortraitWidth = 36; // как в игре (SvgToBrailleConverter по умолчанию): 36 × 18

    private readonly WorldState _settings;
    private readonly DisplayConfig _display;
    private readonly ScreenConfig _screen;
    private readonly GameAiClient _aiClient;
    private readonly BorderDrawer _borderDrawer;
    private int _startTop;

    // Шаги новой игры: на какой можно перейти (F1–F4 / клик в заголовке). Анкета живёт между заходами на шаг.
    public Func<int, bool> CanGo = _ => false;
    public NewGameData? Data { get; private set; }
    // Герой создан нейронкой при последнем «ДАЛЕЕ» (Program сохраняет его карточку в библиотеку героев).
    public bool CreatedNow { get; set; }
    // Раса/класс/описание/выбор героя, с которыми герой создан: изменились — создать заново (с предупреждением);
    // нет — имя, символ, цвет и портрет просто переносятся в героя.
    private string? _createdKey;
    private bool _recreateWarned;
    private int? _gotoStep;
    private string? _tabHovered;

    private readonly List<Field> _fields =
    [
        new(L.T("ИМЯ"), Kind.Text, 30), new(L.T("СИМВОЛ"), Kind.Text, 3),
        new(L.T("РАСА"), Kind.Choice), new(L.T("КЛАСС"), Kind.Choice),
        new(L.T("ОПИСАНИЕ"), Kind.Text, 1500),
        new(L.T("ЦВЕТ"), Kind.Color), new(L.T("ПОРТРЕТ"), Kind.Portrait), new("", Kind.Actions),
        new(L.T("ГЕРОЙ"), Kind.Choice),
        new(L.T("ЗАПРОС"), Kind.Text, 300), new(L.T("ПРИДУМАТЬ"), Kind.AiButton),
        new(L.T("УРОВЕНЬ"), Kind.Choice), new(L.T("МИРОВОЗЗРЕНИЕ"), Kind.Choice),
        new(L.T("РАНДОМ"), Kind.RandomButton),
        new(L.T("ПРЕДЫСТОРИЯ"), Kind.Choice),
        new(L.T("ДАЛЕЕ"), Kind.Button),
    ];
    private const int NameField = 0, SymbolField = 1, RaceField = 2, ClassField = 3, DescriptionField = 4,
        ColorField = 5, HeroField = 8, PromptField = 9, AiField = 10, LevelField = 11, AlignmentField = 12, RandomField = 13,
        BackgroundField = 14;
    private const string Setting = "DnD 5e";   // правила — пока одни (в анкете не показываются)

    // Порядок полей на экране (↑↓): ГЕРОЙ — сверху, кнопка — внизу (в списке _fields кнопка — последней).
    // Описание героя — нижним блоком (как описание мира): текст, запрос к нейронке, ПРИДУМАТЬ/ИЗМЕНИТЬ.
    private static readonly int[] Order = [HeroField, 0, 1, 2, 3, LevelField, AlignmentField, BackgroundField, 5, 6, 7, RandomField,
        DescriptionField, PromptField, AiField, 15];

    // ГЕРОЙ (как МИР на шаге «Мир»): «новый герой» (первым), отложенные варианты этого создания игры (Draft — готовы,
    // только читать; в библиотеку — только тот, с которым перешли дальше) и герои библиотеки (Card).
    private sealed record HeroDraft(string[] Values, int Color, string Image);
    private sealed record HeroChoice(HeroLibrary.Card? Card = null, HeroDraft? Draft = null);
    private readonly List<HeroChoice> _heroes = [new()];

    private int _focus;
    private bool _editing;
    private int _color;
    private readonly List<Portrait> _portraits = [.. Gallery];
    private int _portrait;
    private int _action;             // выбранная кнопка в строке под портретом
    private string _message = "";
    private readonly List<(int row, int x, int w)> _actionPos = []; // кнопки под портретом (для мыши)

    // Раскладка
    private int _width, _height, _x0, _bodyTop;
    private int _buttonRow, _buttonX, _buttonWidth, _formLeft;
    private readonly List<(int row, int x)> _swatches = [];      // образцы палитры (для клика)
    private char[,] _chars = new char[0, 0];
    private List<int>?[,] _fg = new List<int>?[0, 0];
    private List<int>?[,] _bg = new List<int>?[0, 0];
    private readonly List<(int row, int field)> _fieldRows = []; // строки тела, занятые полем (для мыши)
    private (int row, int left, int right)? _arrows;              // ◄ ► у поля в фокусе (цвет/портрет)

    // Раскладка по центру: рамка «АНКЕТА» слева, рамка «ГЕРОЙ» справа той же высоты.
    private const int FormWidth = 56, PreviewWidth = 40, Gap = 3, BoxInner = 22;
    private const int DescHeight = 13;                        // блок «ОПИСАНИЕ ГЕРОЯ»
    private const int LabelCol = 3, ValueCol = 18;           // от левого края рамки анкеты
    private const int MultiLines = 3;                         // строк на описание/пожелания

    private List<int> Fg => _display.MainForeground;
    private List<int> Bright => ColorHelper.Pale(_display.MainForeground, 0.75);
    private List<int> Dim => ColorHelper.Darker(_display.MainForeground, 0.5);

    public NewGameDisplay(WorldState settings, DisplayConfig display, ScreenConfig screen, GameAiClient aiClient)
    {
        _aiClient = aiClient;
        _screen = screen;
        _settings = settings;
        _display = display;
        _borderDrawer = new BorderDrawer(settings, display);
        _startTop = Console.CursorTop;
        LoadHeroOptions(aiClient.RuleSet);
        PortraitFor(RaceField);
        _fields[HeroField].Options.Add(L.T("новый герой"));
        foreach (var h in HeroLibrary.All())
        {
            _heroes.Add(new(h));
            _fields[HeroField].Options.Add(h.Name);
        }
    }

    // Выбран вариант ГЕРОЙ: личность из библиотеки — в поля; «новый» — поля с чистого листа.
    // Герой из библиотеки — его раса и класс закреплены (уровень и умения переходят как есть).
    // Введённое для нового героя — хранится, пока листаешь героев библиотеки (вернулся на «новый» — всё как было).
    private static readonly int[] PersonalFields = [NameField, SymbolField, RaceField, ClassField, LevelField, AlignmentField, BackgroundField, DescriptionField];
    private static readonly int[] ChoiceFields = [RaceField, ClassField, LevelField, AlignmentField, BackgroundField];
    private HeroDraft? _newHeroDraft;
    private string _newHeroPrompt = "";

    private HeroDraft Snapshot() => new([.. PersonalFields.Select(f => _fields[f].Value)], _color, _portraits[_portrait].Image);

    private void ApplyHeroChoice(int previous)
    {
        if (previous == 0) { _newHeroDraft = Snapshot(); _newHeroPrompt = _fields[PromptField].Value; }
        _descScroll = 0;
        var h = _heroes[_fields[HeroField].Index];
        bool isNew = h.Card == null && h.Draft == null;
        // Готовый герой (библиотека или отложенный вариант) — как готовый мир: всё только для чтения.
        foreach (int f in PersonalFields.Concat([ColorField, ColorField + 1, ColorField + 2, PromptField, AiField, RandomField])) _fields[f].ReadOnly = !isNew;
        _fields[PromptField].Value = isNew ? _newHeroPrompt : "";
        if (h.Card != null) { ApplyPrefill(h.Card); return; }
        var d = h.Draft ?? _newHeroDraft;
        for (int i = 0; i < PersonalFields.Length; i++)
        {
            string v = d?.Values[i] ?? "";
            if (ChoiceFields.Contains(PersonalFields[i])) SetChoice(PersonalFields[i], v);
            else _fields[PersonalFields[i]].Value = v;
        }
        if (d != null)
        {
            _color = d.Color;
            int pi = _portraits.FindIndex(p => p.Image == d.Image);
            if (pi >= 0) _portrait = pi;
        }
    }

    // Новый герой уже с описанием — перед «РАНДОМ» он становится готовым вариантом в листалке ГЕРОЙ (только на
    // время этого создания игры), а описание нового — с чистого листа (как мир с описанием перед новой картой).
    private void ArchiveNewHero()
    {
        if (_fields[HeroField].Index != 0 || _fields[DescriptionField].Value.Trim().Length == 0) return;
        _heroes.Insert(1, new(Draft: Snapshot()));
        _fields[HeroField].Options.Insert(1, _fields[NameField].Value.Trim() is { Length: > 0 } n ? n : L.T("вариант"));
        _fields[DescriptionField].Value = _fields[PromptField].Value = "";
        _message = L.T("Прежний вариант — в списке ГЕРОЙ (◄ ►), пока создаётся эта игра");
    }

    // Герой из библиотеки (шаг «Герой» мастера новой игры) — поля анкеты заполнены его личностью, их можно поправить.
    private void ApplyPrefill(HeroLibrary.Card h)
    {
        _fields[NameField].Value = h.Name;
        _fields[SymbolField].Value = h.Symbol;
        if (h.Race.Length > 0) SetChoice(RaceField, h.Race);
        if (h.Class.Length > 0) SetChoice(ClassField, h.Class);
        SetChoice(LevelField, System.Text.RegularExpressions.Regex.Match(h.Level, @"\d+").Value);
        SetChoice(AlignmentField, HeroStat(h.HeroJson, "Мировоззрение"));
        SetChoice(BackgroundField, HeroStat(h.HeroJson, "Предыстория"));
        _fields[DescriptionField].Value = h.Description;   // нет описания — пусто (не текст прежнего героя)
        if (h.Color != null && Array.FindIndex(Palette, p => p.Rgb.SequenceEqual(h.Color)) is int ci and >= 0) _color = ci;
        if (h.Image != null)
        {
            int pi = _portraits.FindIndex(p => p.Image == h.Image);
            if (pi < 0) { _portraits.Insert(0, new Portrait(h.Image, h.Name.ToUpperInvariant())); pi = 0; }
            _portrait = pi;
        }
    }

    // Стат героя из его JSON (лист персонажа) по имени; нет — "".
    private static string HeroStat(string? heroJson, string name)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(heroJson ?? "")?["stats"]?.AsArray()
                .FirstOrDefault(s => string.Equals((string?)s?["name"], name, StringComparison.OrdinalIgnoreCase))?["value"]?.ToString() ?? "";
        }
        catch { return ""; }
    }

    // Анкета (шаг 2). Возвращает шаг, на который перейти: 1 — мир, 3/4 — дальше (герой создан, Data), 0 — выход в меню.
    public async Task<int> Show()
    {
        _startTop = Console.CursorTop;
        _titleTabsCache = null;   // заголовок мог измениться (доступные шаги)
        _tabHovered = null;
        _exitToMenu = false;
        _recreateWarned = false;
        Console.CursorVisible = false;
        _borderDrawer.DrawSeparator();
        _bodyTop = _startTop + 1;
        _width = _display.InnerWidth(_display.ViewCols(_settings.Map));
        _height = Math.Max(24, Console.WindowHeight - _bodyTop - 1);
        _x0 = DisplayConfig.LeftMargin + 1;
        Console.SetCursorPosition(0, _bodyTop);
        for (int r = 0; r < _height; r++) _borderDrawer.DrawContentLine(() => { });
        _borderDrawer.DrawBottomBorder();

        _editing = true; // первое поле — сразу на ввод
        Redraw();

        while (true)
        {
            // Нейронка пишет описание — экран ждёт (только Esc), крутится ожидание.
            if (_describeTask != null)
            {
                if (_describeTask.IsCompleted) FinishDescribe();
                else
                {
                    ConsoleMouseReader.DrainMouseEvents();
                    ConsoleMouseReader.SetCursorShape(false);   // ничего не нажимается — обычный указатель
                    if (ConsoleMouseReader.TryReadKey() is { Key: ConsoleKey.Escape }) return 0;
                    _frame++;
                    Redraw();
                    await Task.Delay(100);
                }
                continue;
            }
            if (HandleMouse() is { } mouseResult)
            {
                if (_exitToMenu) { _exitToMenu = false; return 0; }
                if (_gotoStep is int g)
                {
                    _gotoStep = null;
                    if (await Go(g, sound: false) is int to) return to;
                    Redraw();
                    continue;
                }
                if (mouseResult && await Go(3) is int next) return next;
                continue;
            }
            // Клавиши — прямо из буфера консоли (код + символ), как в главном меню: Console.ReadKey
            // вперемешку с чтением мыши разбирал часть нажатий неправильно.
            if (ConsoleMouseReader.TryReadKey() is not { } key) { Thread.Sleep(15); continue; }

            // Символы, пришедшие одной пачкой: перетащенный в окно файл консоль «печатает» путём —
            // такую вставку игнорируем (своя картинка — только через «ВЫБРАТЬ ФАЙЛ…»).
            var keys = new List<ConsoleKeyInfo> { key };
            if (!char.IsControl(key.KeyChar))
            {
                while (ConsoleMouseReader.TryReadKey() is { } next)
                {
                    keys.Add(next);
                    if (char.IsControl(next.KeyChar)) break;
                }
                string burst = new(keys.Where(k => !char.IsControl(k.KeyChar)).Select(k => k.KeyChar).ToArray());
                if (burst.Length > 3 && IsDroppedFile(burst)) keys.RemoveAll(k => !char.IsControl(k.KeyChar));
            }

            foreach (var k in keys)
            {
                if (k.Key == ConsoleKey.Escape) return 0;
                if (k.Key is >= ConsoleKey.F1 and <= ConsoleKey.F4)
                {
                    if (await Go(k.Key - ConsoleKey.F1 + 1) is int to) return to;
                    break;
                }
                if (k.Key != ConsoleKey.Enter) _recreateWarned = false;   // предупреждение — только для Enter подряд
                _message = ""; // подсказка/ошибка — до следующего нажатия
                bool create = _editing ? HandleEditKey(k) : HandleNavKey(k);
                if (create && await Go(3) is int next) return next;
            }
            Redraw();
        }
    }

    // Перейти на шаг: 1 — мир (заполненное остаётся), 3/4 — дальше: герой создаётся (или переносятся
    // изменения внешности). null — остаёмся (не всё заполнено, ждём подтверждения пересоздания).
    private async Task<int?> Go(int step, bool sound = true)
    {
        if (step == 1) { if (sound) Sound.PlayClick(); return 1; }
        if (step == 2) return null;
        if (!CanCreate())
        {
            _message = L.T("Заполни все поля анкеты: ") + string.Join(", ", MissingFields());
            return null;
        }
        string key = EssentialKey();
        if (_createdKey == key && Data != null)
        {
            ApplyLooks();
            return step;
        }
        if (_createdKey != null && !_recreateWarned)
        {
            _recreateWarned = true;
            _message = L.T("Раса, класс, уровень, предыстория или герой изменились — персонаж создастся заново.\nПравки редактора пропадут. Нажми ещё раз — создать.");
            return null;
        }
        _recreateWarned = false;
        Data = await Create();
        _createdKey = key;
        CreatedNow = true;
        return step;
    }

    // Описание (предыстория) — не лист персонажа: его правка героя не пересоздаёт, только переносится.
    // Выбор ГЕРОЙ — по самой записи (индексы сдвигаются, когда в листалку встаёт отложенный вариант).
    private string EssentialKey() => string.Join("|", System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_heroes[_fields[HeroField].Index]),
        _fields[RaceField].Value.Trim(),
        _fields[ClassField].Value.Trim(), _fields[LevelField].Value, _fields[BackgroundField].Value);

    // Уже созданный герой: имя, символ, цвет и портрет из анкеты — в героя как есть, без нейронки.
    private void ApplyLooks()
    {
        var portrait = _portraits[_portrait];
        Data!.Name = _fields[NameField].Value.Trim();
        Data.Symbol = _fields[SymbolField].Value.Trim();
        Data.Description = _fields[DescriptionField].Value.Trim();
        Data.Alignment = _fields[AlignmentField].Value;
        Data.Color = [.. Palette[_color].Rgb];
        Data.Image = string.IsNullOrEmpty(portrait.Image) ? null : portrait.Image;
        if (_settings.Hero is not { } hero) return;
        hero.Name = Data.Name;
        if (hero.Stats?.FirstOrDefault(s => string.Equals(s.Name, "Мировоззрение", StringComparison.OrdinalIgnoreCase)) is { } align)
            align.Value = Data.Alignment;
        if (Data.Symbol.Length > 0) hero.Symbol = Data.Symbol;
        hero.Color = [.. Data.Color];
        if (Data.Image != null) hero.Image = Data.Image;
    }

    private static bool IsDroppedFile(string text)
    {
        string path = text.Trim().Trim('"');
        try { return Path.IsPathRooted(path) && (File.Exists(path) || Directory.Exists(path)); }
        catch { return false; }
    }

    // ── Клавиатура ────────────────────────────────────────────────────────────
    private bool HandleEditKey(ConsoleKeyInfo key)
    {
        var field = _fields[_focus];
        ref string value = ref field.Value;
        int max = field.MaxLength;

        // Описание — поле с курсором: ←→ Home End ↑↓ PgUp/PgDn; ↑ на первой / ↓ на последней строке — к соседнему полю.
        if (_focus == DescriptionField && key.Key != ConsoleKey.Enter)
        {
            if (TextArea.Edit(ref value, ref _descCursor, key, max, _descRect.w, _descRect.rows)) { _descFollow = true; return false; }
            if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            {
                StopEditing();
                MoveFocus(key.Key == ConsoleKey.UpArrow ? -1 : 1);
            }
            return false;
        }

        if (ClipboardText.IsPasteKey(key))
        {
            string paste = ClipboardText.Get();
            value += paste[..Math.Min(paste.Length, Math.Max(0, max - value.Length))];
            return false;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter when _focus == PromptField:
                if (_fields[PromptField].Value.Trim().Length == 0) return false;   // пустой ввод — не отправлять
                StopEditing();
                RunDescribe();
                return false;
            case ConsoleKey.Enter:
                StopEditing();
                // Подряд по текстовым полям — сразу ввод следующего (как раньше в анкете).
                // (после КЛАСС — описание в нижнем блоке, после описания — строка запроса).
                if (field.Kind == Kind.Text && _focus != PromptField)
                {
                    int pos = Array.IndexOf(Order, _focus);
                    int next = Order.Skip(pos + 1).FirstOrDefault(i => _fields[i].Kind == Kind.Text && !_fields[i].ReadOnly, -1);
                    _focus = next >= 0 ? next : NextFocusable(_focus, 1);
                    _editing = _fields[_focus].Kind == Kind.Text;
                    if (_focus == DescriptionField) FocusDescriptionEnd();
                }
                return false;
            case ConsoleKey.UpArrow: StopEditing(); MoveFocus(-1); return false;
            case ConsoleKey.DownArrow: StopEditing(); MoveFocus(1); return false;
            case ConsoleKey.Backspace:
                if (value.Length > 0) value = value[..^1];
                return false;
        }
        if (!char.IsControl(key.KeyChar) && value.Length < max) value += key.KeyChar;
        return false;
    }

    private bool HandleNavKey(ConsoleKeyInfo key)
    {
        var field = _fields[_focus];
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: MoveFocus(-1); return false;
            case ConsoleKey.DownArrow or ConsoleKey.Tab: MoveFocus(1); return false;
            case ConsoleKey.LeftArrow: Step(field, -1); return false;
            case ConsoleKey.RightArrow: Step(field, 1); return false;
            case ConsoleKey.Enter:
                switch (field.Kind)
                {
                    case Kind.Text: _editing = true; return false;
                    case Kind.Actions: RunAction(_action); return false;
                    case Kind.AiButton: RunDescribe(); return false;
                    case Kind.RandomButton: Randomize(); return false;
                    case Kind.Button:
                        if (CanCreate()) return true;
                        _message = L.T("Заполни все поля анкеты: ") + string.Join(", ", MissingFields());
                        return false;
                }
                return false;
        }
        // Печать или вставка на текстовом поле — сразу ввод.
        if (field.Kind == Kind.Text && !field.ReadOnly && (!char.IsControl(key.KeyChar) || ClipboardText.IsPasteKey(key)))
        {
            _editing = true;
            return HandleEditKey(key);
        }
        return false;
    }

    // Текстовое поле в фокусе сразу открыто на ввод — курсор «▌» показывает, куда пойдёт текст.
    private void MoveFocus(int dir)
    {
        _focus = NextFocusable(_focus, dir);
        _editing = _fields[_focus].Kind == Kind.Text;
        if (_focus == DescriptionField) FocusDescriptionEnd();
        Sound.PlayClick();
    }

    // Следующее поле в направлении dir, пропуская фиксированные (СЕТТИНГ); у края — остаётся.
    private int NextFocusable(int from, int dir)
    {
        int pos = Array.IndexOf(Order, from);
        for (int i = pos + dir; i >= 0 && i < Order.Length; i += dir)
            if (!_fields[Order[i]].ReadOnly && (Order[i] != HeroField || _heroes.Count > 1)) return Order[i];
        return from;
    }

    private void Step(Field field, int dir)
    {
        if (field.Kind == Kind.Color) _color = (_color + dir + Palette.Length) % Palette.Length;
        else if (field.Kind == Kind.Portrait) _portrait = (_portrait + dir + _portraits.Count) % _portraits.Count;
        else if (field.Kind == Kind.Actions) _action = (_action + dir + PortraitActions.Length) % PortraitActions.Length;
        else if (field.Kind == Kind.Choice && field.Options.Count > 0)
        {
            int previous = field.Index;
            field.Index = (field.Index + dir + field.Options.Count) % field.Options.Count;
            if (field == _fields[HeroField]) ApplyHeroChoice(previous);
            else
            {
                field.Value = field.Options[field.Index];
                int fi = _fields.IndexOf(field);
                if (fi is RaceField or ClassField) PortraitFor(fi);
            }
        }
        else return;
        Sound.PlayClick();
    }

    private void StopEditing() => _editing = false;

    // Кнопка под портретом — своя картинка через окно Windows.
    private void RunAction(int action)
    {
        Sound.PlayClick();
        PickCustomImage();
    }

    // Создать можно, когда заполнены все текстовые поля анкеты.
    private bool CanCreate() => !MissingFields().Any();

    private IEnumerable<string> MissingFields() =>
        _fields.Where(f => f.Kind == Kind.Text && f != _fields[PromptField] && f.Value.Trim().Length == 0).Select(f => f.Label.ToLowerInvariant());

    // ── Описание героя от нейронки ───────────────────────────────────────────
    private Task<string?>? _describeTask;
    private int _frame;
    private (int row, int x, int w)? _aiButton, _randomButton;
    // Описание: курсор, прокрутка (первая видимая строка), прокрутка за курсором (после правки), место текста.
    private int _descCursor, _descScroll;
    private bool _descFollow = true;
    private (int top, int x, int w, int rows) _descRect = (0, 0, 1, 1);

    private void FocusDescriptionEnd()
    {
        _descCursor = _fields[DescriptionField].Value.Length;
        _descFollow = true;
    }
    private bool _randomHovered;
    private readonly Dictionary<int, (int row, int x, int w)> _choiceArrows = [];   // листалки: поле → ◄ … ►
    private bool _aiHovered;
    private (int field, int dir) _arrowHovered;   // стрелка листалки под мышью

    // ПРИДУМАТЬ/ИЗМЕНИТЬ: описание (внешность, характер, предыстория) по имени/расе/классу и запросу; есть текст —
    // нейронка его дорабатывает.
    private void RunDescribe()
    {
        Sound.PlayClick();
        if (_describeTask != null || _fields[AiField].ReadOnly) return;
        // Без запроса нейронка только придумывает с нуля; переписывать готовое описание «ни о чём» — нет.
        if (_fields[PromptField].Value.Trim().Length == 0 && _fields[DescriptionField].Value.Trim().Length > 0)
        {
            _message = L.T("Напиши в «Ввод», что изменить в описании");
            _focus = PromptField;
            _editing = true;
            return;
        }
        _describeTask = _aiClient.DescribeHero(_fields[NameField].Value.Trim(), _fields[RaceField].Value.Trim(),
            _fields[ClassField].Value.Trim(), _fields[DescriptionField].Value.Trim(), _fields[PromptField].Value.Trim(),
            $"Уровень: {_fields[LevelField].Value}\nМировоззрение: {_fields[AlignmentField].Value}\nПредыстория: {_fields[BackgroundField].Value}");
    }

    private void FinishDescribe()
    {
        if (_describeTask?.IsFaulted == true)
            _message = _describeTask.Exception?.GetBaseException() is AiSetupException setup
                ? setup.Message : L.T("Нейронка не ответила — попробуй ещё раз.");
        else if (_describeTask is { IsCompletedSuccessfully: true, Result: { Length: > 0 } text })
        {
            _fields[DescriptionField].Value = text.Length > 1500 ? text[..1500] : text;
            _fields[PromptField].Value = "";   // запрос выполнен — строка ввода чистая
            _descScroll = 0;
            _descCursor = text.Length;
            _descFollow = false;   // новый текст — с начала
        }
        else _message = L.T("Нейронка не ответила — попробуй ещё раз.");
        _describeTask = null;
        Redraw();
    }

    private bool _buttonHovered;
    private bool _exitToMenu;  // клик по «[Esc]МЕНЮ» в заголовке
    private List<MouseUiHelper.TitleTab> _titleTabs => _titleTabsCache ??= MouseUiHelper.ComputeTitleTabs(_screen.Title);
    private List<MouseUiHelper.TitleTab>? _titleTabsCache;
    private int _actionHovered = -1; // кнопка под портретом под курсором мыши

    // ── Мышь ──────────────────────────────────────────────────────────────────
    // null — ничего; false — что-то изменилось (перерисовано); true — нажата «СОЗДАТЬ».
    private bool? HandleMouse()
    {
        var (move, click, _) = ConsoleMouseReader.DrainMouseEvents();
        // Колесо над описанием — прокрутка текста.
        if (ConsoleMouseReader.TakeWheel(out var wp) is int notches and not 0)
        {
            int wr = wp.y - _bodyTop - _descRect.top, wc = wp.x - _x0;
            if (wr >= 0 && wr < _descRect.rows && wc >= _formLeft && wc < _formLeft + FormWidth + Gap + PreviewWidth)
            {
                _descScroll -= notches;
                _descFollow = false;
                Redraw();
                return false;
            }
        }
        if (move is { } m)
        {
            int hoverField = FieldAt(m.x, m.y);
            bool overButton = hoverField == _fields.Count - 1;
            int overAction = ActionAt(m.x, m.y);
            bool clickable = hoverField >= 0 && !_fields[hoverField].ReadOnly && _fields[hoverField].Kind != Kind.Actions;
            string? overTab = MouseUiHelper.GetHoveredTabKey(m.x, m.y, _titleTabs);
            ConsoleMouseReader.SetCursorShape(clickable || overTab != null || overAction >= 0 || IsArrow(m.x, m.y) != 0 || SwatchAt(m.x, m.y) >= 0);
            if (overTab != _tabHovered)
            {
                if (_tabHovered != null) MouseUiHelper.SetTabHighlight(_screen.Title, _titleTabs, _tabHovered, false, _display);
                if (overTab != null) MouseUiHelper.SetTabHighlight(_screen.Title, _titleTabs, overTab, true, _display);
                _tabHovered = overTab;
            }
            bool overAi = OnRect(_aiButton, m.x, m.y);
            bool overRandom = OnRect(_randomButton, m.x, m.y);
            if (overRandom) ConsoleMouseReader.SetCursorShape(true);
            if (overRandom != _randomHovered) { _randomHovered = overRandom; Redraw(); }
            var overArrow = ChoiceArrowAt(m.x, m.y);
            if (overAi || overArrow.dir != 0) ConsoleMouseReader.SetCursorShape(true);
            if (overButton != _buttonHovered || overAction != _actionHovered || overAi != _aiHovered || overArrow != _arrowHovered)
            {
                _buttonHovered = overButton;
                _actionHovered = overAction;
                _aiHovered = overAi;
                _arrowHovered = overArrow;
                Redraw();
            }
        }
        if (click is not { } c) return null;

        int swatch = _fields[ColorField].ReadOnly ? -1 : SwatchAt(c.x, c.y);
        if (swatch >= 0)
        {
            if (_editing) StopEditing();
            _focus = _fields.FindIndex(f => f.Kind == Kind.Color);
            _color = swatch;
            Sound.PlayClick();
            Redraw();
            return false;
        }

        // «[Esc]МЕНЮ» в заголовке — как Esc, «[Fn]шаг» — как F1–F4; остальная шапка — перетаскивание окна.
        if (MouseUiHelper.GetHoveredTabKey(c.x, c.y, _titleTabs) is { } tabKey)
        {
            Sound.PlayClick();
            ConsoleMouseReader.SetCursorShape(false);
            if (tabKey == "Esc") _exitToMenu = true;
            else _gotoStep = tabKey[1] - '0';
            return false;
        }
        if (c.y < _bodyTop) { ConsoleMouseReader.StartWindowDrag(); return null; }
        int actionClicked = _fields[ColorField + 2].ReadOnly ? -1 : ActionAt(c.x, c.y);
        if (actionClicked >= 0)
        {
            if (_editing) StopEditing();
            _focus = _fields.FindIndex(f => f.Kind == Kind.Actions);
            _action = actionClicked;
            RunAction(actionClicked);
            Redraw();
            return false;
        }
        int arrow = IsArrow(c.x, c.y);
        if (arrow != 0)
        {
            // Стрелки портрета — листают портрет, откуда бы ни был фокус.
            if (_editing) StopEditing();
            _focus = _fields.FindIndex(f => f.Kind == Kind.Portrait);
            Step(_fields[_focus], arrow);
            Redraw();
            return false;
        }

        if (OnRect(_randomButton, c.x, c.y))
        {
            _focus = RandomField;
            Randomize();
            Redraw();
            return false;
        }
        if (OnRect(_aiButton, c.x, c.y))
        {
            if (_editing) StopEditing();
            _focus = AiField;
            RunDescribe();
            Redraw();
            return false;
        }
        if (ChoiceArrowAt(c.x, c.y) is { dir: not 0 } arrowHit)
        {
            if (_editing) StopEditing();
            _focus = arrowHit.field;
            Step(_fields[arrowHit.field], arrowHit.dir);
            Redraw();
            return false;
        }

        int field = FieldAt(c.x, c.y);
        if (field < 0 || _fields[field].ReadOnly || _fields[field].Kind == Kind.Actions) return null;
        if (_fields[field].Kind == Kind.Choice)
        {
            // Выбор (мир/герой): клик — следующий вариант.
            if (_editing) StopEditing();
            _focus = field;
            Step(_fields[field], 1);
            Redraw();
            return false;
        }
        if (_fields[field].Kind == Kind.Button)
        {
            if (_editing) StopEditing();
            Sound.PlayClick();
            if (CanCreate()) return true;
            _focus = field;
            _message = L.T("Заполни все поля анкеты: ") + string.Join(", ", MissingFields());
            Redraw();
            return false;
        }
        if (_editing) StopEditing();
        _focus = field;
        Sound.PlayClick();
        _editing = _fields[field].Kind == Kind.Text;
        // Клик по тексту описания — курсор в это место.
        if (field == DescriptionField)
        {
            _descCursor = TextArea.CursorAt(_fields[DescriptionField].Value, _descScroll, _descRect.w,
                c.y - _bodyTop - _descRect.top, c.x - _x0 - _descRect.x);
            _descFollow = true;
        }
        Redraw();
        return false;
    }

    // Поле под мышью — только внутри рамки анкеты (строки превью справа совпадают со строками полей),
    // кнопка «СОЗДАТЬ» — только по её тексту.
    private int FieldAt(short x, short y)
    {
        foreach (var (row, field) in _fieldRows)
        {
            if (y != _bodyTop + row) continue;
            bool inside = _fields[field].Kind == Kind.Button
                ? IsOnButton(x)
                : field is DescriptionField or PromptField   // нижний блок — во всю ширину
                    ? x > _x0 + _formLeft && x < _x0 + _formLeft + FormWidth + Gap + PreviewWidth - 1
                    : x > _x0 + _formLeft && x < _x0 + _formLeft + FormWidth - 1;
            return inside ? field : -1;
        }
        return -1;
    }

    private int SwatchAt(short x, short y)
    {
        for (int i = 0; i < _swatches.Count; i++)
        {
            var (row, sx) = _swatches[i];
            if (y == _bodyTop + row && x >= _x0 + sx && x < _x0 + sx + 2) return i;
        }
        return -1;
    }

    private int ActionAt(short x, short y)
    {
        for (int i = 0; i < _actionPos.Count; i++)
        {
            var (row, ax, w) = _actionPos[i];
            if (y == _bodyTop + row && x >= _x0 + ax && x < _x0 + ax + w) return i;
        }
        return -1;
    }

    private bool OnRect((int row, int x, int w)? r, short x, short y) =>
        r is { } b && y == _bodyTop + b.row && x >= _x0 + b.x && x < _x0 + b.x + b.w;

    private (int field, int dir) ChoiceArrowAt(short x, short y)
    {
        foreach (var (field, a) in _choiceArrows)
        {
            if (y != _bodyTop + a.row) continue;
            if (x == _x0 + a.x) return (field, -1);
            if (x == _x0 + a.x + a.w) return (field, 1);
        }
        return (0, 0);
    }

    private bool IsOnButton(short x) => x >= _x0 + _buttonX && x < _x0 + _buttonX + _buttonWidth;

    private int IsArrow(short x, short y)
    {
        if (_arrows is not { } a || y != _bodyTop + a.row) return 0;
        if (x == _x0 + a.left) return -1;
        if (x == _x0 + a.right) return 1;
        return 0;
    }

    // ── Портреты ──────────────────────────────────────────────────────────────
    // «ВЫБРАТЬ ФАЙЛ…»: окно Windows, файл копируется в Storage/Portraits и встаёт первым в галерее.
    private void PickCustomImage()
    {
        string? path = FileDialog.OpenFile(L.T("Портрет героя"),
            L.T("Картинки (PNG, JPG, BMP, GIF, SVG)") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.svg");
        if (path == null) return;
        if (!SvgToBrailleConverter.IsSupportedImageFile(path))
        {
            _message = L.T("Формат не поддерживается (PNG, JPG, BMP, GIF, SVG)");
            return;
        }
        try
        {
            Directory.CreateDirectory(SvgToBrailleConverter.PortraitsDir);
            string fileName = Path.GetFileName(path);
            string target = Path.Combine(SvgToBrailleConverter.PortraitsDir, fileName);
            for (int n = 2; File.Exists(target) && !SameFile(target, path); n++)
                target = Path.Combine(SvgToBrailleConverter.PortraitsDir, $"{Path.GetFileNameWithoutExtension(path)}_{n}{Path.GetExtension(path)}");
            if (!File.Exists(target)) File.Copy(path, target);

            string image = SvgToBrailleConverter.PortraitsPrefix + Path.GetFileName(target);
            if (SvgToBrailleConverter.Convert(image, "", PortraitWidth) == null)
            {
                _message = L.T("Не удалось прочитать картинку");
                return;
            }
            _portraits.RemoveAll(p => p.Image == image);
            _portraits.Insert(0, new Portrait(image, L.T("СВОЙ: ") + Path.GetFileNameWithoutExtension(target).ToUpperInvariant()));
            _portrait = 0;
            _message = "";
        }
        catch (Exception ex)
        {
            _message = L.T("Не удалось загрузить картинку: ") + ex.Message;
        }
    }

    private static bool SameFile(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // ── Создание ──────────────────────────────────────────────────────────────
    private async Task<NewGameData> Create()
    {
        var portrait = _portraits[_portrait];
        var result = new NewGameData
        {
            Name = _fields[NameField].Value.Trim(),
            Symbol = _fields[SymbolField].Value.Trim(),
            Race = _fields[RaceField].Value.Trim(),
            Class = _fields[ClassField].Value.Trim(),
            Level = _fields[LevelField].Value,
            Alignment = _fields[AlignmentField].Value,
            Background = _fields[BackgroundField].Value,
            Description = _fields[DescriptionField].Value.Trim(),
            Setting = Setting,
            SettingWish = "",   // пожелания к игре — шаг «Приключение» после редактора
            Color = [.. Palette[_color].Rgb],
            Image = string.IsNullOrEmpty(portrait.Image) ? null : portrait.Image,
        };

        // Ожидание — по центру, на месте подсказки под кнопкой (а не общим спиннером у края экрана).
        // Герой из библиотеки — не создаётся заново: готовится к новому приключению (снаряжение, состояние).
        _editing = false;
        var reused = _heroes[_fields[HeroField].Index].Card;
        var aiTask = reused?.HeroJson is { } heroJson ? _aiClient.ReuseHero(result, heroJson) : _aiClient.CreateNewGame(result);
        string waitText = reused?.HeroJson != null ? L.T("Готовим героя к новому приключению...") : L.T("Создаём персонажа...");
        for (int frame = 0; !aiTask.IsCompleted; frame++)
        {
            // Пока создаётся персонаж — клики не принимаются, указатель обычный (не «рука»).
            ConsoleMouseReader.DrainMouseEvents();
            ConsoleMouseReader.SetCursorShape(false);
            _message = $"{Spinner.Frames[frame % Spinner.Frames.Length]} {waitText}";
            Redraw();
            await Task.Delay(100);
        }
        _message = "";
        await aiTask;
        return result;
    }

    // ── Отрисовка ─────────────────────────────────────────────────────────────
    // Всё по центру экрана: заголовок, под ним рамки «АНКЕТА» и «ГЕРОЙ» одной высоты, ниже кнопка и
    // подсказка. Поля — плотно, группами; описание и пожелания — в несколько строк с переносом.
    private void Redraw()
    {
        _chars = new char[_height, _width];
        _fg = new List<int>?[_height, _width];
        _bg = new List<int>?[_height, _width];
        for (int r = 0; r < _height; r++) for (int c = 0; c < _width; c++) _chars[r, c] = ' ';
        _fieldRows.Clear();
        _arrows = null;

        int total = FormWidth + Gap + PreviewWidth;
        int left = Math.Max(1, (_width - total) / 2);
        _formLeft = left;
        int boxHeight = BoxInner + 2;
        int blockHeight = boxHeight + 1 + DescHeight + 1 + 1 + 3;   // рамки, описание, кнопка, подсказка (до 2 строк)
        int top = Math.Max(0, (_height - blockHeight) / 2);
        int boxTop = top; // отдельного заголовка нет — «НОВАЯ ИГРА» уже в строке заголовка экрана

        DrawBox(boxTop, left, FormWidth, boxHeight, L.T("АНКЕТА"));
        DrawBox(boxTop, left + FormWidth + Gap, PreviewWidth, boxHeight, L.T("ГЕРОЙ"));

        // Поля анкеты: группы через пустую строку, от верха рамки (не центрируются — иначе вся анкета
        // съезжала бы, когда описание/пожелания прибавляют строку).
        _swatches.Clear();
        int y = boxTop + 2;
        int valueWidth = FormWidth - ValueCol - 3;
        _actionPos.Clear();
        foreach (int group in new[] { HeroField, -1, NameField, SymbolField, -1, RaceField, ClassField, LevelField, AlignmentField, BackgroundField, -1,
                     ColorField, -1, ColorField + 1, -1, ColorField + 2 })
        {
            if (group < 0) { y++; continue; }
            y += DrawField(group, y, left, valueWidth);
        }

        // Кнопка: неактивна (тёмная, без стрелок), пока не заполнены все поля; активная — со стрелками
        // при выборе клавиатурой или наведении мышью. Ширина одна и та же — текст не прыгает.
        // «РАНДОМ» — плашкой внизу рамки анкеты (как кнопки карты на шаге «Мир»); у готового героя — нет.
        _randomButton = null;
        if (!_fields[RandomField].ReadOnly)
        {
            int ry = boxTop + boxHeight - 3, rx = left + LabelCol;
            _randomButton = (ry, rx, Chip(ry, rx, L.T("РАНДОМ"), _focus == RandomField || _randomHovered, false));
        }

        DrawDescription(boxTop + boxHeight + 1, left, total);

        _buttonRow = boxTop + boxHeight + 1 + DescHeight + 1;
        var button = _fields[^1];
        bool enabled = CanCreate();
        bool highlighted = enabled && (_focus == _fields.Count - 1 || _buttonHovered);
        _buttonX = left + Math.Max(0, (total - button.Label.Length - 4) / 2);
        _buttonWidth = Chip(_buttonRow, _buttonX, button.Label, highlighted, !enabled);
        _fieldRows.Add((_buttonRow, _fields.Count - 1));
        // Подсказка или сообщение — до двух строк («\n» — перенос).
        var hint = (_message.Length > 0 ? _message : L.T("[↑↓]ПОЛЕ   [Enter]ИЗМЕНИТЬ   [←→]ЛИСТАТЬ   [F1]МИР   [Esc]МЕНЮ")).Split('\n');
        for (int i = 0; i < hint.Length && i < 2; i++)
            CenterIn(_buttonRow + 2 + i, left, total, Fit(hint[i], total), _recreateWarned ? Bright : Dim);

        DrawPreview(boxTop + 1, left + FormWidth + Gap);

        Console.CursorVisible = false;
        for (int r = 0; r < _height; r++) WriteRow(r);
    }

    // Блок «ОПИСАНИЕ ГЕРОЯ» (как описание мира): сверху текст — свой или от нейронки; под разделителем — «Ввод:»
    // запроса и ПРИДУМАТЬ/ИЗМЕНИТЬ. Готовый герой — только текст на весь блок.
    private void DrawDescription(int top, int left, int total)
    {
        var desc = _fields[DescriptionField];
        string name = _fields[NameField].Value.Trim();
        DrawBox(top, left, total, DescHeight, name.Length > 0 ? L.T("ОПИСАНИЕ ГЕРОЯ") + " · " + name.ToUpperInvariant() : L.T("ОПИСАНИЕ ГЕРОЯ"));
        int lx = left + 3, width = total - 6;
        bool locked = desc.ReadOnly;
        int rows = locked ? DescHeight - 2 : DescHeight - 4;
        bool editing = _focus == DescriptionField && _editing;
        _descRect = (top + 1, lx, width, rows);
        // Текст: перенос по словам, курсор (подсвеченная клетка), прокрутка — колесом, ↑↓, PgUp/PgDn; полоса справа.
        if (desc.Value.Length == 0 && !editing)
            Put(top + 1, lx, Fit(L.T("внешность, характер, предыстория — своим текстом или «ПРИДУМАТЬ» по запросу"), width), Dim);
        else
        {
            var view = TextArea.View(desc.Value, _descCursor, ref _descScroll, width, rows, editing && _descFollow,
                out int curRow, out int curCol, out int totalLines);
            for (int i = 0; i < view.Count; i++) Put(top + 1 + i, lx, view[i], editing ? Bright : Fg);
            if (editing && curRow >= 0)
            {
                string line = view[curRow];
                Put(top + 1 + curRow, lx + curCol, curCol < line.Length ? line[curCol].ToString() : " ", _display.MainBackground, bg: Bright);
            }
            if (TextArea.Bar(rows, totalLines, _descScroll) is { } bar)
                for (int i = 0; i < rows; i++) Put(top + 1 + i, left + total - 2, bar[i] ? "┃" : "│", bar[i] ? Bright : Dim);
        }
        if (!locked)
            for (int i = 0; i < rows; i++) _fieldRows.Add((top + 1 + i, DescriptionField));
        _aiButton = null;
        if (locked) return;

        int sep = top + DescHeight - 3, iy = sep + 1;
        Put(sep, left, "├" + new string('─', total - 2) + "┤", MouseUiHelper.FrameColor(_display));
        if (_describeTask != null)
        {
            // Нейронка думает — строка целиком под ожидание, без «Ввод:» и кнопки.
            Put(iy, lx, Fit(Spinner.Frames[_frame % Spinner.Frames.Length] + " " + L.T("Нейронка пишет описание героя…"), width), Bright);
            return;
        }
        string goText = desc.Value.Trim().Length > 0 ? L.T("ИЗМЕНИТЬ") : L.T("ПРИДУМАТЬ");
        int gx = left + total - 3 - (goText.Length + 4);
        bool promptFocus = _focus == PromptField;
        Put(iy, lx, L.T("Ввод: "), promptFocus ? Bright : Fg);
        int px = lx + 6, promptW = gx - 2 - px;
        var prompt = _fields[PromptField].Value;
        bool empty = prompt.Length == 0 && !(promptFocus && _editing);
        string text = empty ? L.T("что придумать или как изменить описание (необязательно)") : TailFit(prompt + (promptFocus && _editing ? "▌" : ""), promptW);
        Put(iy, px, Fit(text, promptW), empty ? Dim : promptFocus ? Bright : Fg);
        _fieldRows.Add((iy, PromptField));
        int w = Chip(iy, gx, goText, _focus == AiField || _aiHovered, false);
        _aiButton = (iy, gx, w);
    }

    // Одно поле анкеты; возвращает, сколько строк заняло.
    private int DrawField(int index, int y, int left, int valueWidth)
    {
        var f = _fields[index];
        bool focused = index == _focus;
        int lx = left + LabelCol, vx = left + ValueCol;
        // У строки кнопок подписи нет — её фокус показывают стрелки у самой кнопки.
        if (f.Kind != Kind.Actions)
            Put(y, lx - 2, focused ? "→ " + f.Label : "  " + f.Label, focused ? Bright : Fg);

        int lines = 1;
        switch (f.Kind)
        {
            case Kind.Text:
            {
                bool editing = focused && _editing;
                bool multi = f.MaxLength > 100;
                if (f.Value.Length == 0 && !editing) { Put(y, vx, "—", Dim); break; }
                // Перенос по символам на ширину поля (не по словам): при вводе текст не перескакивает,
                // а переходит на новую строку, только когда текущая заполнилась.
                string text = f.Value + (editing ? "▌" : "");
                var wrapped = multi ? Chunk(text, valueWidth) : [text];
                if (wrapped.Count == 0) wrapped.Add(editing ? "▌" : "");
                int max = multi ? MultiLines : 1;
                // При вводе — последние строки (видно, что печатается), иначе — первые с «…».
                var shown = editing ? wrapped.Skip(Math.Max(0, wrapped.Count - max)).ToList() : wrapped.Take(max).ToList();
                if (!editing && wrapped.Count > max) shown[^1] = Fit(shown[^1] + "…", valueWidth);
                for (int i = 0; i < shown.Count; i++) Put(y + i, vx, Fit(shown[i], valueWidth), editing ? Bright : Fg);
                // Высота — по тексту: новая строка появляется, только когда текущая заполнилась (до 3).
                lines = shown.Count;
                break;
            }

            case Kind.Color:
            {
                // Палитра образцами в ряд, под выбранным — «▲» и название: ничего не прыгает,
                // ←→ двигают стрелку, образец можно выбрать кликом.
                // Образцы — фон двух символов через промежуток; выбранный не отмечается — его видно в
                // превью героя (портрет и имя его цветом).
                for (int i = 0; i < Palette.Length; i++)
                {
                    int sx = vx + i * 3;
                    Put(y, sx, "  ", Fg, bg: Palette[i].Rgb);
                    if (!f.ReadOnly) _swatches.Add((y, sx));
                }
                break;
            }

            case Kind.Actions:
            {
                if (f.ReadOnly) break;   // готовый герой — портрет не меняется
                // Кнопки под портретом — как пункты меню: выбранная «→ … ←» — под мышью, а без мыши —
                // выбранная стрелками (при фокусе на строке).
                int ax = vx - 2;
                for (int i = 0; i < PortraitActions.Length; i++)
                {
                    bool sel = _actionHovered >= 0 ? i == _actionHovered : focused && i == _action;
                    int w = Chip(y, ax, PortraitActions[i], sel, false);
                    _actionPos.Add((y, ax, w));
                    ax += w + 2;
                }
                break;
            }

            case Kind.Choice:
            {
                // Поле фиксированной ширины: стрелки не прыгают при листании, значение по центру.
                int inner = valueWidth - 4, textW = inner;
                // ГЕРОЙ — «какой из скольких» приглушённо у правой стрелки (как МИР на шаге «Мир»).
                if (index == HeroField)
                {
                    string counter = $"{f.Index + 1}/{f.Options.Count}";
                    Put(y, vx + 2 + inner - counter.Length, counter, Dim);
                    textW -= counter.Length + 1;
                }
                string text = f.Options.Count == 0 ? "—" : Fit(f.Options[f.Index], textW);
                Put(y, vx + 2 + (textW - text.Length) / 2, text, focused ? Bright : Fg);
                _choiceArrows.Remove(index);
                if (f.ReadOnly) break;   // готовый герой — без стрелок
                // Стрелки всегда активны (листать мышью можно без фокуса), под мышью — ярче.
                Put(y, vx, "◄", focused || _arrowHovered == (index, -1) ? Bright : Fg);
                Put(y, vx + 2 + inner + 1, "►", focused || _arrowHovered == (index, 1) ? Bright : Fg);
                _choiceArrows[index] = (y, vx, inner + 3);
                break;
            }

            case Kind.Portrait:
            {
                // Название — в поле фиксированной ширины: «◄» и «►» стоят на месте при листании.
                var p = _portraits[_portrait];
                const int titleWidth = 16; // + счётчик «подбираем… 1/35» справа — не перекрываются
                string title = Fit(p.Title, titleWidth);
                int rx = vx + 2 + titleWidth + 1;
                // Готовый герой — портрет его, без стрелок и счётчика.
                if (f.ReadOnly)
                {
                    Put(y, vx + 2 + (titleWidth - title.Length) / 2, title, Fg);
                    break;
                }
                // Стрелки активны всегда (листать мышью можно и без фокуса на поле).
                Put(y, vx, "◄", focused ? Bright : Fg);
                Put(y, vx + 2 + (titleWidth - title.Length) / 2, title, focused ? Bright : Fg);
                Put(y, rx, "►", focused ? Bright : Fg);
                _arrows = (y, vx, rx);
                PutRight(y, left + FormWidth - 3, $"{_portrait + 1}/{_portraits.Count}", Dim);
                break;
            }
        }
        for (int i = 0; i < lines; i++) _fieldRows.Add((y + i, index));
        return lines;
    }

    // Рамка «ГЕРОЙ»: портрет цветом героя, под ним имя, символ как на карте и откуда портрет.
    private void DrawPreview(int y, int left)
    {
        var color = Palette[_color].Rgb;
        var p = _portraits[_portrait];
        int inner = PreviewWidth - 2;
        int artX = left + 1 + (inner - PortraitWidth) / 2;
        var art = string.IsNullOrEmpty(p.Image) ? null : SvgToBrailleConverter.Convert(p.Image, "", PortraitWidth);
        int artRows = PortraitWidth / 2;
        if (art != null)
            for (int i = 0; i < art.Length && i < artRows; i++) Put(y + 1 + i, artX, art[i], color, skipBlank: true);
        else
            CenterIn(y + artRows / 2, left, PreviewWidth, string.IsNullOrEmpty(p.Image) ? L.T("Enter — выбрать файл") : L.T("нет картинки"), Dim);

        int ty = y + 1 + artRows;
        string name = _fields[NameField].Value.Trim();
        CenterIn(ty, left, PreviewWidth, name.Length == 0 ? L.T("ИМЯ ГЕРОЯ") : Fit(name, inner - 2), name.Length == 0 ? Dim : color);

        // Символ — как клетка героя на карте (MapObjectsProvider: фон комнаты, бледнее, с зеленцой).
        string symbol = _fields[SymbolField].Value.Trim();
        string onMap = L.T("НА КАРТЕ  ???");
        int sx = CenterIn(ty + 1, left, PreviewWidth, onMap, Dim);
        var cellBg = ColorHelper.MixWith(ColorHelper.Pale([55, 55, 65]), [0, 200, 80], 0.18);
        Put(ty + 1, sx + onMap.Length - 3, symbol.Length == 0 ? "???" : symbol.PadRight(3)[..3], ColorHelper.Saturate(color), bg: cellBg);
    }

    // Рамка в цвете рамок игры, заголовок — в разрыве верхней линии: «╭─ АНКЕТА ──…──╮».
    private void DrawBox(int y, int x, int w, int h, string title)
    {
        var line = MouseUiHelper.FrameColor(_display);   // рамки блоков — приглушённо, как на шаге «Мир»
        Put(y, x, "╭─ ", line);
        Put(y, x + 3, title, Bright);
        Put(y, x + 3 + title.Length, " " + new string('─', Math.Max(0, w - title.Length - 5)) + "╮", line);
        for (int r = 1; r < h - 1; r++)
        {
            Put(y + r, x, "│", line);
            Put(y + r, x + w - 1, "│", line);
        }
        Put(y + h - 1, x, "╰" + new string('─', w - 2) + "╯", line);
    }

    // Кнопка-плашка «  ТЕКСТ  » на заливке (как на шаге «Мир»): выбрана — заливка ярче, недоступна — приглушена.
    // Возвращает ширину.
    private int Chip(int y, int x, string text, bool on, bool disabled)
    {
        string t = $"  {text}  ";
        var bg = ColorHelper.MixWith(_display.MainBackground, _display.MainForeground, on ? 0.42 : 0.16);
        Put(y, x, t, disabled ? Dim : on ? ColorHelper.Pale(_display.MainForeground, 0.85) : Fg, bg: bg);
        return t.Length;
    }

    private int CenterIn(int row, int left, int width, string text, List<int> color)
    {
        int x = left + Math.Max(0, (width - text.Length) / 2);
        Put(row, x, text, color);
        return x;
    }

    private void PutRight(int row, int right, string text, List<int> color) => Put(row, right - text.Length + 1, text, color);

    private static List<string> Chunk(string text, int width)
    {
        var lines = new List<string>();
        for (int i = 0; i < text.Length; i += width) lines.Add(text.Substring(i, Math.Min(width, text.Length - i)));
        return lines;
    }

    private static string Fit(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";

    private static string TailFit(string text, int width) =>
        text.Length <= width ? text : "…" + text[^(width - 1)..];

    private void Put(int row, int x, string text, List<int> color, bool skipBlank = false, List<int>? bg = null)
    {
        if (row < 0 || row >= _height) return;
        for (int i = 0; i < text.Length && x + i < _width; i++)
        {
            if (x + i < 0) continue;
            if (skipBlank && text[i] == '⠀') continue;
            _chars[row, x + i] = text[i];
            _fg[row, x + i] = color;
            _bg[row, x + i] = bg;
        }
    }

    private void WriteRow(int row)
    {
        Console.SetCursorPosition(_x0, _bodyTop + row);
        int c = 0;
        while (c < _width)
        {
            var fg = _fg[row, c];
            var bg = _bg[row, c];
            int start = c;
            while (c < _width && ReferenceEquals(_fg[row, c], fg) && ReferenceEquals(_bg[row, c], bg)) c++;
            var run = new string(Enumerable.Range(start, c - start).Select(i => _chars[row, i]).ToArray());
            ColorHelper.WriteColored(run, fgColor: fg ?? _display.MainForeground, bgColor: bg ?? _display.MainBackground);
        }
    }
}
