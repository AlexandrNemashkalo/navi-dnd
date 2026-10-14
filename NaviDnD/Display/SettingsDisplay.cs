using NaviDnD.Data;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

// The settings screen is in the style of a questionnaire for a new game: in the center there is a “SETTINGS”
// frame with groups (sound, AI, debugging) and a “HINT” frame with a description of the item under the cursor.
// ↑↓ - item, ←→ - switch/select, on a text item (token, path to Claude) - immediately enter. Changes are
// applied immediately and saved (AppConfig.SaveUserSettings → Storage/settings.json). Esc - in the menu. Title
// - function: when changing the language, it is built anew.
public class SettingsDisplay(WorldState settings, DisplayConfig display, AppConfig config, Storage storage, Func<string> title)
{
    private string _title = title();
    private List<MouseUiHelper.TitleTab> _titleTabs = [];
    private int _titleRow;
    private bool _escHovered;
    private enum Kind { Toggle, Choice, Text, Info } // Info - help line (control), not editable

    private sealed class Item(string label, Kind kind, string hint)
    {
        public string Label = label;
        public Kind Kind = kind;
        public string Hint = hint;
        public Func<string>? HintTail;               // changing hint tail (full voice acting status)
        public Func<string> Get = () => "";
        public Action<int> Step = _ => { };          // ←→ for switches and selection
        public Action<string> Set = _ => { };        // text
        public bool Secret;                          // token - not shown in its entirety in the list
        public string? Group;                        // group header above item
        public Func<bool> Visible = () => true;      // hidden - not drawn or selected (settings of another AI)
    }

    private static readonly (string Id, string Name)[] Models =
    [
        ("claude-opus-5-5", "Opus 5.5"), ("claude-sonnet-5-5", "Sonnet 5.5"), ("claude-haiku-4-5-20251001", "Haiku 4.5"),
    ];
    private static readonly (string Id, string Name)[] CodexModels =
    [
        ("gpt-6.1-sol", "6.1 Sol"), ("gpt-6-astra", "6 Astra"), ("gpt-6-luna", "6 Luna"),
    ];

    private const int ListWidth = 60, HintWidth = 52, Gap = 3, RowGap = 1, BoxInner = 22;
    private const int LabelCol = 3, ValueCol = 22;

    private readonly List<Item> _items = [];
    private readonly List<(string keys, string action)> _controls = [];
    private int _focus;
    // NaviDnD.app: screen or font changed - window restarts after saving.
    private bool _relaunch;
    // The item selected when opening (after restarting the window - the one that was changed).
    public int? InitialFocus { get; init; }
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
        _titleTabs = MouseUiHelper.ComputeTitleTabs(_title);
        Speech.WarmUp(both: true);   // test phrases of both voices - no delay for loading the model
        BuildItems();
        if (InitialFocus is int focus && focus >= 0 && focus < _items.Count) _focus = focus;
        var border = new BorderDrawer(settings, display);
        _titleRow = Term.CursorTop - 1;
        _bodyTop = Term.CursorTop + 1;
        border.DrawSeparator();
        _width = display.InnerWidth(display.ViewCols(settings.Map));
        _height = Math.Max(24, Term.WindowHeight - _bodyTop - 1);
        _x0 = DisplayConfig.LeftMargin + 1;
        for (int r = 0; r < _height; r++) border.DrawContentLine(() => { });
        border.DrawBottomBorder();
        Redraw();

        string speechStatus = Speech.Status;
        if (config.AiProvider != "codex") ClaudeLogin.Check(config);
        string loginStatus = ClaudeLogin.Status;
        while (true)
        {
            if (speechStatus != Speech.Status) { speechStatus = Speech.Status; Redraw(); }
            if (loginStatus != ClaudeLogin.Status) { loginStatus = ClaudeLogin.Status; Redraw(); }
            var (move, click, _) = ConsoleMouseReader.DrainMouseEvents();
            if (move is { } m)
            {
                int over = ItemAt(m.x, m.y);
                bool overEsc = MouseUiHelper.GetHoveredTabKey(m.x, m.y, _titleTabs) == "Esc";
                ConsoleMouseReader.SetCursorShape(over >= 0 || overEsc || ArrowAt(m.x, m.y).dir != 0);
                if (overEsc != _escHovered)
                {
                    _escHovered = overEsc;
                    MouseUiHelper.SetTabHighlight(_title, _titleTabs, "Esc", overEsc, display);
                }
                if (over != _hovered) { _hovered = over; Redraw(); }
            }
            if (click is { } c)
            {
                // “[Esc]MENU” in the title - as Esc; the rest of the header is dragging the window.
                if (MouseUiHelper.GetHoveredTabKey(c.x, c.y, _titleTabs) == "Esc")
                {
                    Speech.Stop();
                    if (_editing) Commit();
                    Sound.PlayClick();
                    ConsoleMouseReader.SetCursorShape(false);
                    return;
                }
                if (c.y < _bodyTop) { ConsoleMouseReader.StartWindowDrag(); continue; }
                // There are arrows “◄ ►” for each item and they change it (and not the item in focus).
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
                    // Clicking on a line only selects the item; The value is changed by the arrows “◄ ►” (or ←→).
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
                    if (_editing) { _editing = false; break; } // cancel entry
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

    // true — exit to the menu.
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

    // Next custom item (control help lines are skipped).
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

    // Text item in focus - immediate input (like questionnaire fields), with the current value.
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
        if (_relaunch && OperatingSystem.IsMacOS())
        {
            _relaunch = false;
            MacApp.Relaunch(config, _focus);
        }
    }

    // ── Items ──────────────────────────────── ────────────────────────────────
    private void BuildItems()
    {
        _items.Clear();
        _items.Add(new Item(L.T("LANGUAGE"), Kind.Choice, L.T("Language of menus and screens. New worlds are created in this language: the story, names and voice-over use it. An existing world keeps its own language."))
        {
            Get = () => L.IsEnglish ? "ENGLISH" : "РУССКИЙ",
            Step = _ =>
            {
                config.Language = L.IsEnglish ? L.Russian : L.English;
                L.SetLanguage(config.Language);
                BuildItems();
                RedrawTitle();
            },
            Group = L.T("INTERFACE"),
        });
        // The window mode and console font are only available on Windows:; the terminal on macOS/Linux has its own.
        if (OperatingSystem.IsWindows())
        {
            Toggle(L.T("SCREEN"), L.T("Full screen: the game at the same size centered on the monitor with the game background around it (taskbar hidden). Windowed — as usual, the window can be dragged."),
                () => config.Fullscreen, v => { config.Fullscreen = v; ApplyScreen(); },
                on: L.T("FULL SCREEN"), off: L.T("WINDOWED"));
            string[] faces = ConsoleSetup.FontFaces;
            _items.Add(new Item(L.T("FONT FACE"), Kind.Choice,
                L.T("Game font: Consolas — regular, DejaVu Sans Mono — smooth (from the game's Fonts folder, crisp even at sizes above 16)."))
            {
                Get = () => config.FontFace,
                Step = dir =>
                {
                    int i = Math.Max(0, Array.IndexOf(faces, config.FontFace));
                    config.FontFace = faces[(i + dir + faces.Length) % faces.Length];
                    ConsoleSetup.FontFace = config.FontFace;
                    // The font has its own sizes - it does not have a selected one: auto.
                    if (!ConsoleSetup.FontSizes(config.FontFace).Contains(config.FullscreenFontSize)) config.FullscreenFontSize = 0;
                    ApplyScreen();
                },
            });
            _items.Add(new Item(L.T("FONT SIZE"), Kind.Choice,
                L.T("Font size in full screen sets the size of the whole game: auto — the largest that fits the monitor; a number — that size (or as large as fits). Only crisp sizes are offered: Consolas 14/16, DejaVu 16–22. Windowed is always 16."))
            {
                Get = () => config.FullscreenFontSize <= 0 ? L.T("auto") : config.FullscreenFontSize.ToString(),
                Step = dir =>
                {
                    int[] fonts = [0, .. ConsoleSetup.FontSizes(config.FontFace)];   // auto + sizes of this font
                    int i = Math.Max(0, Array.IndexOf(fonts, config.FullscreenFontSize));
                    config.FullscreenFontSize = fonts[(i + dir + fonts.Length) % fonts.Length];
                    ApplyScreen();
                },
            });
        }
        else if (OperatingSystem.IsMacOS() && MacApp.InOwnTerminal)
        {
            // NaviDnD.app: screen and font - near the terminal window; shift restarts the window (and returns to this point).
            Toggle(L.T("SCREEN"), L.T("Full screen: the game in the middle of the screen with the game background around it, the menu bar and Dock hidden. Windowed: a window exactly the size of the game that you can drag by its top. Changing it restarts the game window."),
                () => config.Fullscreen, v => { config.Fullscreen = v; _relaunch = true; },
                on: L.T("FULL SCREEN"), off: L.T("WINDOWED"));
            _items.Add(new Item(L.T("FONT SIZE"), Kind.Choice,
                L.T("Font size sets the size of the whole game: auto is the largest that fits the screen; the list shows only sizes that fit. Changing it restarts the game window."))
            {
                Get = () => config.FullscreenFontSize <= 0 ? L.T("auto") : config.FullscreenFontSize.ToString(),
                Step = dir =>
                {
                    int[] sizes = MacApp.FontSizes(config);
                    int i = Math.Max(0, Array.IndexOf(sizes, config.FullscreenFontSize));
                    config.FullscreenFontSize = sizes[(i + dir + sizes.Length) % sizes.Length];
                    _relaunch = true;
                },
            });
        }
        Toggle(L.T("KEY HINTS"), L.T("Key hints [F1]…[F10], [Esc] on tabs and sub-tabs. Off — they are hidden, the keys still work."),
            () => config.ShowKeyHints, v => { config.ShowKeyHints = v; display.ShowKeyHints = v; },
            on: L.T("SHOW"), off: L.T("HIDE"));

        // Voice acting - in the language of the game (Silero): Russian and English models - in the game. Test
        // phrases - in the language of the voice, not the interface.
        Toggle(L.T("VOICE-OVER"), L.T("Local, no key, in the games language: Russian and English voices are included. F11 stops speech; Enter/Esc stop it too."),
            () => config.SpeechEnabled, v =>
            {
                config.SpeechEnabled = v;
                Speech.Stop();
                if (v) Speech.WarmUp();
                if (v && L.WorldIsEnglish) Speech.Sample("Voice-over is on. I will read the story to you.", config.SileroVoiceEn);
                else if (v) Speech.Sample("Озвучка включена. Я буду читать рассказ ведущего.", config.SileroVoice);
            }, group: L.T("VOICE-OVER"));
        // The line "VOICE ACTING STATUS" is short for an error - it is entirely in the voice acting tooltip.
        _items[^1].HintTail = () => config.SpeechEnabled ? "\n\n" + L.T("Status: ") + Speech.Status : "";
        _items.Add(new Item(L.T("NARRATOR VOICE"), Kind.Choice, L.T("Voice for Russian games. Aidar and Eugene are male voices; Baya, Kseniya and Kseniya 2 are female. A sample phrase plays when you switch; the choice is saved."))
        {
            Get = () => config.SileroVoice switch { "aidar" => L.T("Aidar"), "baya" => L.T("Baya"), "kseniya" => L.T("Kseniya"), "xenia" => L.T("Kseniya 2"), _ => L.T("Eugene") },
            Step = dir =>
            {
                string[] voices = ["aidar", "eugene", "baya", "kseniya", "xenia"];
                Speech.Stop();
                int index = Array.IndexOf(voices, config.SileroVoice);
                if (index < 0) index = dir > 0 ? voices.Length - 1 : 0;
                config.SileroVoice = voices[(index + dir + voices.Length) % voices.Length];
                Speech.Sample("Вы входите в тёмный лес. У старого дуба вас ждёт незнакомец.", config.SileroVoice);
            },
        });
        _items.Add(new Item(L.T("ENGLISH VOICE"), Kind.Choice, L.T("Voice for English games: three male and two female. A sample phrase plays when you switch; the choice is saved."))
        {
            Get = () => Array.IndexOf(Speech.EnglishVoices, config.SileroVoiceEn) switch
            {
                0 => L.T("Male 1"), 1 => L.T("Male 2"), 2 => L.T("Male 3"), 3 => L.T("Female 1"), _ => L.T("Female 2"),
            },
            Step = dir =>
            {
                var voices = Speech.EnglishVoices;
                Speech.Stop();
                int index = Math.Max(0, Array.IndexOf(voices, config.SileroVoiceEn));
                config.SileroVoiceEn = voices[(index + dir + voices.Length) % voices.Length];
                Speech.Sample("You enter a dark forest. A stranger waits for you by the old oak tree.", config.SileroVoiceEn);
            },
        });
        _items.Add(new Item(L.T("SPEECH VOLUME"), Kind.Choice, L.T("Speech volume, separate from music and sounds. Step: 10%."))
        {
            Get = () => new string('█', config.SpeechVolume / 10) + new string('░', 10 - config.SpeechVolume / 10) + $" {config.SpeechVolume,3}%",
            Step = dir => config.SpeechVolume = Math.Clamp(config.SpeechVolume + dir * 10, 0, 100),
        });
        _items.Add(new Item(L.T("VOICE STATUS"), Kind.Info, L.T("Voice-over loading and errors. Details are in logs/speech.log. To check again, turn voice-over off and on."))
        { Get = () => config.SpeechEnabled ? Speech.Status : L.T("Off") });
        Toggle(L.T("SOUNDS"), L.T("Clicks of buttons, tabs and arrows, dice rolls."),
            () => config.SoundEnabled, v => { config.SoundEnabled = v; Sound.Enabled = v; }, group: L.T("SOUND"));
        _items.Add(new Item(L.T("VOLUME"), Kind.Choice, L.T("Overall volume of all game sounds: clicks, dice rolls, typing. Step: 10%."))
        {
            // Percentages are always 3 characters (“60%”, “100%”): the length of the line does not change, the bar does not jump.
            Get = () => new string('█', config.SoundVolume / 10) + new string('░', 10 - config.SoundVolume / 10) + $" {config.SoundVolume,3}%",
            Step = dir =>
            {
                config.SoundVolume = Math.Clamp(config.SoundVolume + dir * 10, 0, 100);
                Sound.Volume = config.SoundVolume / 100f;
            },
        });
        Toggle(L.T("MUSIC"), L.T("Music that fits the moment: its own in the menu, on the road, in town, in a tavern, in a dungeon, at night and in combat."),
            () => config.MusicEnabled, v => { config.MusicEnabled = v; Music.Enabled = v; });
        _items.Add(new Item(L.T("MUSIC VOLUME"), Kind.Choice, L.T("Music volume, separate from sounds (also multiplied by the overall volume). Step: 10%."))
        {
            Get = () => new string('█', config.MusicVolume / 10) + new string('░', 10 - config.MusicVolume / 10) + $" {config.MusicVolume,3}%",
            Step = dir =>
            {
                config.MusicVolume = Math.Clamp(config.MusicVolume + dir * 10, 0, 100);
                Music.Volume = config.MusicVolume / 100f;
            },
        });
        Toggle(L.T("AMBIENT SOUNDS"), L.T("Birds by day, crickets at night, a hearth in the tavern, dripping water in a dungeon, wind on the road. Volume follows the music."),
            () => config.AmbienceEnabled, v => { config.AmbienceEnabled = v; Music.AmbienceEnabled = v; });
        Toggle(L.T("TYPING SOUND"), L.T("A soft string pluck while the Dungeon Master types a reply in the dialog."),
            () => config.TypingSoundEnabled, v => { config.TypingSoundEnabled = v; Sound.TypingEnabled = v; });

        _items.Add(new Item(L.T("AI"), Kind.Choice,
            L.T("Who runs the game: Claude (Claude CLI) or Codex (OpenAI Codex CLI — codex must be installed and signed in). Prompts and tools are the same; the game was tuned on Claude, Codex may stray from the format."))
        {
            Get = () => config.AiProvider == "codex" ? "Codex" : "Claude",
            Step = _ => config.AiProvider = config.AiProvider == "codex" ? "claude" : "codex",
            Group = L.T("ARTIFICIAL INTELLIGENCE"),
        });
        string opusNote = "\n\n" + L.T("Opus is strongly recommended: the game was built and tested on it; other models may cause bugs.");
        Model(L.T("GAME MODEL"), L.T("Claude model for game turns: actions, combat, triggers.") + opusNote,
            () => config.ClaudeModel, v => config.ClaudeModel = v);
        OnlyFor("claude");
        Model(L.T("WORLD MODEL"), L.T("Model for starting a new game: plot, location plan, filling the starting block.") + opusNote,
            () => config.ClaudeStartNewGameModel, v => config.ClaudeStartNewGameModel = v);
        OnlyFor("claude");
        Model(L.T("HERO MODEL"), L.T("Model for creating a hero from the form: stats, equipment, features.") + opusNote,
            () => config.ClaudeCreateNewGameModel, v => config.ClaudeCreateNewGameModel = v);
        OnlyFor("claude");
        // Login - with the button: Enter opens the login in the browser, status - on the right (ClaudeLogin).
        _items.Add(new Item(L.T("CLAUDE SIGN-IN"), Kind.Toggle,
            L.T("Enter — sign in to your Claude account: the browser opens, sign in and allow access, and the game checks the sign-in itself. A Claude subscription (Pro or Max) is required. The sign-in is kept by Claude Code itself — the token below is not needed."))
        {
            Get = () => ClaudeLogin.Status,
            Step = _ => ClaudeLogin.Toggle(config),
        });
        OnlyFor("claude");
        Text(L.T("CLAUDE TOKEN"), L.T("Claude OAuth token (claude setup-token). Stored only on your computer in Storage/settings.json."),
            () => config.ClaudeOAuthToken, v => config.ClaudeOAuthToken = v, secret: true);
        OnlyFor("claude");
        Text(L.T("CLAUDE PATH"), OperatingSystem.IsWindows() ? L.T("Path to claude.exe (Claude CLI), which the game uses to reach the Dungeon Master.") : L.T("Path to claude (Claude CLI), which the game uses to reach the Dungeon Master."),
            () => config.ClaudeCliPath, v => config.ClaudeCliPath = v);
        OnlyFor("claude");
        Model(L.T("GAME MODEL"), L.T("Codex model for game turns: actions, combat, triggers. Sol — balanced, Astra — complex tasks, Luna — simple ones. Availability depends on your account."),
            () => config.CodexModel, v => config.CodexModel = v, choices: CodexModels);
        OnlyFor("codex");
        Model(L.T("WORLD MODEL"), L.T("Codex model for the world concept and new game start: plot, location plan, opening scene."),
            () => string.IsNullOrWhiteSpace(config.CodexStartNewGameModel) ? config.CodexModel : config.CodexStartNewGameModel,
            v => config.CodexStartNewGameModel = v, choices: CodexModels);
        OnlyFor("codex");
        Model(L.T("HERO MODEL"), L.T("Codex model for creating a hero: ability scores, equipment, features and description."),
            () => string.IsNullOrWhiteSpace(config.CodexCreateNewGameModel) ? config.CodexModel : config.CodexCreateNewGameModel,
            v => config.CodexCreateNewGameModel = v, choices: CodexModels);
        OnlyFor("codex");
        _items.Add(new Item(L.T("CODEX REASONING"), Kind.Choice,
            L.T("Fast — less waiting; medium — balanced for play; deep — complex rules, slower replies. Hero description and JSON repair always use fast reasoning."))
        {
            Get = () => config.CodexReasoningEffort switch { "low" => L.T("FAST"), "high" => L.T("DEEP"), _ => L.T("MEDIUM") },
            Step = dir =>
            {
                string[] levels = ["low", "medium", "high"];
                int index = Array.IndexOf(levels, config.CodexReasoningEffort);
                config.CodexReasoningEffort = levels[((index < 0 ? 1 : index) + dir + levels.Length) % levels.Length];
            },
        });
        OnlyFor("codex");
        Text(L.T("CODEX PATH"), OperatingSystem.IsWindows() ? L.T("Path to Codex CLI. \"codex\" — look it up in PATH (for the npm package the game picks codex.exe itself).") : L.T("Path to Codex CLI. \"codex\" — look it up in PATH."),
            () => config.CodexCliPath, v => config.CodexCliPath = v.Trim().Length > 0 ? v.Trim() : "codex");
        OnlyFor("codex");

        Toggle(L.T("FOG OF WAR"), L.T("Keep explored cells between game launches. Off — the map is fogged again on every launch."),
            () => config.SaveExploredCells, v => { config.SaveExploredCells = v; storage.SaveExploredCells = v; },
            group: L.T("DEBUG"), on: L.T("KEEP"), off: L.T("DON'T KEEP"));
        Toggle(L.T("WHOLE MAP"), L.T("Show the whole map without fog of war, all hidden objects and creatures. For debugging."),
            () => config.RevealMap, v => config.RevealMap = v, on: L.T("REVEALED"), off: L.T("HIDDEN"));
        Toggle(L.T("TRIGGERS"), L.T("Traps, ambushes, round events and AI filling of map blocks. Off — the hero walks without events."),
            () => !config.DisableTriggers, v => config.DisableTriggers = !v);
        Toggle(L.T("AI ERRORS"), L.T("AI failures (malformed reply, dropped connection) are shown in full in the dialog. Off — a short \"repeat the action\", details in the log."),
            () => config.ShowAiErrors, v => config.ShowAiErrors = v, on: L.T("DETAILED"), off: L.T("SHORT"));
        // Credits: game creator and thanks.
        _items.Add(new Item(L.T("CREATOR"), Kind.Info, "") { Get = () => "navitalevich", Group = L.T("CREDITS") });
        _items.Add(new Item(L.T("THANKS"), Kind.Info, "") { Get = () => L.F("{0} — macOS port", "keytonw") });
        // Controls - help (without reassigning keys), a separate block on the right under the tooltip.
        _controls.Clear();
        _controls.AddRange([
            (L.T("ARROWS"), L.T("hero step")),
            (L.T("TWO ARROWS"), L.T("diagonal step (or NumPad)")),
            (L.T("CTRL+ARROWS"), L.T("pan the map")),
            OperatingSystem.IsMacOS() ? (L.T("DRAG, TWO FINGERS"), L.T("pan the map / world map")) : (L.T("MOUSE: DRAG"), L.T("pan the map / world map")),
            (L.T("MOUSE: HOVER"), L.T("path, distance, card")),
            (L.T("MOUSE: CLICK"), L.T("pin the card")),
            OperatingSystem.IsMacOS() ? ("⌘ +/−, CTRL +/−", L.T("world map zoom")) : (L.T("WHEEL, CTRL +/−"), L.T("world map zoom")),
            (L.T("WORLD: CLICK, F10"), L.T("destination, travel (F12 — pace)")),
            ("TAB / SHIFT+TAB", L.T("select a creature or object")),
            ("ALT+← →", L.T("page through the card")),
            ("ALT+↑ ↓", L.T("scroll the dialog")),
            (L.T("TEXT + ENTER"), L.T("hero's action to the DM")),
            ("F1–F5", L.T("tabs: map (gray — no tactical map), world, journal, character, companion")),
            ("F6–F10", L.T("sub-tabs of the current tab")),
            ("ESC", L.T("main menu")),
        ]);
    }

    // The language has changed - the screen title is new (the “[Esc]MENU” tab in the new language).
    private void RedrawTitle()
    {
        _title = title();
        _titleTabs = MouseUiHelper.ComputeTitleTabs(_title);
        _drawnChars = null;
        _drawnFg = null;
        Term.SetCursorPosition(0, _titleRow);
        new BorderDrawer(settings, display).DrawContentLine(() => MouseUiHelper.WriteColoredTitle(_title, display));
    }

    // The entire screen and scale at once: font and window size (ConsoleSetup), background and window in the center.
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

    // The last added item is only for the selected AI (claude/codex).
    private void OnlyFor(string provider)
    {
        var item = _items[^1];
        item.Visible = () => (config.AiProvider == "codex" ? "codex" : "claude") == provider;
    }

    private void Toggle(string label, string hint, Func<bool> get, Action<bool> set, string? group = null,
        string? on = null, string? off = null) =>
        _items.Add(new Item(label, Kind.Toggle, hint)
        {
            Get = () => get() ? on ?? L.T("ON") : off ?? L.T("OFF"), Step = _ => set(!get()), Group = group,
        });

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

    // ── Mouse ───────────────────────────────── ─────────────────────────────────
    private int ItemAt(short x, short y)
    {
        foreach (var (row, item) in _itemRows)
            if (y == _bodyTop + row && x > _x0 + _left && x < _x0 + _left + ListWidth - 1)
                return _items[item].Kind == Kind.Info ? -1 : item;
        return -1;
    }

    // Arrow under mouse: (item, -1/+1) or (-1, 0).
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

    // ── Rendering ────────────────────────────── ───────────────────────────────
    private void Redraw()
    {
        _chars = new char[_height, _width];
        _fg = new List<int>?[_height, _width];
        for (int r = 0; r < _height; r++) for (int c = 0; c < _width; c++) _chars[r, c] = ' ';
        _itemRows.Clear();
        _arrows.Clear();

        int total = ListWidth + Gap + HintWidth;
        _left = Math.Max(1, (_width - total) / 2);
        int rx = _left + ListWidth + Gap, textWidth = HintWidth - 6;
        // On the right there are two frames under each other: “HINT” on top, “CONTROL” on the bottom (key -
        // action, move action - under it). On the left - only settings; the total height is the largest of the
        // columns.
        int keyWidth = _controls.Max(c => c.keys.Length) + 2;
        var controlLines = _controls.SelectMany(c => TextWrapper.WrapText(c.action, textWidth - keyWidth)
            .Select((line, n) => (keys: n == 0 ? c.keys : "", line))).ToList();
        int controlsHeight = controlLines.Count + 4;
        // The entire hint fits for any item: height - according to the longest.
        int hintHeight = _items.Where(i => i.Visible()).Max(i => HintLines(i, textWidth).Count) + 6;
        int contentRows = _items.Count(i => i.Visible()) + _items.Count(i => i.Group != null && i.Visible()) * 2 - 1;
        int boxHeight = Math.Max(Math.Max(BoxInner, contentRows + 3) + 2, hintHeight + RowGap + controlsHeight);
        hintHeight = boxHeight - RowGap - controlsHeight;
        int top = Math.Max(0, (_height - boxHeight - 3) / 2);
        DrawBox(top, _left, ListWidth, boxHeight, L.T("SETTINGS"));
        DrawBox(top, rx, HintWidth, hintHeight, L.T("HINT"));
        int controlsTop = top + hintHeight + RowGap;   // line between frames - like gap Gap between columns
        DrawBox(controlsTop, rx, HintWidth, controlsHeight, L.T("CONTROLS"));
        for (int i = 0; i < controlLines.Count; i++)
        {
            Put(controlsTop + 2 + i, rx + 3, controlLines[i].keys, Fg);
            Put(controlsTop + 2 + i, rx + 3 + keyWidth, controlLines[i].line, Dim);
        }

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

        // Tooltip for item under cursor (mouse overrides keyboard focus).
        var hinted = _items[_hovered >= 0 ? _hovered : _focus];
        Put(top + 2, rx + 3, hinted.Label, Bright);
        var lines = HintLines(hinted, textWidth);
        for (int i = 0; i < lines.Count && i < hintHeight - 6; i++) Put(top + 4 + i, rx + 3, lines[i], Fg);

        string footer = _editing ? L.T("[Enter]SAVE   [Esc]CANCEL") : L.T("[↑↓]ITEM   [←→]CHANGE");
        CenterIn(top + boxHeight + 1, _left, total, footer, Dim);

        Term.CursorVisible = false;
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
                // Value - in a fixed-width field: "◄" and "►" are in place.
                const int slot = 16; // fits "██████████ 100%"
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

    // Item tooltip lines: paragraphs - by line breaks (empty line - indentation between paragraphs).
    private static List<string> HintLines(Item item, int width) =>
        (item.Hint + item.HintTail?.Invoke()).Split('\n').SelectMany(p => p.Length == 0 ? [""] : TextWrapper.WrapText(p, width)).ToList();

    // The token in the list is the beginning and end, the middle is hidden.
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
        Term.SetCursorPosition(_x0, _bodyTop + row);
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
