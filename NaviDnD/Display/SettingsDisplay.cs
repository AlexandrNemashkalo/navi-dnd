using NaviDnD.Data;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

// Экран настроек в стиле анкеты новой игры: по центру рамка «НАСТРОЙКИ» с группами (звук, ИИ,
// отладка) и рамка «ПОДСКАЗКА» с описанием пункта под курсором. ↑↓ — пункт, ←→ — переключить/
// выбрать, на текстовом пункте (токен, путь к Claude) — сразу ввод. Изменения применяются сразу и
// сохраняются (AppConfig.SaveUserSettings → Storage/settings.json). Esc — в меню.
public class SettingsDisplay(WorldState settings, DisplayConfig display, AppConfig config, Storage storage, string title)
{
    private readonly List<MouseUiHelper.TitleTab> _titleTabs = MouseUiHelper.ComputeTitleTabs(title);
    private bool _escHovered;
    private enum Kind { Toggle, Choice, Text, Info } // Info — строка справки (управление), не редактируется

    private sealed class Item(string label, Kind kind, string hint)
    {
        public string Label = label;
        public Kind Kind = kind;
        public string Hint = hint;
        public Func<string> Get = () => "";
        public Action<int> Step = _ => { };          // ←→ для переключателей и выбора
        public Action<string> Set = _ => { };        // текст
        public bool Secret;                          // токен — в списке не показываем целиком
        public string? Group;                        // заголовок группы над пунктом
        public Func<bool> Visible = () => true;      // скрыт — не рисуется и не выбирается (настройки другой нейронки)
    }

    private static readonly (string Id, string Name)[] Models =
    [
        ("claude-opus-5-5", "Opus 5.5"), ("claude-sonnet-5-5", "Sonnet 5.5"), ("claude-haiku-4-5-20251001", "Haiku 4.5"),
    ];
    private static readonly (string Id, string Name)[] CodexModels =
    [
        ("gpt-6.1-sol", "6.1 Sol"), ("gpt-6-astra", "6 Astra"), ("gpt-6-luna", "6 Luna"),
    ];

    // Подсказка ко всем моделям ИИ: игра создавалась и проверялась на Opus.
    private const string OpusNote = "Настоятельно рекомендуется Opus: игра создавалась и тестировалась на нём, с другими моделями возможны баги.";

    private const int ListWidth = 60, HintWidth = 40, Gap = 3, BoxInner = 22;
    private const int LabelCol = 3, ValueCol = 22;

    private readonly List<Item> _items = [];
    private int _focus;
    private bool _editing;
    private string _edit = "";

    private int _width, _height, _x0, _bodyTop, _left;
    private char[,] _chars = new char[0, 0];
    private List<int>?[,] _fg = new List<int>?[0, 0];
    private char[,]? _drawnChars;
    private List<int>?[,]? _drawnFg;
    private readonly List<(int row, int item)> _itemRows = [];
    private readonly List<(int row, int item, int left, int right)> _arrows = [];
    private int _hovered = -1;

    private List<int> Fg => display.MainForeground;
    private List<int> Bright => ColorHelper.Pale(display.MainForeground, 0.75);
    private List<int> Dim => ColorHelper.Darker(display.MainForeground, 0.5);

    public void Show()
    {
        _drawnChars = null;
        _drawnFg = null;
        BuildItems();
        var border = new BorderDrawer(settings, display);
        _bodyTop = Console.CursorTop + 1;
        border.DrawSeparator();
        _width = display.InnerWidth(display.ViewCols(settings.Map));
        _height = Math.Max(24, Console.WindowHeight - _bodyTop - 1);
        _x0 = DisplayConfig.LeftMargin + 1;
        for (int r = 0; r < _height; r++) border.DrawContentLine(() => { });
        border.DrawBottomBorder();
        Redraw();

        string speechStatus = Speech.Status;
        while (true)
        {
            if (speechStatus != Speech.Status) { speechStatus = Speech.Status; Redraw(); }
            var (move, click, _) = ConsoleMouseReader.DrainMouseEvents();
            if (move is { } m)
            {
                int over = ItemAt(m.x, m.y);
                bool overEsc = MouseUiHelper.GetHoveredTabKey(m.x, m.y, _titleTabs) == "Esc";
                ConsoleMouseReader.SetCursorShape(over >= 0 || overEsc || ArrowAt(m.x, m.y).dir != 0);
                if (overEsc != _escHovered)
                {
                    _escHovered = overEsc;
                    MouseUiHelper.SetTabHighlight(title, _titleTabs, "Esc", overEsc, display);
                }
                if (over != _hovered) { _hovered = over; Redraw(); }
            }
            if (click is { } c)
            {
                // «[Esc]МЕНЮ» в заголовке — как Esc; остальная шапка — перетаскивание окна.
                if (MouseUiHelper.GetHoveredTabKey(c.x, c.y, _titleTabs) == "Esc")
                {
                    Speech.Stop();
                    if (_editing) Commit();
                    Sound.PlayClick();
                    ConsoleMouseReader.SetCursorShape(false);
                    return;
                }
                if (c.y < _bodyTop) { ConsoleMouseReader.StartWindowDrag(); continue; }
                // Стрелки «◄ ►» есть у каждого пункта и меняют именно его (а не пункт в фокусе).
                var (arrowItem, arrow) = ArrowAt(c.x, c.y);
                int item = ItemAt(c.x, c.y);
                if (arrow != 0)
                {
                    if (_editing) Commit();
                    _focus = arrowItem;
                    Change(_items[arrowItem], arrow);
                    Redraw();
                    continue;
                }
                if (item >= 0)
                {
                    if (_editing) Commit();
                    // Клик по строке только выбирает пункт; значение меняют стрелки «◄ ►» (или ←→).
                    _focus = item;
                    Sound.PlayClick();
                    StartEditIfText();
                    Redraw();
                }
                continue;
            }

            if (ConsoleMouseReader.TryReadKey() is not { } key) { Thread.Sleep(15); continue; }
            // Read queued repeats before drawing, so holding a key cannot build a render backlog.
            for (int i = 0; i < 64; i++)
            {
                if (key.Key == ConsoleKey.Escape)
                {
                    if (_editing) { _editing = false; break; } // отменить ввод
                    return;
                }
                bool wasEditing = _editing;
                if (HandleKey(key)) return;
                if (i == 63 || !wasEditing || !_editing || ConsoleMouseReader.TryReadKey() is not { } next) break;
                key = next;
            }
            Redraw();
        }
    }

    // true — выйти в меню.
    private bool HandleKey(ConsoleKeyInfo key)
    {
        var item = _items[_focus];
        if (_editing)
        {
            if (ClipboardText.IsPasteKey(key))
            {
                string paste = ClipboardText.Get().Trim();
                _edit += paste[..Math.Min(paste.Length, Math.Max(0, 400 - _edit.Length))];
                return false;
            }
            switch (key.Key)
            {
                case ConsoleKey.Enter: Commit(); return false;
                case ConsoleKey.UpArrow: Commit(); Move(-1); return false;
                case ConsoleKey.DownArrow: Commit(); Move(1); return false;
                case ConsoleKey.Backspace: if (_edit.Length > 0) _edit = _edit[..^1]; return false;
            }
            if (!char.IsControl(key.KeyChar) && _edit.Length < 400) _edit += key.KeyChar;
            return false;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow: Move(-1); return false;
            case ConsoleKey.DownArrow or ConsoleKey.Tab: Move(1); return false;
            case ConsoleKey.LeftArrow: Change(item, -1); return false;
            case ConsoleKey.RightArrow: Change(item, 1); return false;
            case ConsoleKey.Enter:
                if (item.Kind == Kind.Toggle) Change(item, 1);
                StartEditIfText();
                return false;
        }
        return false;
    }

    // Следующий настраиваемый пункт (строки справки управления пропускаются).
    private void Move(int dir)
    {
        for (int i = _focus + dir; i >= 0 && i < _items.Count; i += dir)
        {
            if (_items[i].Kind == Kind.Info || !_items[i].Visible()) continue;
            _focus = i;
            Sound.PlayClick();
            StartEditIfText();
            return;
        }
    }

    // Текстовый пункт в фокусе — сразу ввод (как поля анкеты), с текущим значением.
    private void StartEditIfText()
    {
        if (_items[_focus].Kind != Kind.Text || _editing) return;
        _editing = true;
        _edit = _items[_focus].Get();
    }

    private void Commit()
    {
        if (!_editing) return;
        _editing = false;
        var item = _items[_focus];
        string value = _edit.Trim().Trim('"');
        if (value.Length == 0 || value == item.Get()) return;
        item.Set(value);
        config.SaveUserSettings();
    }

    private void Change(Item item, int dir)
    {
        if (item.Kind is not (Kind.Toggle or Kind.Choice)) return;
        item.Step(dir);
        config.SaveUserSettings();
        Sound.PlayClick();
    }

    // ── Пункты ────────────────────────────────────────────────────────────────
    private void BuildItems()
    {
        _items.Clear();
        Toggle("ОЗВУЧКА", "Русские голоса, локально без ключа. В готовой сборке Silero, Python и модель уже включены: скачивание не требуется. F11 — остановить речь; Enter/Esc также останавливают её.",
            () => config.SpeechEnabled, v =>
            {
                config.SpeechEnabled = v;
                Speech.Stop();
                if (v) Speech.WarmUp();
                if (v) Speech.Speak("Озвучка включена. Я буду читать рассказ ведущего.");
            }, group: "ОЗВУЧКА");
        _items.Add(new Item("ГОЛОС РАССКАЗЧИКА", Kind.Choice, "Айдар и Евгений — мужские голоса; Бая, Ксения и Ксения 2 — женские. При переключении звучит пробная фраза; выбор сохраняется.")
        {
            Get = () => config.SileroVoice switch { "aidar" => "Айдар", "baya" => "Бая", "kseniya" => "Ксения", "xenia" => "Ксения 2", _ => "Евгений" },
            Step = dir =>
            {
                string[] voices = ["aidar", "eugene", "baya", "kseniya", "xenia"];
                Speech.Stop();
                int index = Array.IndexOf(voices, config.SileroVoice);
                if (index < 0) index = dir > 0 ? voices.Length - 1 : 0;
                config.SileroVoice = voices[(index + dir + voices.Length) % voices.Length];
                Speech.Speak("Вы входите в тёмный лес. У старого дуба вас ждёт незнакомец.");
            },
        });
        Text("ПУТЬ К PYTHON", "Для запуска из исходников без встроенного комплекта нужен Python 3.10–3.12 x64. Готовая сборка использует собственный Python и не требует настройки этого пути.",
            () => config.SileroPythonPath, v => config.SileroPythonPath = v);
        _items.Add(new Item("ГРОМКОСТЬ РЕЧИ", Kind.Choice, "Отдельная громкость речи, независимо от музыки и звуков. Шаг — 10%.")
        {
            Get = () => $"{config.SpeechVolume}%",
            Step = dir => config.SpeechVolume = Math.Clamp(config.SpeechVolume + dir * 10, 0, 100),
        });
        _items.Add(new Item("СТАТУС ОЗВУЧКИ", Kind.Info, "Загрузка и ошибки озвучки. Подробности — в logs/speech.log. Чтобы повторить проверку, выключи и включи озвучку.")
        { Get = () => config.SpeechEnabled ? Speech.Status : "Выключена" });
        Toggle("ЗВУКИ", "Щелчки кнопок, вкладок и стрелок, бросок кубика.",
            () => config.SoundEnabled, v => { config.SoundEnabled = v; Sound.Enabled = v; }, group: "ЗВУК");
        _items.Add(new Item("ГРОМКОСТЬ", Kind.Choice, "Общая громкость всех звуков игры: щелчки, бросок кубика, печать текста. Шаг — 10%.")
        {
            // Проценты — всегда 3 знака (« 60%», «100%»): длина строки не меняется, полоска не прыгает.
            Get = () => new string('█', config.SoundVolume / 10) + new string('░', 10 - config.SoundVolume / 10) + $" {config.SoundVolume,3}%",
            Step = dir =>
            {
                config.SoundVolume = Math.Clamp(config.SoundVolume + dir * 10, 0, 100);
                Sound.Volume = config.SoundVolume / 100f;
            },
        });
        Toggle("МУЗЫКА", "Музыка по ситуации: своя в меню, в дороге, городе, таверне, подземелье, ночью и в бою.",
            () => config.MusicEnabled, v => { config.MusicEnabled = v; Music.Enabled = v; });
        _items.Add(new Item("ГРОМКОСТЬ МУЗЫКИ", Kind.Choice, "Громкость музыки — отдельно от звуков (ещё умножается на общую громкость). Шаг — 10%.")
        {
            Get = () => new string('█', config.MusicVolume / 10) + new string('░', 10 - config.MusicVolume / 10) + $" {config.MusicVolume,3}%",
            Step = dir =>
            {
                config.MusicVolume = Math.Clamp(config.MusicVolume + dir * 10, 0, 100);
                Music.Volume = config.MusicVolume / 100f;
            },
        });
        Toggle("ЗВУКИ ОКРУЖЕНИЯ", "Птицы днём, сверчки ночью, камин в таверне, капли в подземелье, ветер в пути. Громкость — как у музыки.",
            () => config.AmbienceEnabled, v => { config.AmbienceEnabled = v; Music.AmbienceEnabled = v; });
        Toggle("ЗВУК ПЕЧАТИ", "Тихий щипок струны, пока мастер печатает ответ в диалоге.",
            () => config.TypingSoundEnabled, v => { config.TypingSoundEnabled = v; Sound.TypingEnabled = v; });

        _items.Add(new Item("НЕЙРОНКА", Kind.Choice,
            "Кто ведёт игру: Claude (Claude CLI) или Codex (OpenAI Codex CLI — нужен установленный codex и вход в аккаунт). " +
            "Промпты и инструменты те же; игра отлаживалась на Claude, с Codex возможны отступления от формата.")
        {
            Get = () => config.AiProvider == "codex" ? "Codex" : "Claude",
            Step = _ => config.AiProvider = config.AiProvider == "codex" ? "claude" : "codex",
            Group = "ИСКУССТВЕННЫЙ ИНТЕЛЛЕКТ",
        });
        Model("МОДЕЛЬ ИГРЫ", "Модель Claude для ходов игры: действия, бой, триггеры." + "\n\n" + OpusNote,
            () => config.ClaudeModel, v => config.ClaudeModel = v);
        OnlyFor("claude");
        Model("МОДЕЛЬ МИРА", "Модель для старта новой игры: сюжет, план локации, наполнение стартового блока." + "\n\n" + OpusNote,
            () => config.ClaudeStartNewGameModel, v => config.ClaudeStartNewGameModel = v);
        OnlyFor("claude");
        Model("МОДЕЛЬ ГЕРОЯ", "Модель для создания героя по анкете: статы, снаряжение, способности." + "\n\n" + OpusNote,
            () => config.ClaudeCreateNewGameModel, v => config.ClaudeCreateNewGameModel = v);
        OnlyFor("claude");
        Text("ТОКЕН CLAUDE", "OAuth-токен Claude (claude setup-token). Хранится только у тебя в Storage/settings.json.",
            () => config.ClaudeOAuthToken, v => config.ClaudeOAuthToken = v, secret: true);
        OnlyFor("claude");
        Text("ПУТЬ К CLAUDE", "Путь к claude.exe (Claude CLI), через который игра обращается к мастеру.",
            () => config.ClaudeCliPath, v => config.ClaudeCliPath = v);
        OnlyFor("claude");
        Model("МОДЕЛЬ ИГРЫ", "Модель Codex для ходов игры: действия, бой, триггеры. Sol — баланс, Astra — сложные задачи, Luna — простые. Доступность зависит от аккаунта.",
            () => config.CodexModel, v => config.CodexModel = v, choices: CodexModels);
        OnlyFor("codex");
        Model("МОДЕЛЬ МИРА", "Модель Codex для концепции мира и старта новой игры: сюжет, план локации, стартовая сцена.",
            () => string.IsNullOrWhiteSpace(config.CodexStartNewGameModel) ? config.CodexModel : config.CodexStartNewGameModel,
            v => config.CodexStartNewGameModel = v, choices: CodexModels);
        OnlyFor("codex");
        Model("МОДЕЛЬ ГЕРОЯ", "Модель Codex для создания героя: характеристики, снаряжение, способности и описание.",
            () => string.IsNullOrWhiteSpace(config.CodexCreateNewGameModel) ? config.CodexModel : config.CodexCreateNewGameModel,
            v => config.CodexCreateNewGameModel = v, choices: CodexModels);
        OnlyFor("codex");
        _items.Add(new Item("РАЗМЫШЛЕНИЕ CODEX", Kind.Choice,
            "Быстро — меньше ожидание; средне — баланс для игры; глубоко — сложные правила, дольше ответ. Описание героя и починка JSON всегда используют быстрое размышление.")
        {
            Get = () => config.CodexReasoningEffort switch { "low" => "БЫСТРО", "high" => "ГЛУБОКО", _ => "СРЕДНЕ" },
            Step = dir =>
            {
                string[] levels = ["low", "medium", "high"];
                int index = Array.IndexOf(levels, config.CodexReasoningEffort);
                config.CodexReasoningEffort = levels[((index < 0 ? 1 : index) + dir + levels.Length) % levels.Length];
            },
        });
        OnlyFor("codex");
        Text("ПУТЬ К CODEX", "Путь к Codex CLI. «codex» — найти в PATH (из npm-пакета игра сама берёт codex.exe).",
            () => config.CodexCliPath, v => config.CodexCliPath = v.Trim().Length > 0 ? v.Trim() : "codex");
        OnlyFor("codex");

        Toggle("ТУМАН ВОЙНЫ", "Сохранять исследованные клетки между запусками игры. Выключено — каждый запуск карта снова в тумане.",
            () => config.SaveExploredCells, v => { config.SaveExploredCells = v; storage.SaveExploredCells = v; },
            group: "ОТЛАДКА", on: "СОХРАНЯТЬ", off: "НЕ СОХРАНЯТЬ");
        Toggle("ВСЯ КАРТА", "Показать всю карту без тумана войны, все скрытые объекты и существ. Для отладки.",
            () => config.RevealMap, v => config.RevealMap = v, on: "ОТКРЫТА", off: "СКРЫТА");
        Toggle("ТРИГГЕРЫ", "Ловушки, засады, события раундов и наполнение блоков нейронкой. Выключено — герой ходит без событий.",
            () => !config.DisableTriggers, v => config.DisableTriggers = !v);
        Toggle("ОШИБКИ ИИ", "Сбой нейронки (кривой ответ, обрыв) — подробно в диалоге. Выключено — коротко «повтори действие», подробности в логе.",
            () => config.ShowAiErrors, v => config.ShowAiErrors = v, on: "ПОДРОБНО", off: "КОРОТКО");

        Toggle("ЭКРАН", "Весь экран: игра того же размера по центру монитора, вокруг — фон игры (панель задач скрыта). В окне — как обычно, окно можно перетаскивать.",
            () => config.Fullscreen, v => { config.Fullscreen = v; ApplyScreen(); },
            group: "УПРАВЛЕНИЕ", on: "ВЕСЬ ЭКРАН", off: "В ОКНЕ");
        string[] faces = ConsoleSetup.FontFaces;
        _items.Add(new Item("ТИП ШРИФТА", Kind.Choice,
            "Шрифт игры: Consolas — обычный, DejaVu Sans Mono — гладкий (из папки Fonts игры, ровный и на размерах крупнее 16).")
        {
            Get = () => config.FontFace,
            Step = dir =>
            {
                int i = Math.Max(0, Array.IndexOf(faces, config.FontFace));
                config.FontFace = faces[(i + dir + faces.Length) % faces.Length];
                ConsoleSetup.FontFace = config.FontFace;
                // У шрифта свои размеры — выбранного у него нет: авто.
                if (!ConsoleSetup.FontSizes(config.FontFace).Contains(config.FullscreenFontSize)) config.FullscreenFontSize = 0;
                ApplyScreen();
            },
        });
        _items.Add(new Item("ШРИФТ", Kind.Choice,
            "Размер шрифта во весь экран — от него размер всей игры: авто — самый крупный, при котором игра влезает в монитор; " +
            "число — этот размер (не влезает — сколько влезает). Размеры — те, где шрифт ровный: Consolas 14/16, DejaVu 16–22. В окне — всегда 16.")
        {
            Get = () => config.FullscreenFontSize <= 0 ? "авто" : config.FullscreenFontSize.ToString(),
            Step = dir =>
            {
                int[] fonts = [0, .. ConsoleSetup.FontSizes(config.FontFace)];   // авто + размеры этого шрифта
                int i = Math.Max(0, Array.IndexOf(fonts, config.FullscreenFontSize));
                config.FullscreenFontSize = fonts[(i + dir + fonts.Length) % fonts.Length];
                ApplyScreen();
            },
        });
        Toggle("ПОДСКАЗКИ КЛАВИШ", "Подсказки клавиш [F1]…[F10], [Esc] у вкладок и подвкладок. Выключено — их не видно, клавиши работают как прежде.",
            () => config.ShowKeyHints, v => { config.ShowKeyHints = v; display.ShowKeyHints = v; },
            on: "ПОКАЗЫВАТЬ", off: "СКРЫТЬ");
        // Управление — пока только справка (без переназначения клавиш).
        Info("СТРЕЛКИ", "шаг героя");
        Info("ДВЕ СТРЕЛКИ", "шаг по диагонали (или NumPad)");
        Info("CTRL+СТРЕЛКИ", "сдвинуть карту");
        Info("МЫШЬ: ТЯНУТЬ", "сдвинуть карту / карту мира");
        Info("МЫШЬ: НАВЕСТИ", "путь, дистанция, карточка");
        Info("МЫШЬ: КЛИК", "закрепить карточку");
        Info("КОЛЕСО, CTRL +/−", "масштаб карты мира");
        Info("МИР: КЛИК, F10", "цель пути, в путь (F12 — темп)");
        Info("TAB / SHIFT+TAB", "выбрать существо или объект");
        Info("ALT+← →", "листать карточку");
        Info("ALT+↑ ↓", "листать диалог");
        Info("ТЕКСТ + ENTER", "действие героя — мастеру");
        Info("F1–F5", "вкладки: карта (серая — тактической карты нет), мир, журнал, персонаж, спутник");
        Info("F6–F10", "подвкладки текущей вкладки");
        Info("ESC", "главное меню");
    }

    // Весь экран и масштаб — сразу: шрифт и размер окна (ConsoleSetup), подложка и окно по центру.
    private void ApplyScreen()
    {
        _drawnChars = null;
        _drawnFg = null;
        if (!config.Fullscreen) FullscreenBackdrop.Set(false, display.MainBackground);
        ConsoleSetup.Fullscreen = config.Fullscreen;
        ConsoleSetup.FullscreenFontSize = config.FullscreenFontSize;
        ConsoleSetup.SetConsoleConfig(settings);
        if (config.Fullscreen) FullscreenBackdrop.Set(true, display.MainBackground);
        ConsoleMouseReader.RefreshWindowButtons();
    }

    // Последний добавленный пункт — только для выбранной нейронки (claude/codex).
    private void OnlyFor(string provider)
    {
        var item = _items[^1];
        item.Visible = () => (config.AiProvider == "codex" ? "codex" : "claude") == provider;
    }

    private void Info(string keys, string action, string? group = null) =>
        _items.Add(new Item(keys, Kind.Info, "") { Get = () => action, Group = group });

    private void Toggle(string label, string hint, Func<bool> get, Action<bool> set, string? group = null,
        string on = "ВКЛ", string off = "ВЫКЛ") =>
        _items.Add(new Item(label, Kind.Toggle, hint) { Get = () => get() ? on : off, Step = _ => set(!get()), Group = group });

    private void Model(string label, string hint, Func<string> get, Action<string> set, string? group = null,
        (string Id, string Name)[]? choices = null) =>
        _items.Add(new Item(label, Kind.Choice, hint)
        {
            Get = () => (choices ?? Models).FirstOrDefault(m => m.Id == get()).Name ?? get(),
            Step = dir =>
            {
                var options = choices ?? Models;
                int i = Array.FindIndex(options, m => m.Id == get());
                set(options[(Math.Max(0, i) + dir + options.Length) % options.Length].Id);
            },
            Group = group,
        });

    private void Text(string label, string hint, Func<string> get, Action<string> set, bool secret = false) =>
        _items.Add(new Item(label, Kind.Text, hint) { Get = get, Set = set, Secret = secret });

    // ── Мышь ──────────────────────────────────────────────────────────────────
    private int ItemAt(short x, short y)
    {
        foreach (var (row, item) in _itemRows)
            if (y == _bodyTop + row && x > _x0 + _left && x < _x0 + _left + ListWidth - 1)
                return _items[item].Kind == Kind.Info ? -1 : item;
        return -1;
    }

    // Стрелка под мышью: (пункт, -1/+1) или (-1, 0).
    private (int item, int dir) ArrowAt(short x, short y)
    {
        foreach (var (row, item, l, r) in _arrows)
        {
            if (y != _bodyTop + row) continue;
            if (x == _x0 + l) return (item, -1);
            if (x == _x0 + r) return (item, 1);
        }
        return (-1, 0);
    }

    // ── Отрисовка ─────────────────────────────────────────────────────────────
    private void Redraw()
    {
        _chars = new char[_height, _width];
        _fg = new List<int>?[_height, _width];
        for (int r = 0; r < _height; r++) for (int c = 0; c < _width; c++) _chars[r, c] = ' ';
        _itemRows.Clear();
        _arrows.Clear();

        int total = ListWidth + Gap + HintWidth;
        _left = Math.Max(1, (_width - total) / 2);
        // Высота рамок — по содержимому: пункты + заголовки групп с отступами + поля сверху/снизу.
        int contentRows = _items.Count(i => i.Visible()) + _items.Count(i => i.Group != null) * 2 - 1;
        int boxHeight = Math.Max(BoxInner, contentRows + 3) + 2;
        int top = Math.Max(0, (_height - boxHeight - 3) / 2);
        DrawBox(top, _left, ListWidth, boxHeight, "НАСТРОЙКИ");
        DrawBox(top, _left + ListWidth + Gap, HintWidth, boxHeight, "ПОДСКАЗКА");

        int y = top + 2;
        int valueWidth = ListWidth - ValueCol - 3;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (!item.Visible()) continue;
            if (item.Group != null)
            {
                if (i > 0) y++;
                Put(y++, _left + LabelCol, item.Group, Dim);
            }
            DrawItem(i, y, valueWidth);
            _itemRows.Add((y, i));
            y++;
        }

        // Подсказка к пункту под курсором (мышь важнее фокуса клавиатуры).
        var hinted = _items[_hovered >= 0 ? _hovered : _focus];
        int hx = _left + ListWidth + Gap + 3;
        Put(top + 2, hx, hinted.Label, Bright);
        // Абзацы подсказки — по переносам строк (пустая строка — отступ между абзацами).
        var lines = hinted.Hint.Split('\n')
            .SelectMany(p => p.Length == 0 ? [""] : TextWrapper.WrapText(p, HintWidth - 6))
            .ToList();
        for (int i = 0; i < lines.Count && i < boxHeight - 6; i++) Put(top + 4 + i, hx, lines[i], Fg);

        string footer = _editing ? "[Enter]СОХРАНИТЬ   [Esc]ОТМЕНИТЬ" : "[↑↓]ПУНКТ   [←→]ИЗМЕНИТЬ";
        CenterIn(top + boxHeight + 1, _left, total, footer, Dim);

        Console.CursorVisible = false;
        for (int r = 0; r < _height; r++)
            if (RowChanged(r)) WriteRow(r);
        _drawnChars = _chars;
        _drawnFg = _fg;
    }

    private bool RowChanged(int row)
    {
        if (_drawnChars == null || _drawnFg == null
            || _drawnChars.GetLength(0) != _height || _drawnChars.GetLength(1) != _width) return true;
        for (int c = 0; c < _width; c++)
            if (_drawnChars[row, c] != _chars[row, c]
                || !(_drawnFg[row, c] ?? display.MainForeground)
                    .SequenceEqual(_fg[row, c] ?? display.MainForeground)) return true;
        return false;
    }

    private void DrawItem(int index, int y, int valueWidth)
    {
        var item = _items[index];
        bool focused = index == _focus;
        bool lit = focused || index == _hovered;
        int lx = _left + LabelCol, vx = _left + ValueCol;

        if (item.Kind == Kind.Info)
        {
            Put(y, lx, item.Label, Fg);
            Put(y, vx, Fit(item.Get(), valueWidth + 2), Dim);
            return;
        }

        Put(y, lx - 2, focused ? "→ " + item.Label : "  " + item.Label, lit ? Bright : Fg);
        switch (item.Kind)
        {
            case Kind.Toggle or Kind.Choice:
            {
                // Значение — в поле фиксированной ширины: «◄» и «►» стоят на месте.
                const int slot = 16; // влезает «██████████ 100%»
                string value = Fit(item.Get(), slot);
                int rx = vx + 2 + slot + 1;
                Put(y, vx, "◄", lit ? Bright : Fg);
                Put(y, vx + 2 + (slot - value.Length) / 2, value, focused ? Bright : Fg);
                Put(y, rx, "►", lit ? Bright : Fg);
                _arrows.Add((y, index, vx, rx));
                break;
            }
            case Kind.Text:
            {
                bool editing = focused && _editing;
                string value = editing ? _edit : item.Get();
                if (item.Secret && !editing) value = Mask(value);
                string shown = editing
                    ? (value.Length > valueWidth - 1 ? "…" + value[^(valueWidth - 2)..] : value) + "▌"
                    : Fit(value.Length == 0 ? "—" : value, valueWidth);
                Put(y, vx, shown, editing ? Bright : value.Length == 0 ? Dim : Fg);
                break;
            }
        }
    }

    // Токен в списке — начало и конец, середина скрыта.
    private static string Mask(string token) =>
        token.Length <= 16 ? new string('•', token.Length) : token[..10] + "••••••" + token[^4..];

    private void DrawBox(int y, int x, int w, int h, string title)
    {
        Put(y, x, "╭─ ", MouseUiHelper.FrameColor(display));
        Put(y, x + 3, title, Bright);
        Put(y, x + 3 + title.Length, " " + new string('─', Math.Max(0, w - title.Length - 5)) + "╮", MouseUiHelper.FrameColor(display));
        for (int r = 1; r < h - 1; r++)
        {
            Put(y + r, x, "│", MouseUiHelper.FrameColor(display));
            Put(y + r, x + w - 1, "│", MouseUiHelper.FrameColor(display));
        }
        Put(y + h - 1, x, "╰" + new string('─', w - 2) + "╯", MouseUiHelper.FrameColor(display));
    }

    private void CenterIn(int row, int left, int width, string text, List<int> color) =>
        Put(row, left + Math.Max(0, (width - text.Length) / 2), text, color);

    private static string Fit(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";

    private void Put(int row, int x, string text, List<int> color)
    {
        if (row < 0 || row >= _height) return;
        for (int i = 0; i < text.Length && x + i < _width; i++)
        {
            if (x + i < 0) continue;
            _chars[row, x + i] = text[i];
            _fg[row, x + i] = color;
        }
    }

    private void WriteRow(int row)
    {
        Console.SetCursorPosition(_x0, _bodyTop + row);
        int c = 0;
        while (c < _width)
        {
            var fg = _fg[row, c];
            int start = c;
            while (c < _width && ReferenceEquals(_fg[row, c], fg)) c++;
            var run = new string(Enumerable.Range(start, c - start).Select(i => _chars[row, i]).ToArray());
            ColorHelper.WriteColored(run, fgColor: fg ?? display.MainForeground, bgColor: display.MainBackground);
        }
    }
}
