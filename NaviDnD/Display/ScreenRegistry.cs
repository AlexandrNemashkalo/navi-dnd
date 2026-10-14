namespace NaviDnD;

internal static class ScreenRegistry
{
    private static readonly (Screen screen, string key)[] GameTabs =
        [(Screen.Map, "F1"), (Screen.World, "F2"), (Screen.Journal, "F3"), (Screen.Character, "F4"), (Screen.LevelUp, "F4"), (Screen.Abilities, "F5")];

    // Game screen title: active tab " → NAME ←", the rest "[Fn]NAME " - the same length, so the names do not
    // shift when switching (MouseUiHelper.ComputeTitleTabs). New in the journal - “JOURNAL•” (the dot takes the
    // place of the space, the length is the same). There is no tactical map (hero on the way, scene in text) -
    // "{F1}MAP": drawn in gray and not clickable (MouseUiHelper.WriteColoredTitle/ComputeTitleTabs).
    internal static string GameTitleText(string activeKey, bool journalUnread, bool mapDisabled = false)
    {
        (string key, string name)[] tabs =
            [("F1", L.T("MAP")), ("F2", L.T("WORLD")), ("F3", L.T("JOURNAL")), ("F4", L.T("CHARACTER")), ("F5", L.T("COMPANION"))];
        return string.Concat(tabs.Select(t => "   " + (t.key == activeKey ? $"  → {t.name} ←"
                   : t.key == "F1" && mapDisabled ? $"{{{t.key}}}{t.name}  "
                   : t.key == "F3" && journalUnread ? $"[{t.key}]{t.name}• " : $"[{t.key}]{t.name}  ")))
               + "   " + L.T("[Esc]MENU") + "   ";
    }

    // Heading of the new game: steps - like game tabs (“→ EDITOR ←”, “[F1]WORLD”; not yet accessible step -
    // “{F4}ADVENTURE”, dark and not clickable) through “›” - the order of stages, on the right - “[Esc]MENU”.
    internal static string NewGameTitle(int step, Func<int, bool>? canGo = null)
    {
        string[] steps = [L.T("WORLD"), L.T("HERO"), L.T("EDITOR"), L.T("ADVENTURE")];
        return L.T(" NEW GAME") + string.Concat(steps.Select((s, i) => (i == 0 ? "   " : "  ›  ") + (i + 1 == step ? $"  → {s} ←"
                   : canGo?.Invoke(i + 1) ?? i == 0 ? $"[F{i + 1}]{s}  " : $"{{F{i + 1}}}{s}  "))) + "   " + L.T("[Esc]MENU") + "   ";
    }

    // New game step titles based on which steps are already available.
    internal static void RefreshNewGameTitles(Dictionary<Screen, ScreenConfig> screens, NewGameSession ng)
    {
        foreach (var s in new[] { Screen.NewGame, Screen.NewGameHero, Screen.NewGameCharacter, Screen.NewGameAbilities, Screen.NewGameAdventure })
            screens[s].Title = NewGameTitle(NewGameSession.StepOf(s), ng.CanGo);
        screens[Screen.NewGameExit].Title = NewGameTitle(NewGameSession.StepOf(ng.ReturnTo), ng.CanGo);
    }

    // Update game screen titles (new label in journal, gray "Map" without tactical map).
    internal static void RefreshGameTitles(Dictionary<Screen, ScreenConfig> screens, bool journalUnread, bool mapDisabled = false)
    {
        foreach (var (screen, key) in GameTabs)
            if (screens.TryGetValue(screen, out var cfg)) cfg.Title = GameTitleText(key, journalUnread, mapDisabled);
    }

    internal static Dictionary<Screen, ScreenConfig> GetScreens(
        GameState state, Storage storage, Func<bool> hasSave, Action loadCurrentState, Func<bool> hasLocation)
    {
        void GoToMenu()
        {
            state.LastGameScreen = state.CurrentScreen;
            state.CurrentScreen = Screen.Menu;
        }

        void StartGame()
        {
            state.CurrentScreen = Screen.Map;
            state.PendingStartNewGame = true;
        }

        ScreenConfig GameScreen(string title) => new()
        {
            Title = title,
            Commands =
            {
                // Map (terrain) - only when it is available; otherwise the tab is gray, F1 does not open it.
                ["F1"] = () => { if (hasLocation()) state.CurrentScreen = Screen.Map; },
                ["F2"] = () => state.CurrentScreen = Screen.World,
                ["F3"] = () => state.CurrentScreen = Screen.Journal,
                ["F4"] = () => state.CurrentScreen = Screen.Character,
                ["F5"] = () => state.CurrentScreen = Screen.Abilities,
                ["Esc"] = GoToMenu,
            }
        };

        // Game screen title: active tab " → NAME ←", the rest "[Fn]NAME " - the same length, so the names do
        // not shift when switching (MouseUiHelper.ComputeTitleTabs).
        string GameTitle(string activeKey) => GameTitleText(activeKey, journalUnread: false);

        // “Character”: F10 (or a button on the card) – the “New Level” window, when there is enough experience and there is no battle.
        ScreenConfig CharacterScreen()
        {
            var cfg = GameScreen(GameTitle("F4"));
            cfg.Commands["F10"] = () => { if (LevelUp.Available(storage.WorldState)) state.CurrentScreen = Screen.LevelUp; };
            return cfg;
        }

        // New game step (F1–F4), if it is already available; Esc — exit to the menu with a warning.
        void GoStep(int step)
        {
            if (state.NewGame is { } ng && ng.CanGo(step)) state.CurrentScreen = NewGameSession.StepScreen(step);
        }

        void ExitNewGame()
        {
            if (state.NewGame is { } ng) ng.ReturnTo = state.CurrentScreen;
            state.CurrentScreen = state.NewGame != null ? Screen.NewGameExit : Screen.Menu;
        }

        ScreenConfig EditorScreen(string title) => new()
        {
            Title = title,
            Commands =
            {
                ["F1"] = () => GoStep(1),
                ["F2"] = () => GoStep(2),
                ["F4"] = () => GoStep(4),   // step “Adventure”, then start
                ["Esc"] = ExitNewGame,
            }
        };

        return new Dictionary<Screen, ScreenConfig>
        {
            [Screen.Map]      = GameScreen(GameTitle("F1")),
            [Screen.World]    = GameScreen(GameTitle("F2")),
            [Screen.Journal]  = GameScreen(GameTitle("F3")),
            [Screen.Character]= CharacterScreen(),
            [Screen.LevelUp]  = GameScreen(GameTitle("F4")),
            [Screen.Abilities]= GameScreen(GameTitle("F5")), // “Satellite” tab (stub for now)

            [Screen.Menu] = new ScreenConfig
            {
                Title = L.T(" MENU   [F1]CONTINUE     [F2]NEW GAME     [Esc]QUIT"),
                Commands =
                {
                    ["F1"] = () =>
                    {
                        if (hasSave())
                        {
                            loadCurrentState();
                            state.CurrentScreen = Screen.Map;
                        }
                    },
                    ["F2"] = () => state.CurrentScreen = Screen.NewGame,
                    ["Esc"] = () => ConsoleSetup.CloseConsoleAndExit()
                }
            },

            // Step “Adventure” of the new game GM (after the hero editor): wishes and starting place. Steps of
            // the new game: each has its own screen, transitions are the screens themselves (Program) and
            // editor commands.
            [Screen.NewGameAdventure] = new ScreenConfig { Title = NewGameTitle(4), Commands = { ["Start"] = StartGame } },
            [Screen.NewGame] = new ScreenConfig { Title = NewGameTitle(1) },
            [Screen.NewGameHero] = new ScreenConfig { Title = NewGameTitle(2) },
            [Screen.NewGameExit] = new ScreenConfig { Title = NewGameTitle(1) },

            // Editor - "EDIT" step of a new game: only steps in the header (no character/companion tabs).
            [Screen.NewGameCharacter] = EditorScreen(NewGameTitle(3)),
            [Screen.NewGameAbilities] = EditorScreen(NewGameTitle(3)),
        };
    }
}
