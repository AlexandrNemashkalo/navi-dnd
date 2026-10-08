using NaviDnD.Data.Models;

namespace NaviDnD;

public class WorldState
{
    // Версия формата файла (StorageFormat): старые сохранения переписываются при обновлении игры.
    public int FormatVersion { get; set; } = StorageFormat.Current;
    // Язык игры (L.Russian/L.English) — язык мира при создании: рассказ, история, названия; L.World при загрузке.
    public string Language { get; set; } = L.Russian;
    public MapConfig Map { get; set; } = new MapConfig();
    public List<DialogMessage>? History { get; set; } = [];
    public Hero? Hero { get; set; }
    public Narrative? Narrative { get; set; }
    public GameTime Time { get; set; } = new GameTime();
    public List<ScheduledEvent>? ScheduledEvents { get; set; }
    public CombatState? Combat { get; set; }
    public List<WorldImage>? Images { get; set; }
    public string? ActiveImageKey { get; set; }
    // Мир игры: геометрия — файл библиотеки миров (Storage/Worlds/<id>), лор и открытые места — здесь (GameWorld).
    public GameWorldLink? World { get; set; }

    // Сводка путешествия, которую мастер не получил (сервер не ответил): путь уже пройден — она уходит мастеру
    // со следующим действием игрока, чтобы дорога, еда и встреча не потерялись.
    public string? PendingTravel { get; set; }
}

// Игра в мире: лор и места — общая хроника мира (WorldMap.Chronicle, переходит в другие игры этого мира);
// у игры — что знает её герой (Known — имена мест хроники; столицы известны всем), где он (клетка X,Y мира и
// место Place, если он в нём), где бывал (Visited).
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
    // Раунд с начала текущей части суток — ИИ сбрасывает на 1 при каждой смене PartOfDay
    // (см. Prompts/Dnd5e/SendAction systemPrompt.md § Время), поэтому не монотонен по всей игре.
    public int TotalRounds { get; set; } = 1;

    // Ключ PartsOfDay (night/morning/day/evening; см. Prompts/Dnd5e/SendAction systemPrompt.md § Время). Слово мастера
    // на любом языке Storage приводит к ключу; незнакомое остаётся как есть (днём для света и музыки).
    public string PartOfDay { get; set; } = PartsOfDay.Morning;

    // Номер игрового дня — ИИ инкрементирует сам при переходе цикла через ночь (см. промпт).
    public int Day { get; set; } = 1;

    // Часть суток для экрана — на языке интерфейса; незнакомое значение — как есть.
    public static string PartOfDayText(string? key) => L.T(PartsOfDay.Russian(key));
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
    // На стороне героя (дружелюбное существо вступило в бой): не бьёт героя атакой по возможности, враги — его цель.
    public bool? Ally { get; set; }
}
