namespace NaviDnD;

internal static class ScreenRegistry
{
    private static readonly (Screen screen, string key)[] GameTabs =
        [(Screen.Map, "F1"), (Screen.World, "F2"), (Screen.Journal, "F3"), (Screen.Character, "F4"), (Screen.LevelUp, "F4"), (Screen.Abilities, "F5")];

    // Заголовок игрового экрана: активная вкладка "  → ИМЯ ←", остальные "[Fn]ИМЯ  " — одинаковой длины,
    // поэтому названия не сдвигаются при переключении (MouseUiHelper.ComputeTitleTabs). Новое в журнале —
    // «ЖУРНАЛ•» (точка занимает место пробела, длина та же). Нет тактической карты (герой в пути, сцена
    // текстом) — «{F1}КАРТА»: рисуется серой и не нажимается (MouseUiHelper.WriteColoredTitle/ComputeTitleTabs).
    internal static string GameTitleText(string activeKey, bool journalUnread, bool mapDisabled = false)
    {
        (string key, string name)[] tabs =
            [("F1", L.T("КАРТА")), ("F2", L.T("МИР")), ("F3", L.T("ЖУРНАЛ")), ("F4", L.T("ПЕРСОНАЖ")), ("F5", L.T("СПУТНИК"))];
        return string.Concat(tabs.Select(t => "   " + (t.key == activeKey ? $"  → {t.name} ←"
                   : t.key == "F1" && mapDisabled ? $"{{{t.key}}}{t.name}  "
                   : t.key == "F3" && journalUnread ? $"[{t.key}]{t.name}• " : $"[{t.key}]{t.name}  ")))
               + "   " + L.T("[Esc]МЕНЮ") + "   ";
    }

    // Заголовок новой игры: шаги — как вкладки игры («  → РЕДАКТОР ←», «[F1]МИР  »; ещё недоступный шаг —
    // «{F4}ПРИКЛЮЧЕНИЕ», тёмный и не нажимается) через «›» — порядок этапов, справа — «[Esc]МЕНЮ».
    internal static string NewGameTitle(int step, Func<int, bool>? canGo = null)
    {
        string[] steps = [L.T("МИР"), L.T("ГЕРОЙ"), L.T("РЕДАКТОР"), L.T("ПРИКЛЮЧЕНИЕ")];
        return L.T(" НОВАЯ ИГРА") + string.Concat(steps.Select((s, i) => (i == 0 ? "   " : "  ›  ") + (i + 1 == step ? $"  → {s} ←"
                   : canGo?.Invoke(i + 1) ?? i == 0 ? $"[F{i + 1}]{s}  " : $"{{F{i + 1}}}{s}  "))) + "   " + L.T("[Esc]МЕНЮ") + "   ";
    }

    // Заголовки шагов новой игры — по тому, какие шаги уже доступны.
    internal static void RefreshNewGameTitles(Dictionary<Screen, ScreenConfig> screens, NewGameSession ng)
    {
        foreach (var s in new[] { Screen.NewGame, Screen.NewGameHero, Screen.NewGameCharacter, Screen.NewGameAbilities, Screen.NewGameAdventure })
            screens[s].Title = NewGameTitle(NewGameSession.StepOf(s), ng.CanGo);
        screens[Screen.NewGameExit].Title = NewGameTitle(NewGameSession.StepOf(ng.ReturnTo), ng.CanGo);
    }

    // Обновить заголовки игровых экранов (метка нового в журнале, серая «Карта» без тактической карты).
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
                // Карта (местность) — только когда она есть; иначе вкладка серая, F1 не открывает её.
                ["F1"] = () => { if (hasLocation()) state.CurrentScreen = Screen.Map; },
                ["F2"] = () => state.CurrentScreen = Screen.World,
                ["F3"] = () => state.CurrentScreen = Screen.Journal,
                ["F4"] = () => state.CurrentScreen = Screen.Character,
                ["F5"] = () => state.CurrentScreen = Screen.Abilities,
                ["Esc"] = GoToMenu,
            }
        };

        // Заголовок игрового экрана: активная вкладка "  → ИМЯ ←", остальные "[Fn]ИМЯ  " — одинаковой
        // длины, поэтому названия не сдвигаются при переключении (MouseUiHelper.ComputeTitleTabs).
        string GameTitle(string activeKey) => GameTitleText(activeKey, journalUnread: false);

        // «Персонаж»: F10 (или кнопка на карточке) — окно «Новый уровень», когда опыта хватает и нет боя.
        ScreenConfig CharacterScreen()
        {
            var cfg = GameScreen(GameTitle("F4"));
            cfg.Commands["F10"] = () => { if (LevelUp.Available(storage.WorldState)) state.CurrentScreen = Screen.LevelUp; };
            return cfg;
        }

        // Шаг новой игры (F1–F4), если он уже доступен; Esc — выход в меню с предупреждением.
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
                ["F4"] = () => GoStep(4),   // шаг «Приключение», потом старт
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
            [Screen.Abilities]= GameScreen(GameTitle("F5")), // вкладка «Спутник» (пока заглушка)

            [Screen.Menu] = new ScreenConfig
            {
                Title = L.T(" МЕНЮ   [F1]ПРОДОЛЖИТЬ     [F2]НОВАЯ ИГРА     [Esc]ВЫЙТИ"),
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

            // Шаг «Приключение» мастера новой игры (после редактора героя): пожелания и место старта.
            // Шаги новой игры: свой экран у каждого, переходы — сами экраны (Program) и команды редактора.
            [Screen.NewGameAdventure] = new ScreenConfig { Title = NewGameTitle(4), Commands = { ["Start"] = StartGame } },
            [Screen.NewGame] = new ScreenConfig { Title = NewGameTitle(1) },
            [Screen.NewGameHero] = new ScreenConfig { Title = NewGameTitle(2) },
            [Screen.NewGameExit] = new ScreenConfig { Title = NewGameTitle(1) },

            // Редактор — шаг «РЕДАКТОР» новой игры: в заголовке только шаги (без вкладок персонаж/спутник).
            [Screen.NewGameCharacter] = EditorScreen(NewGameTitle(3)),
            [Screen.NewGameAbilities] = EditorScreen(NewGameTitle(3)),
        };
    }
}
