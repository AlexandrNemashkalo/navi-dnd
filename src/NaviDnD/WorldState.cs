using NaviDnD.Data.Models;

namespace NaviDnD;

public class WorldState
{
    // File format version (StorageFormat):) old saves are overwritten when updating the game.
    public int FormatVersion { get; set; } = StorageFormat.Current;
    // Game language (L.Russian/L.English) - language of the world when creating: story, history, names; L.World when loading.
    public string Language { get; set; } = L.Russian;
    // Picture pack of the game (CatalogSearch.GameIconsPack/DcssPack) - chosen in the hero questionnaire, like the
    // language it stays with the adventure. Saves before the choice - game-icons.
    public string IconPack { get; set; } = Helpers.CatalogSearch.GameIconsPack;
    public MapConfig Map { get; set; } = new MapConfig();
    public List<DialogMessage>? History { get; set; } = [];
    public Hero? Hero { get; set; }
    public Narrative? Narrative { get; set; }
    public GameTime Time { get; set; } = new GameTime();
    public List<ScheduledEvent>? ScheduledEvents { get; set; }
    public CombatState? Combat { get; set; }
    public List<WorldImage>? Images { get; set; }
    public string? ActiveImageKey { get; set; }
    // Game world: geometry - world library file (Storage/Worlds/<id>), lore and open areas - here (GameWorld).
    public GameWorldLink? World { get; set; }

    // Travel summary that the GM did not receive (the server did not respond): the path has already been
    // completed - it goes to the GM with the player’s next action, so that the road, food and meeting are not
    // lost.
    public string? PendingTravel { get; set; }
}

// Game in the world: lore and places - general chronicle of the world (WorldMap.Chronicle, goes into other
// games of this world); the game - what its hero knows (Known - names of places in the chronicle; capitals are
// known to everyone), where he is (cell X, Y of the world and place Place, if he is in it), where he has been
// (Visited).
public class GameWorldLink
{
    public string Id { get; set; } = "";
    public string? Place { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public List<string> Known { get; set; } = [];
    public List<string> Visited { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public int Version { get; set; }

    public bool Knows(string name) => Known.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public class WorldImage : IPatchable
{
    public bool? Deleted { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public string Content { get; set; } = "";
}

public class GameTime
{
    // Round from the beginning of the current part of the day - AI resets by 1 with each change of PartOfDay
    // (see Prompts/Dnd5e/SendAction systemPrompt.md § Time), so it is not monotonous throughout the game.
    public int TotalRounds { get; set; } = 1;

    // Key PartsOfDay (night/morning/day/evening; see Prompts/Dnd5e/SendAction systemPrompt.md § Time). The GM's
    // word in any language Storage leads to the key; the unfamiliar remains as it is (in the daytime for light
    // and music).
    public string PartOfDay { get; set; } = PartsOfDay.Morning;

    // Game day number - AI increments itself when the cycle passes through the night (see prompt).
    public int Day { get; set; } = 1;

    // Part of the day for the screen - in the interface language; unknown meaning - as is.
    public static string PartOfDayText(string? key) => L.T(PartsOfDay.Label(key));
}

public class CombatState
{
    public bool Active { get; set; }
    public string CurrentTurn { get; set; } = "";
    public List<InitiativeEntry>? Initiative { get; set; }
}

public class InitiativeEntry : IPatchable
{
    public bool? Deleted { get; set; }
    public string Symbol { get; set; } = "";
    public int Score { get; set; }
    // On the hero's side (a friendly creature entered the battle): does not hit the hero with an attack of opportunity, enemies are his target.
    public bool? Ally { get; set; }
}
