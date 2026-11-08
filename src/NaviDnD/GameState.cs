namespace NaviDnD;

public enum Screen
{
    Map,
    Journal,
    Character,
    LevelUp,      // New Level window on top of Character tab
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

    // Location (Storage.SlotPath) for the game being created - becomes active when the world starts.
    public int? NewGameSlot { get; set; }

    // The game being created (steps 1–4) - before starting or exiting to the menu.
    public NewGameSession? NewGame { get; set; }

}

// Creating a new game step by step (world 1 → hero 2 → editor 3 → adventure 4): the completed one lives until
// the game starts or exits to the menu - you can go back and forth step by step (F1–F4, click on the step in
// the title).
public class NewGameSession
{
    public Display.NewGameWizard? Wizard { get; set; }
    public Display.NewGameDisplay? Form { get; set; }
    public string? WorldId { get; set; }            // step 1 completed
    public bool HeroReady { get; set; }             // step 2 completed - hero created
    public Display.NewGameWizard.Adventure? Adventure { get; set; }
    public Screen ReturnTo { get; set; } = Screen.NewGame;   // menu exit canceled - return here

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
    // Adventure parameters by string (“Genre: ...; Difficulty: ...; Pace: ...”) - in StartNewGame and narrative.style.
    public string? AdventureStyle { get; set; }
    // Selected by the player in the profile - they are transferred to the hero as is (the AI does not reselect them).
    public List<int>? Color { get; set; }
    public string? Image { get; set; }
    // Picture pack of the adventure (CatalogSearch.GameIconsPack/DcssPack) - the questionnaire field "PICTURES".
    public string? IconPack { get; set; }
    // Gender of the hero from the questionnaire: "male" / "female"; null - unknown (hero from the library).
    public string? Gender { get; set; }
    // Game world: id of the library world (continue its chronicle) or null - a new world of size WorldSize.
    public string? WorldId { get; set; }
    public string WorldSize { get; set; } = "small";
    // Step “Adventure”: the starting location chosen by the player on the world map (chronicle location or cell), optional.
    public string? StartPlace { get; set; }
    public int? StartX { get; set; }
    public int? StartY { get; set; }
}
