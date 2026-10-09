namespace NaviDnD;

public enum Screen
{
    Map,
    Journal,
    Character,
    LevelUp,      // окно «Новый уровень» поверх вкладки «Персонаж»
    Abilities,
    World,
    Menu,
    NewGame,
    NewGameCharacter,
    NewGameAbilities,
    NewGameAdventure,
    NewGameHero,
    NewGameExit
}

public class GameState
{
    public Screen CurrentScreen { get; set; } = Screen.Menu;

    public Screen LastGameScreen { get; set; } = Screen.Menu;

    public bool PendingStartNewGame { get; set; }

    // Место (Storage.SlotPath) для создаваемой игры — становится активным при старте мира.
    public int? NewGameSlot { get; set; }

    // Создаваемая игра (шаги 1–4) — до старта или выхода в меню.
    public NewGameSession? NewGame { get; set; }

}

// Создание новой игры по шагам (1 мир → 2 герой → 3 редактор → 4 приключение): заполненное живёт до старта игры
// или выхода в меню — по шагам можно ходить туда-обратно (F1–F4, клик по шагу в заголовке).
public class NewGameSession
{
    public Display.NewGameWizard? Wizard { get; set; }
    public Display.NewGameDisplay? Form { get; set; }
    public string? WorldId { get; set; }            // шаг 1 пройден
    public bool HeroReady { get; set; }             // шаг 2 пройден — герой создан
    public Display.NewGameWizard.Adventure? Adventure { get; set; }
    public Screen ReturnTo { get; set; } = Screen.NewGame;   // выход в меню отменён — вернуться сюда

    public bool CanGo(int step) => step <= 1 || (step == 2 ? WorldId != null : HeroReady);

    public static Screen StepScreen(int step) => step switch
    {
        1 => Screen.NewGame, 2 => Screen.NewGameHero, 3 => Screen.NewGameCharacter, _ => Screen.NewGameAdventure,
    };

    public static int StepOf(Screen screen) => screen switch
    {
        Screen.NewGame => 1, Screen.NewGameHero => 2, Screen.NewGameAdventure => 4, _ => 3,
    };
}

public class ScreenConfig
{
    public string Title { get; set; }
    public Dictionary<string, Action> Commands { get; set; } = new();
}

public class NewGameData
{
    public string Name { get; set; }
    public string Symbol { get; set; }
    public string? Race { get; set; }
    public string? Class { get; set; }
    public string? Level { get; set; }
    public string? Alignment { get; set; }
    public string? Background { get; set; }
    public string Description { get; set; }
    public string Setting { get; set; }
    public string SettingWish { get; set; }
    // Параметры приключения строкой («Жанр: …; Сложность: …; Темп: …») — в StartNewGame и narrative.style.
    public string? AdventureStyle { get; set; }
    // Выбраны игроком в анкете — переносятся в героя как есть (нейронка их не переподбирает).
    public List<int>? Color { get; set; }
    public string? Image { get; set; }
    // Мир игры: id мира библиотеки (продолжить его хронику) или null — новый мир размера WorldSize.
    public string? WorldId { get; set; }
    public string WorldSize { get; set; } = "small";
    // Шаг «Приключение»: место старта, выбранное игроком на карте мира (место хроники или клетка), — по желанию.
    public string? StartPlace { get; set; }
    public int? StartX { get; set; }
    public int? StartY { get; set; }
}
