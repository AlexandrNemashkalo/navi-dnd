using NaviDnD.Clients;
using NaviDnD.Display;
using NaviDnD.Helpers;
using System.Text;
using NaviDnD.Data;
using NaviDnD.Data.Models;

namespace NaviDnD;

class Program
{
    static async Task Main()
    {
        // Необработанное исключение раньше просто ронял процесс молча (консоль закрывается сразу,
        // .NET печатает стек в stderr, который никто не видит) — ни строчки в логах. Теперь падение
        // хотя бы попадает в тот же ai_*.log, где и обычные вызовы ИИ, и его можно разобрать.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                string dir = Path.Combine(AppConfig.ProjectRoot, "logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"ai_{DateTime.Now:yyyy-MM-dd}.log");
                string text = $"=== {DateTime.Now:HH:mm:ss.fff} | UNHANDLED CRASH ===\n{e.ExceptionObject}\n\n";
                File.AppendAllText(path, text, new UTF8Encoding(false));
            }
            catch { /* краш-логгер не должен сам уронить обработчик краша */ }
        };

        // Окно консоли Windows создаёт и показывает ДО первой строчки Main() — прячем сразу же,
        // всё (без рамки, размер под карту, шрифт/цвета) настраиваем невидимо и показываем готовым.
        Console.CursorVisible = false;
        ConsoleSetup.BlockConsoleFullscreen();
        ConsoleSetup.LoadBundledFonts();   // шрифты из папки Fonts — до настройки шрифта консоли
        ConsoleSetup.EnableAnsiSupport();
        ConsoleSetup.DisableQuickEdit();
        ConsoleMouseReader.Enable();
        ConsoleZoomBlocker.Start(); // Ctrl+колесо / щипок тачпада не меняют шрифт и размер окна
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ConsoleMouseReader.Disable();

        var config = new AppConfig();
        config.InitializeStorage();
        // Настройки игрока (экран «НАСТРОЙКИ») — не в UI-тестах: там важны значения по умолчанию.
        if (Environment.GetEnvironmentVariable("NAVIDND_TEST_WORLDSTATE") == null) config.LoadUserSettings();
        Sound.Enabled = config.SoundEnabled;
        Speech.Initialize(config);
        GameUpdates.Initialize();
        Sound.Volume = config.SoundVolume / 100f;
        Sound.TypingEnabled = config.TypingSoundEnabled;
        Music.Enabled = config.MusicEnabled;
        Music.AmbienceEnabled = config.AmbienceEnabled;
        Music.Volume = config.MusicVolume / 100f;

        var storage = new Storage { SaveExploredCells = config.SaveExploredCells };
        string? testStatePath = Environment.GetEnvironmentVariable("NAVIDND_TEST_WORLDSTATE");
        Func<bool> hasSave = testStatePath != null ? () => File.Exists(testStatePath) : Storage.HasSave;
        // Игры, начатые до карты мира, при загрузке привязываются к активному миру (GameWorld.EnsureLink).
        Action loadCurrentState = testStatePath != null ? () => storage.LoadFrom(testStatePath) : () => { storage.LoadSave(); GameWorld.EnsureLink(storage.WorldState); };
        Storage.InitGames(); // прежнее сохранение → игра 1, активная игра — последняя
        if (hasSave()) loadCurrentState();
        var settings = storage.WorldState;

        ConsoleSetup.ShowWindow(); // без этого не открывается
        // Весь экран и масштаб (настройки; не в UI-тестах) — до первой настройки окна: шрифт под монитор.
        ConsoleSetup.Fullscreen = config.Fullscreen && testStatePath == null;
        ConsoleSetup.FullscreenFontSize = config.FullscreenFontSize;
        // Шрифт из настроек (UI-тесты — по умолчанию); убранный из списка (SimSun, MS Gothic, Lucida Console, Cascadia Mono, Courier New, Unifont) — снова Consolas.
        if (testStatePath == null)
            ConsoleSetup.FontFace = config.FontFace = ConsoleSetup.FontFaces.Contains(config.FontFace) ? config.FontFace : "Consolas";
        ConsoleSetup.SetConsoleConfig(settings);
        var logger = new AiLogger();

        // Clean up stale requests from previous session
        string rollRequestPath = Path.Combine(AppConfig.ProjectRoot, "Storage", "roll_request.json");
        if (File.Exists(rollRequestPath)) File.Delete(rollRequestPath);
        string askPlayerRequestPath = Path.Combine(AppConfig.ProjectRoot, "Storage", "ask_player_request.json");
        if (File.Exists(askPlayerRequestPath)) File.Delete(askPlayerRequestPath);
        string selectTargetRequestPath = Path.Combine(AppConfig.ProjectRoot, "Storage", "select_target_request.json");
        if (File.Exists(selectTargetRequestPath)) File.Delete(selectTargetRequestPath);

        // Нейронка — из настроек: Claude CLI или Codex CLI (один и тот же промпт и MCP-сервер игры), меняется сразу.
        IAiProvider provider = new SwitchableAiProvider(config, logger);
        if (config.MockedActions.Length > 0)
            provider = new SelectiveMockProvider(provider, config.MockedActions.ToHashSet(), config.MockResponsesDir);

        var aiClient = new GameAiClient(settings, storage, provider, config) { Logger = logger };

        var display = new DisplayConfig { ShowKeyHints = config.ShowKeyHints };
        // Весь экран (настройка; не в UI-тестах) — окно уже своего размера: подложка и окно по центру.
        if (config.Fullscreen && testStatePath == null) FullscreenBackdrop.Set(true, display.MainBackground);
        var borderDrawer = new BorderDrawer(settings, display);
        var legendDisplay = new LegendDisplay(storage, display, config);
        var mapDisplay = new MapDisplay(settings, display, legendDisplay);
        var heroDisplay = new HeroDisplay(settings, display);
        var abilityDisplay = new AbilityDisplay(settings, display);
        var journalDisplay = new JournalDisplay(settings, display);
        // Что в журнале уже видели (для метки «ЖУРНАЛ•»): снимок содержимого; новая/другая игра — заново.
        string? journalSeen = null;
        int journalSeenSlot = -1;

        // Прогреть фон/рамку темы ДО показа окна — иначе пользователь видит кадр с дефолтным
        // чёрным фоном/белым текстом на месте нашего оформления в момент появления окна.
        Console.CursorVisible = false;
        Console.BackgroundColor = ConsoleColor.Black;
        ColorHelper.ClearScreen(display.MainBackground, display.MainForeground);
        ColorHelper.SetBackgroundColor(display.MainBackground);
        ColorHelper.SetForegroundColor(display.MainForeground);
        Console.OutputEncoding = Encoding.UTF8;

        var mapLoop = new MapScreenLoop(settings, display, storage, aiClient, config, mapDisplay, legendDisplay);

        var state = new GameState();
        // Музыка и звуки окружения по ситуации (MusicDirector, sound/music, sound/ambience). В UI-тестах — тишина.
        if (testStatePath == null) MusicDirector.Start(state, settings);
        var screens = ScreenRegistry.GetScreens(state, storage, hasSave, loadCurrentState, () => GameWorld.HasLocation(settings));

        display.TabSwitchProvider = tabKey => tabKey switch
        {
            // Карта (местность) — только когда есть тактическая карта; без неё вкладка серая.
            "F1" => display.OnMapRedraw == null || !GameWorld.HasLocation(settings) ? (null, null) : (() => { display.JournalShown = false; display.MapLevel = MapLevel.Location; display.OnMapRedraw(); }, screens[Screen.Map].Title),
            "F2" => display.OnMapRedraw == null ? (null, null) : (() => { display.JournalShown = false; display.MapLevel = MapLevel.World; display.OnMapRedraw(); }, screens[Screen.World].Title),
            "F3" => (() => { display.JournalShown = true; journalSeen = JournalDisplay.Stamp(settings); journalDisplay.Draw(); }, screens[Screen.Journal].Title),
            "F4" => (() => { display.JournalShown = false; heroDisplay.DrawHeroCard(); }, screens[Screen.Character].Title),
            "F5" => (() => { display.JournalShown = false; abilityDisplay.Draw(); }, screens[Screen.Abilities].Title),
            "F6" when display.JournalShown => (() => { journalDisplay.SetSectionSilently(JournalDisplay.Section.Quests); journalDisplay.Draw(); }, screens[Screen.Journal].Title),
            "F7" when display.JournalShown => (() => { journalDisplay.SetSectionSilently(JournalDisplay.Section.People); journalDisplay.Draw(); }, screens[Screen.Journal].Title),
            "F8" when display.JournalShown => (() => { journalDisplay.SetSectionSilently(JournalDisplay.Section.Locations); journalDisplay.Draw(); }, screens[Screen.Journal].Title),
            "F9" when display.JournalShown => (() => { journalDisplay.SetSectionSilently(JournalDisplay.Section.Bestiary); journalDisplay.Draw(); }, screens[Screen.Journal].Title),
            "F10" when display.JournalShown => (() => { journalDisplay.SetSectionSilently(JournalDisplay.Section.Notes); journalDisplay.Draw(); }, screens[Screen.Journal].Title),
            "F6" => (() => { display.CharacterSubTab = CharacterSubTab.Inventory;  display.InventoryScrollOffset = 0; display.SelectedInventoryIndex = -1; display.SelectedImageLines = null; display.SelectedImageColor = null; heroDisplay.DrawHeroCard(); }, screens[Screen.Character].Title),
            "F7" => (() => { display.CharacterSubTab = CharacterSubTab.Abilities; display.AbilityPageOffset  = 0; display.SelectedInventoryIndex = -1; display.SelectedImageLines = null; display.SelectedImageColor = null; heroDisplay.DrawHeroCard(); }, screens[Screen.Character].Title),
            "F8" => (() => { display.CharacterSubTab = CharacterSubTab.Effects;   display.EffectsPageOffset  = 0; display.SelectedInventoryIndex = -1; display.SelectedImageLines = null; display.SelectedImageColor = null; heroDisplay.DrawHeroCard(); }, screens[Screen.Character].Title),
            "F9" => !heroDisplay.HasSpells ? (null, null) : (() => { display.CharacterSubTab = CharacterSubTab.Spells;    display.SpellsPageOffset   = 0; display.SelectedInventoryIndex = -1; display.SelectedImageLines = null; display.SelectedImageColor = null; heroDisplay.DrawHeroCard(); }, screens[Screen.Character].Title),
            _    => (null, null)
        };

        // «МОИ ИГРЫ»: выбрать игру (загрузить и в карту) или свободное место (новая игра туда).
        void ShowGames(string? message)
        {
            ColorHelper.ClearScreen(display.MainBackground, display.MainForeground);
            borderDrawer.DrawTopBorder();
            const string gamesTitle = " МОИ ИГРЫ   [Esc]МЕНЮ";
            borderDrawer.DrawContentLine(() => MouseUiHelper.WriteColoredTitle(gamesTitle, display));
            var (action, slot) = new GamesDisplay(settings, display, gamesTitle, message).Show();
            if (action == GamesDisplay.Action.Play)
            {
                Storage.SetActiveSlot(slot);
                loadCurrentState();
                state.CurrentScreen = Screen.Map;
            }
            else if (action == GamesDisplay.Action.NewGame)
            {
                state.NewGameSlot = slot;
                state.NewGame = null;   // создание с чистого листа
                state.CurrentScreen = Screen.NewGame;
            }
        }

        if (testStatePath == null && !AiCliDiscovery.IsAvailable("claude", config.ClaudeCliPath)
            && !AiCliDiscovery.IsAvailable("codex", config.CodexCliPath))
        {
            AiSetupNotice.Show();
            ColorHelper.ClearScreen(display.MainBackground, display.MainForeground);
            borderDrawer.DrawTopBorder();
            const string setupTitle = " НАСТРОЙКИ: Claude и Codex не найдены   [Esc]МЕНЮ";
            borderDrawer.DrawContentLine(() => MouseUiHelper.WriteColoredTitle(setupTitle, display));
            new SettingsDisplay(settings, display, config, storage, setupTitle).Show();
        }

        while (true)
        {
            if (state.PendingStartNewGame)
            {
                state.PendingStartNewGame = false;
                // Новая игра сохраняется в выбранное для неё место (Storage.SlotPath) — оно становится текущим.
                if (state.NewGameSlot is int newSlot) Storage.SetActiveSlot(newSlot);
                state.NewGameSlot = null;
                var startTask = aiClient.StartNewGame("");
                // Ожидание — отдельным экраном: рамка, заголовок и надпись по центру (а не в углу прежнего экрана).
                ColorHelper.ClearScreen(display.MainBackground, display.MainForeground);
                Console.SetCursorPosition(0, 0);
                borderDrawer.DrawTopBorder();
                borderDrawer.DrawContentLine(() => MouseUiHelper.WriteColoredTitle(" НОВАЯ ИГРА", display));
                borderDrawer.DrawSeparator();
                int waitRows = Math.Max(3, Console.WindowHeight - 5);
                for (int r = 0; r < waitRows; r++) borderDrawer.DrawContentLine(() => { });
                borderDrawer.DrawBottomBorder();
                await Helpers.Spinner.WhileCentered(startTask, "Создаём мир", 3 + waitRows / 2,
                    DisplayConfig.LeftMargin + 1, display.InnerWidth(display.ViewCols(settings.Map)), ColorHelper.Pale(display.MainForeground, 0.75));
                await startTask;
                // Параметры приключения — мастеру на каждый ход (narrative.style → строка style: в состоянии).
                if (storage.NewGameData?.AdventureStyle is { Length: > 0 } adventureStyle)
                {
                    (storage.WorldState.Narrative ??= new()).Style = adventureStyle;
                    storage.Save();
                }
                // Герой — в библиотеку героев (новая игра им же — шаг «Герой»).
                // (в UI-тестах — нет: тестовый герой не должен попасть в список игрока)
                if (testStatePath == null) HeroLibrary.SyncFromSave(Storage.SavePath);
                logger.LogTriggers(storage.WorldState.Map);
            }

            ConsoleSetup.SetConsoleConfig(settings);
            Console.CursorVisible = false;
            Console.BackgroundColor = ConsoleColor.Black;
            ColorHelper.ClearScreen(display.MainBackground, display.MainForeground);
            ColorHelper.SetBackgroundColor(display.MainBackground);
            ColorHelper.SetForegroundColor(display.MainForeground);
            Console.OutputEncoding = Encoding.UTF8;

            // Тактической карты нет (герой в пути, сцена текстом) — вместо местности мир (до отрисовки заголовка).
            if (state.CurrentScreen == Screen.Map && !GameWorld.HasLocation(settings)) state.CurrentScreen = Screen.World;
            var screen = screens[state.CurrentScreen];

            // Главное меню рисует экран целиком само (логотип, пункты, картинки) — без строки заголовка.
            // F1/F2/Esc — те же команды экрана Menu; ActiveTabKey сброшен, чтобы звук не глушился.
            if (state.CurrentScreen == Screen.Menu)
            {
                display.ActiveTabKey = null;
                var choice = new MainMenuDisplay(settings, display).Show(hasSave());
                if (choice == MainMenuDisplay.Choice.Updates)
                {
                    if (await GameUpdates.ShowAsync(new UpdateDisplay(settings, display))) return;
                    continue;
                }
                if (choice == MainMenuDisplay.Choice.Settings)
                {
                    // Настройки — свой экран в рамке с заголовком; выход из них — снова в меню.
                    ColorHelper.ClearScreen(display.MainBackground, display.MainForeground);
                    borderDrawer.DrawTopBorder();
                    const string settingsTitle = " НАСТРОЙКИ   [Esc]МЕНЮ";
                    borderDrawer.DrawContentLine(() => MouseUiHelper.WriteColoredTitle(settingsTitle, display));
                    new SettingsDisplay(settings, display, config, storage, settingsTitle).Show();
                    continue;
                }
                // Новая игра — в первое свободное место; все заняты — «МОИ ИГРЫ» с подсказкой освободить.
                if (choice == MainMenuDisplay.Choice.NewGame)
                {
                    state.NewGame = null;   // создание с чистого листа
                    if (Storage.FreeSlot() is int free) state.NewGameSlot = free;
                    else { ShowGames($"Все {Storage.MaxGames} места заняты — удали одну из игр [Del], чтобы начать новую"); continue; }
                }
                if (choice == MainMenuDisplay.Choice.Games) { ShowGames(null); continue; }
                string command = choice switch
                {
                    MainMenuDisplay.Choice.Continue => "F1",
                    MainMenuDisplay.Choice.NewGame => "F2",
                    _ => "Esc",
                };
                if (screen.Commands.TryGetValue(command, out var menuAction)) menuAction();
                continue;
            }

            borderDrawer.DrawTopBorder();

            // Метка нового в журнале — во всех игровых заголовках.
            if (journalSeenSlot != Storage.ActiveSlot || journalSeen == null)
            {
                journalSeenSlot = Storage.ActiveSlot;
                journalSeen = JournalDisplay.Stamp(settings);
            }
            if (state.CurrentScreen == Screen.Journal) journalSeen = JournalDisplay.Stamp(settings);
            ScreenRegistry.RefreshGameTitles(screens, JournalDisplay.Stamp(settings) != journalSeen, mapDisabled: !GameWorld.HasLocation(settings));
            // Новая игра: сессия — до отрисовки заголовка (на первом шаге доступен только «МИР»).
            if (state.CurrentScreen == Screen.NewGame) state.NewGame ??= new NewGameSession();
            if (state.NewGame is { } newGameSession) ScreenRegistry.RefreshNewGameTitles(screens, newGameSession);

            string title = state.CurrentScreen == Screen.Menu && !hasSave()
                ? " МЕНЮ   [F2]НОВАЯ ИГРА     [Esc]ВЫЙТИ"
                : screen.Title;

            borderDrawer.DrawContentLine(() => MouseUiHelper.WriteColoredTitle(title, display));

            
            switch (state.CurrentScreen)
            {
                // Карта (местность) и Мир — тот же цикл карты; тактической карты нет (герой в пути, сцена
                // текстом) — вместо местности мир.
                case Screen.Map:
                case Screen.World:
                    if (state.CurrentScreen == Screen.Map && !GameWorld.HasLocation(settings))
                    {
                        state.CurrentScreen = Screen.World;
                        screen = screens[Screen.World];
                        title = screen.Title;
                    }
                    display.JournalShown = false;
                    display.ActiveTabKey = state.CurrentScreen == Screen.World ? "F2" : "F1";
                    display.MapLevel = state.CurrentScreen == Screen.World ? MapLevel.World : MapLevel.Location;
                    await mapLoop.RunAsync(screen, title);
                    break;

                case Screen.Journal:
                    display.ActiveTabKey = "F3";
                    display.JournalShown = true;
                    display.SelectedImageLines = null;
                    display.SelectedImageColor = null;
                    display.NotePage = 0;
                    display.NotePageCount = 0;
                    MouseUiHelper.SetDefaultImage(display, "lorc/open-book");
                    await MouseUiHelper.HandleGameScreen(settings, display, storage, screen, journalDisplay.Draw,
                        (s, dlg) => aiClient.SendAction(s, dlg == null ? null : dlg.AppendStreamChunk, dlg == null ? null : dlg.NewStreamingMessage, dlg == null ? null : dlg.TickSpinner, null),
                        title,
                        onScrollLeft:  () => journalDisplay.Flip(-1),
                        onScrollRight: () => journalDisplay.Flip(1),
                        onTab:         () => journalDisplay.SelectNext(1),
                        onShiftTab:    () => journalDisplay.SelectNext(-1),
                        onF6: () => journalDisplay.SetSection(JournalDisplay.Section.Quests),
                        onF7: () => journalDisplay.SetSection(JournalDisplay.Section.People),
                        onF8: () => journalDisplay.SetSection(JournalDisplay.Section.Locations),
                        onF9: () => journalDisplay.SetSection(JournalDisplay.Section.Bestiary),
                        onF10: () => journalDisplay.SetSection(JournalDisplay.Section.Notes),
                        onTextInput: journalDisplay.TryAddNote,
                        onDelete: journalDisplay.DeleteSelectedNote,
                        setActiveDialog: d => aiClient.ActiveDialog = d,
                        pollFactory: dlg => MouseUiHelper.MakeJournalPollAction(dlg, journalDisplay, title, display));
                    display.JournalShown = false;
                    break;

                case Screen.Character:
                    display.JournalShown = false;
                    display.ActiveTabKey = "F4";
                    display.SelectedInventoryIndex = -1;
                    display.SelectedImageLines = null;
                    display.SelectedImageColor = null;
                    display.PinnedInventoryIndex = -1;
                    display.NotePage = 0;
                    display.NotePageCount = 0;
                    MouseUiHelper.SetDefaultImage(display, settings.Hero?.Image, settings.Hero?.Color);
                    await MouseUiHelper.HandleGameScreen(settings, display, storage, screen, heroDisplay.DrawHeroCard,
                        (s, dlg) => aiClient.SendAction(s, dlg == null ? null : dlg.AppendStreamChunk, dlg == null ? null : dlg.NewStreamingMessage, dlg == null ? null : dlg.TickSpinner, null),
                        title,
                        heroDisplay:   heroDisplay,
                        onScrollLeft: () =>
                        {
                            switch (display.CharacterSubTab)
                            {
                                case CharacterSubTab.Inventory:
                                    display.SelectedInventoryIndex = display.InventoryPageFirstPrevItemIdx;
                                    display.InventoryScrollOffset = Math.Max(0, display.InventoryScrollOffset - 1);
                                    break;
                                case CharacterSubTab.Abilities:
                                    display.AbilityPageOffset = Math.Max(0, display.AbilityPageOffset - 1);
                                    break;
                                case CharacterSubTab.Spells:
                                    display.SpellsPageOffset = Math.Max(0, display.SpellsPageOffset - 1);
                                    break;
                                default:
                                    display.EffectsPageOffset = Math.Max(0, display.EffectsPageOffset - 1);
                                    break;
                            }
                        },
                        onScrollRight: () =>
                        {
                            switch (display.CharacterSubTab)
                            {
                                case CharacterSubTab.Inventory:
                                    display.SelectedInventoryIndex = display.InventoryPageFirstNextItemIdx;
                                    display.InventoryScrollOffset++;
                                    break;
                                case CharacterSubTab.Abilities:
                                    display.AbilityPageOffset++;
                                    break;
                                case CharacterSubTab.Spells:
                                    display.SpellsPageOffset++;
                                    break;
                                default:
                                    display.EffectsPageOffset++;
                                    break;
                            }
                        },
                        onTab: () =>
                        {
                            if (display.CharacterSubTab == CharacterSubTab.Spells)
                            {
                                var spells = settings.Hero == null ? [] : HeroDisplay.SortedSpells(settings.Hero);
                                if (spells.Count == 0) return;
                                if (display.SelectedSpellIndex < 0)
                                {
                                    display.SelectedSpellIndex = 0;
                                }
                                else if (display.SelectedSpellIndex == display.SpellsPageLastItemIdx
                                    && display.SpellsPageFirstNextItemIdx >= 0)
                                {
                                    display.SpellsPageOffset++;
                                    display.SelectedSpellIndex = display.SpellsPageFirstNextItemIdx;
                                }
                                else
                                {
                                    int next = display.SelectedSpellIndex + 1;
                                    if (next >= spells.Count) { display.SpellsPageOffset = 0; next = 0; }
                                    display.SelectedSpellIndex = next;
                                }
                                if (display.SelectedSpellIndex < spells.Count)
                                {
                                    var spell = spells[display.SelectedSpellIndex];
                                    display.PinnedSpellIndex = display.SelectedSpellIndex;
                                    MouseUiHelper.SetSelectedSpell(display, spell, 0);
                                }
                                return;
                            }
                            if (display.CharacterSubTab != CharacterSubTab.Inventory) return;
                            var items = settings.Hero?.Inventory ?? [];
                            if (items.Count == 0) return;
                            if (display.SelectedInventoryIndex < 0)
                            {
                                display.SelectedInventoryIndex = 0;
                            }
                            else if (display.SelectedInventoryIndex == display.InventoryPageLastItemIdx
                                && display.InventoryPageFirstNextItemIdx >= 0)
                            {
                                display.InventoryScrollOffset++;
                                display.SelectedInventoryIndex = display.InventoryPageFirstNextItemIdx;
                            }
                            else
                            {
                                int next = display.SelectedInventoryIndex + 1;
                                if (next >= items.Count) { display.InventoryScrollOffset = 0; next = 0; }
                                display.SelectedInventoryIndex = next;
                            }
                            if (display.SelectedInventoryIndex < items.Count)
                            {
                                var item = items[display.SelectedInventoryIndex];
                                display.PinnedInventoryIndex = display.SelectedInventoryIndex;
                                MouseUiHelper.SetSelectedInventoryItem(display, item, 0);
                            }
                        },
                        onShiftTab: () =>
                        {
                            if (display.CharacterSubTab == CharacterSubTab.Spells)
                            {
                                var spells = settings.Hero == null ? [] : HeroDisplay.SortedSpells(settings.Hero);
                                if (spells.Count == 0) return;
                                if (display.SelectedSpellIndex < 0)
                                {
                                    display.SpellsPageOffset = int.MaxValue;
                                    display.SelectedSpellIndex = spells.Count - 1;
                                }
                                else if (display.SelectedSpellIndex == display.SpellsPageFirstItemIdx
                                    && display.SpellsPageFirstPrevItemIdx >= 0)
                                {
                                    display.SpellsPageOffset--;
                                    display.SelectedSpellIndex = display.SpellsPageFirstPrevItemIdx;
                                }
                                else
                                {
                                    int prev = display.SelectedSpellIndex - 1;
                                    if (prev < 0) { display.SpellsPageOffset = int.MaxValue; prev = spells.Count - 1; }
                                    display.SelectedSpellIndex = prev;
                                }
                                if (display.SelectedSpellIndex < spells.Count && display.SelectedSpellIndex >= 0)
                                {
                                    var spell = spells[display.SelectedSpellIndex];
                                    display.PinnedSpellIndex = display.SelectedSpellIndex;
                                    MouseUiHelper.SetSelectedSpell(display, spell, 0);
                                }
                                return;
                            }
                            if (display.CharacterSubTab != CharacterSubTab.Inventory) return;
                            var items = settings.Hero?.Inventory ?? [];
                            if (items.Count == 0) return;
                            if (display.SelectedInventoryIndex < 0)
                            {
                                display.InventoryScrollOffset = int.MaxValue;
                                display.SelectedInventoryIndex = items.Count - 1;
                            }
                            else if (display.SelectedInventoryIndex == display.InventoryPageFirstItemIdx
                                && display.InventoryPageFirstPrevItemIdx >= 0)
                            {
                                display.InventoryScrollOffset--;
                                display.SelectedInventoryIndex = display.InventoryPageFirstPrevItemIdx;
                            }
                            else
                            {
                                int prev = display.SelectedInventoryIndex - 1;
                                if (prev < 0) { display.InventoryScrollOffset = int.MaxValue; prev = items.Count - 1; }
                                display.SelectedInventoryIndex = prev;
                            }
                            if (display.SelectedInventoryIndex < items.Count && display.SelectedInventoryIndex >= 0)
                            {
                                var item = items[display.SelectedInventoryIndex];
                                display.PinnedInventoryIndex = display.SelectedInventoryIndex;
                                MouseUiHelper.SetSelectedInventoryItem(display, item, 0);
                            }
                        },
                        onF6: () => { display.CharacterSubTab = CharacterSubTab.Inventory;  display.InventoryScrollOffset = 0; display.SelectedInventoryIndex = -1; display.SelectedSpellIndex = -1; display.PinnedSpellIndex = -1; display.SelectedImageLines = null; display.SelectedImageColor = null; display.PinnedInventoryIndex = -1; display.NotePage = 0; display.NotePageCount = 0; },
                        onF7: () => { display.CharacterSubTab = CharacterSubTab.Abilities; display.AbilityPageOffset = 0;  display.SelectedInventoryIndex = -1; display.SelectedSpellIndex = -1; display.PinnedSpellIndex = -1; display.SelectedImageLines = null; display.SelectedImageColor = null; display.PinnedInventoryIndex = -1; display.NotePage = 0; display.NotePageCount = 0; },
                        onF8: () => { display.CharacterSubTab = CharacterSubTab.Effects;   display.EffectsPageOffset = 0;  display.SelectedInventoryIndex = -1; display.SelectedSpellIndex = -1; display.PinnedSpellIndex = -1; display.SelectedImageLines = null; display.SelectedImageColor = null; display.PinnedInventoryIndex = -1; display.NotePage = 0; display.NotePageCount = 0; },
                        onF9: () => { if (!heroDisplay.HasSpells) return; display.CharacterSubTab = CharacterSubTab.Spells;    display.SpellsPageOffset = 0;   display.SelectedInventoryIndex = -1; display.SelectedSpellIndex = -1; display.PinnedSpellIndex = -1; display.SelectedImageLines = null; display.SelectedImageColor = null; display.PinnedInventoryIndex = -1; display.NotePage = 0; display.NotePageCount = 0; },
                        onInventorySelect: (idx, page) =>
                        {
                            var items = settings.Hero?.Inventory ?? [];
                            if (idx >= 0 && idx < items.Count)
                                MouseUiHelper.SetSelectedInventoryItem(display, items[idx], page);
                        },
                        onSpellSelect: (idx, page) =>
                        {
                            var spells = settings.Hero == null ? [] : HeroDisplay.SortedSpells(settings.Hero);
                            if (idx >= 0 && idx < spells.Count)
                                MouseUiHelper.SetSelectedSpell(display, spells[idx], page);
                        },
                        setActiveDialog: d => aiClient.ActiveDialog = d);
                    break;

                case Screen.Abilities:
                    display.JournalShown = false;
                    display.ActiveTabKey = "F5";
                    MouseUiHelper.SetDefaultImage(display, "delapouite/spell-book");
                    await MouseUiHelper.HandleGameScreen(settings, display, storage, screen, abilityDisplay.Draw,
                        (s, dlg) => aiClient.SendAction(s, dlg == null ? null : dlg.AppendStreamChunk, dlg == null ? null : dlg.NewStreamingMessage, dlg == null ? null : dlg.TickSpinner, null),
                        title,
                        onScrollUp:   () => display.AbilityScrollOffset = Math.Max(0, display.AbilityScrollOffset - 1),
                        onScrollDown: () => display.AbilityScrollOffset++,
                        setActiveDialog: d => aiClient.ActiveDialog = d);
                    break;

                // Новая игра по шагам: 1 мир → 2 герой (анкета) → 3 редактор → 4 приключение. Заполненное живёт в
                // state.NewGame (мастер шагов и анкета) — по шагам можно ходить туда-обратно (F1–F4); Esc — выход в
                // меню с предупреждением (Screen.NewGameExit).
                case Screen.NewGame:
                {
                    var ng = state.NewGame ??= new NewGameSession();
                    var wizard = ng.Wizard ??= new NewGameWizard(settings, display, aiClient);
                    wizard.Title = screen.Title;
                    wizard.CanGo = ng.CanGo;
                    int to = await wizard.PickWorld();
                    if (to == 0) { ng.ReturnTo = Screen.NewGame; state.CurrentScreen = Screen.NewGameExit; break; }
                    ng.WorldId = wizard.ChosenWorldId;
                    if (storage.NewGameData is { } chosen) chosen.WorldId = ng.WorldId;
                    state.CurrentScreen = NewGameSession.StepScreen(to);
                    break;
                }

                // Шаг «Герой» — анкета: новый герой или из библиотеки героев (поле ГЕРОЙ сверху).
                case Screen.NewGameHero:
                {
                    var ng = state.NewGame;
                    if (ng?.Wizard == null || ng.WorldId == null) { state.CurrentScreen = Screen.NewGame; break; }
                    Console.SetCursorPosition(0, ng.Wizard.StartTop);
                    var form = ng.Form ??= new NewGameDisplay(settings, display, screen, aiClient);
                    form.CanGo = ng.CanGo;
                    int to = await form.Show();
                    if (to == 0) { ng.ReturnTo = Screen.NewGameHero; state.CurrentScreen = Screen.NewGameExit; break; }
                    if (to >= 3 && form.Data is { } data)
                    {
                        data.WorldId = ng.WorldId;
                        storage.NewGameData = data;
                        ng.HeroReady = true;
                        // Созданный нейронкой герой — сразу в библиотеку героев (в UI-тестах — нет).
                        if (testStatePath == null)
                        {
                            if (form.CreatedNow) { storage.SaveDraft(); HeroLibrary.SyncFromSave(Storage.DraftSavePath); }
                            HeroLibrary.SetDescription(data.Name, data.Description);   // описание — с карточкой героя
                        }
                        form.CreatedNow = false;
                    }
                    state.CurrentScreen = NewGameSession.StepScreen(to);
                    break;
                }

                // Шаг «Приключение» (после редактора): пожелания и место старта; «НАЧАТЬ ИГРУ» — старт.
                case Screen.NewGameAdventure:
                {
                    var ng = state.NewGame;
                    if (ng?.Wizard == null || !ng.HeroReady) { state.CurrentScreen = Screen.Menu; break; }
                    ng.Wizard.Title = screen.Title;
                    ng.Wizard.CanGo = ng.CanGo;
                    var (to, adventure) = await ng.Wizard.PickAdventure(ng.WorldId ?? WorldLibrary.Current.Id, ng.Adventure);
                    ng.Adventure = adventure;
                    if (to == 0) { ng.ReturnTo = Screen.NewGameAdventure; state.CurrentScreen = Screen.NewGameExit; break; }
                    if (to != NewGameWizard.StartStep) { state.CurrentScreen = NewGameSession.StepScreen(to); break; }
                    if (storage.NewGameData is { } ngd)
                    {
                        ngd.WorldId = ng.WorldId;
                        ngd.SettingWish = adventure.Wish;
                        ngd.AdventureStyle = adventure.Style;
                        ngd.StartPlace = adventure.StartPlace;
                        ngd.StartX = adventure.StartTile?.x;
                        ngd.StartY = adventure.StartTile?.y;
                    }
                    state.NewGame = null;
                    screen.Commands["Start"]();
                    break;
                }

                // Выход из создания игры в меню — с предупреждением; «остаться» — назад на тот же шаг.
                case Screen.NewGameExit:
                {
                    var ng = state.NewGame;
                    if (ng?.Wizard == null) { state.CurrentScreen = Screen.Menu; break; }
                    if (await ng.Wizard.ConfirmExit())
                    {
                        ng.Wizard.DiscardWorld();
                        state.NewGame = null;
                        state.CurrentScreen = Screen.Menu;
                    }
                    else state.CurrentScreen = ng.ReturnTo;
                    break;
                }

                case Screen.NewGameCharacter:
                    display.ActiveTabKey = "F3";
                    display.SelectedInventoryIndex = -1;
                    display.SelectedImageLines = null;
                    display.SelectedImageColor = null;
                    display.PinnedInventoryIndex = -1;
                    display.NotePage = 0;
                    display.NotePageCount = 0;
                    MouseUiHelper.SetDefaultImage(display, settings.Hero?.Image, settings.Hero?.Color);
                    await MouseUiHelper.HandleGameScreen(settings, display, storage, screen, heroDisplay.DrawHeroCard,
                        (s, dlg) => aiClient.FixHeroForNewGame(s, dlg),
                        title,
                        heroDisplay:   heroDisplay,
                        onScrollLeft:  () =>
                        {
                            switch (display.CharacterSubTab)
                            {
                                case CharacterSubTab.Inventory:
                                    display.SelectedInventoryIndex = display.InventoryPageFirstPrevItemIdx;
                                    display.InventoryScrollOffset = Math.Max(0, display.InventoryScrollOffset - 1);
                                    break;
                                case CharacterSubTab.Abilities:
                                    display.AbilityPageOffset = Math.Max(0, display.AbilityPageOffset - 1);
                                    break;
                                case CharacterSubTab.Spells:
                                    display.SpellsPageOffset = Math.Max(0, display.SpellsPageOffset - 1);
                                    break;
                                default:
                                    display.EffectsPageOffset = Math.Max(0, display.EffectsPageOffset - 1);
                                    break;
                            }
                        },
                        onScrollRight: () =>
                        {
                            switch (display.CharacterSubTab)
                            {
                                case CharacterSubTab.Inventory:
                                    display.SelectedInventoryIndex = display.InventoryPageFirstNextItemIdx;
                                    display.InventoryScrollOffset++;
                                    break;
                                case CharacterSubTab.Abilities:
                                    display.AbilityPageOffset++;
                                    break;
                                case CharacterSubTab.Spells:
                                    display.SpellsPageOffset++;
                                    break;
                                default:
                                    display.EffectsPageOffset++;
                                    break;
                            }
                        },
                        onTab: () =>
                        {
                            if (display.CharacterSubTab == CharacterSubTab.Spells)
                            {
                                var spells = settings.Hero == null ? [] : HeroDisplay.SortedSpells(settings.Hero);
                                if (spells.Count == 0) return;
                                if (display.SelectedSpellIndex < 0)
                                {
                                    display.SelectedSpellIndex = 0;
                                }
                                else if (display.SelectedSpellIndex == display.SpellsPageLastItemIdx
                                    && display.SpellsPageFirstNextItemIdx >= 0)
                                {
                                    display.SpellsPageOffset++;
                                    display.SelectedSpellIndex = display.SpellsPageFirstNextItemIdx;
                                }
                                else
                                {
                                    int next = display.SelectedSpellIndex + 1;
                                    if (next >= spells.Count) { display.SpellsPageOffset = 0; next = 0; }
                                    display.SelectedSpellIndex = next;
                                }
                                if (display.SelectedSpellIndex < spells.Count)
                                {
                                    var spell = spells[display.SelectedSpellIndex];
                                    display.PinnedSpellIndex = display.SelectedSpellIndex;
                                    MouseUiHelper.SetSelectedSpell(display, spell, 0);
                                }
                                return;
                            }
                            if (display.CharacterSubTab != CharacterSubTab.Inventory) return;
                            var items = settings.Hero?.Inventory ?? [];
                            if (items.Count == 0) return;
                            if (display.SelectedInventoryIndex < 0)
                            {
                                display.SelectedInventoryIndex = 0;
                            }
                            else if (display.SelectedInventoryIndex == display.InventoryPageLastItemIdx
                                && display.InventoryPageFirstNextItemIdx >= 0)
                            {
                                display.InventoryScrollOffset++;
                                display.SelectedInventoryIndex = display.InventoryPageFirstNextItemIdx;
                            }
                            else
                            {
                                int next = display.SelectedInventoryIndex + 1;
                                if (next >= items.Count) { display.InventoryScrollOffset = 0; next = 0; }
                                display.SelectedInventoryIndex = next;
                            }
                            if (display.SelectedInventoryIndex < items.Count)
                            {
                                var item = items[display.SelectedInventoryIndex];
                                display.PinnedInventoryIndex = display.SelectedInventoryIndex;
                                MouseUiHelper.SetSelectedInventoryItem(display, item, 0);
                            }
                        },
                        onShiftTab: () =>
                        {
                            if (display.CharacterSubTab == CharacterSubTab.Spells)
                            {
                                var spells = settings.Hero == null ? [] : HeroDisplay.SortedSpells(settings.Hero);
                                if (spells.Count == 0) return;
                                if (display.SelectedSpellIndex < 0)
                                {
                                    display.SpellsPageOffset = int.MaxValue;
                                    display.SelectedSpellIndex = spells.Count - 1;
                                }
                                else if (display.SelectedSpellIndex == display.SpellsPageFirstItemIdx
                                    && display.SpellsPageFirstPrevItemIdx >= 0)
                                {
                                    display.SpellsPageOffset--;
                                    display.SelectedSpellIndex = display.SpellsPageFirstPrevItemIdx;
                                }
                                else
                                {
                                    int prev = display.SelectedSpellIndex - 1;
                                    if (prev < 0) { display.SpellsPageOffset = int.MaxValue; prev = spells.Count - 1; }
                                    display.SelectedSpellIndex = prev;
                                }
                                if (display.SelectedSpellIndex < spells.Count && display.SelectedSpellIndex >= 0)
                                {
                                    var spell = spells[display.SelectedSpellIndex];
                                    display.PinnedSpellIndex = display.SelectedSpellIndex;
                                    MouseUiHelper.SetSelectedSpell(display, spell, 0);
                                }
                                return;
                            }
                            if (display.CharacterSubTab != CharacterSubTab.Inventory) return;
                            var items = settings.Hero?.Inventory ?? [];
                            if (items.Count == 0) return;
                            if (display.SelectedInventoryIndex < 0)
                            {
                                display.InventoryScrollOffset = int.MaxValue;
                                display.SelectedInventoryIndex = items.Count - 1;
                            }
                            else if (display.SelectedInventoryIndex == display.InventoryPageFirstItemIdx
                                && display.InventoryPageFirstPrevItemIdx >= 0)
                            {
                                display.InventoryScrollOffset--;
                                display.SelectedInventoryIndex = display.InventoryPageFirstPrevItemIdx;
                            }
                            else
                            {
                                int prev = display.SelectedInventoryIndex - 1;
                                if (prev < 0) { display.InventoryScrollOffset = int.MaxValue; prev = items.Count - 1; }
                                display.SelectedInventoryIndex = prev;
                            }
                            if (display.SelectedInventoryIndex < items.Count && display.SelectedInventoryIndex >= 0)
                            {
                                var item = items[display.SelectedInventoryIndex];
                                display.PinnedInventoryIndex = display.SelectedInventoryIndex;
                                MouseUiHelper.SetSelectedInventoryItem(display, item, 0);
                            }
                        },
                        onF6: () => { display.CharacterSubTab = CharacterSubTab.Inventory;  display.InventoryScrollOffset = 0; display.SelectedInventoryIndex = -1; display.SelectedSpellIndex = -1; display.PinnedSpellIndex = -1; display.SelectedImageLines = null; display.SelectedImageColor = null; display.PinnedInventoryIndex = -1; display.NotePage = 0; display.NotePageCount = 0; },
                        onF7: () => { display.CharacterSubTab = CharacterSubTab.Abilities; display.AbilityPageOffset = 0;  display.SelectedInventoryIndex = -1; display.SelectedSpellIndex = -1; display.PinnedSpellIndex = -1; display.SelectedImageLines = null; display.SelectedImageColor = null; display.PinnedInventoryIndex = -1; display.NotePage = 0; display.NotePageCount = 0; },
                        onF8: () => { display.CharacterSubTab = CharacterSubTab.Effects;   display.EffectsPageOffset = 0;  display.SelectedInventoryIndex = -1; display.SelectedSpellIndex = -1; display.PinnedSpellIndex = -1; display.SelectedImageLines = null; display.SelectedImageColor = null; display.PinnedInventoryIndex = -1; display.NotePage = 0; display.NotePageCount = 0; },
                        onF9: () => { if (!heroDisplay.HasSpells) return; display.CharacterSubTab = CharacterSubTab.Spells;    display.SpellsPageOffset = 0;   display.SelectedInventoryIndex = -1; display.SelectedSpellIndex = -1; display.PinnedSpellIndex = -1; display.SelectedImageLines = null; display.SelectedImageColor = null; display.PinnedInventoryIndex = -1; display.NotePage = 0; display.NotePageCount = 0; },
                        onInventorySelect: (idx, page) =>
                        {
                            var items = settings.Hero?.Inventory ?? [];
                            if (idx >= 0 && idx < items.Count)
                                MouseUiHelper.SetSelectedInventoryItem(display, items[idx], page);
                        },
                        onSpellSelect: (idx, page) =>
                        {
                            var spells = settings.Hero == null ? [] : HeroDisplay.SortedSpells(settings.Hero);
                            if (idx >= 0 && idx < spells.Count)
                                MouseUiHelper.SetSelectedSpell(display, spells[idx], page);
                        },
                        setActiveDialog: d => aiClient.ActiveDialog = d);
                    break;

                case Screen.NewGameAbilities:
                    display.ActiveTabKey = "F2";
                    await MouseUiHelper.HandleGameScreen(settings, display, storage, screen, abilityDisplay.Draw,
                        (s, dlg) => aiClient.FixHeroForNewGame(s, dlg),
                        title,
                        onScrollUp:   () => display.AbilityScrollOffset = Math.Max(0, display.AbilityScrollOffset - 1),
                        onScrollDown: () => display.AbilityScrollOffset++,
                        setActiveDialog: d => aiClient.ActiveDialog = d);
                    break;
            }
        }
    }
}
