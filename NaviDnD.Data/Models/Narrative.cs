namespace NaviDnD.Data.Models;

public class Narrative
{
    /// <summary>Сеттинг: тон, атмосфера, описание мира</summary>
    public string World { get; set; } = "";

    /// <summary>Текущий квест / основная арка</summary>
    public string CurrentArc { get; set; } = "";

    /// <summary>Параметры приключения, выбранные игроком (жанр, упор, длина, сложность, темп) — мастер следует им всю игру</summary>
    public string Style { get; set; } = "";

    /// <summary>Активные и завершённые сюжетные нити</summary>
    public List<PlotThread> PlotThreads { get; set; } = [];

    /// <summary>Запомненные NPC: отношение и ключевые факты</summary>
    public List<NpcRecord> Npcs { get; set; } = [];

    /// <summary>
    /// Виды монстров, которых герой опознал (MonsterKey из map.entities) — по виду, не по конкретной
    /// сущности: один раз изучил гоблина, все гоблины дальше узнаваемы. Список целиком заменяется
    /// патчем (не PatchByIndex) — сюда никогда не пишут id/deleted.
    /// </summary>
    public List<string>? KnownMonsters { get; set; }
}

public class PlotThread : IPatchable
{
    public bool? Deleted { get; set; }

    public string Name { get; set; }

    /// <summary>"активна" | "раскрыта" | "завершена"</summary>
    public string Status { get; set; }

    /// <summary>Что о задании знает герой (показывается в журнале).</summary>
    public string Description { get; set; }

    /// <summary>true — герой о нити ещё не знает (заговор, будущий поворот): в журнале её нет.</summary>
    public bool? Hidden { get; set; }

    /// <summary>Заметки мастера: скрытая правда, планы на сюжет — только для ИИ, герою не показываются.</summary>
    public string? DmNotes { get; set; }

    /// <summary>
    /// Шаги по ходу задания («Узнал, что ключ у старосты») — журнал героя. Как npcs[i].memory: движок
    /// только дописывает новые строки (Storage.MergeInto).
    /// </summary>
    public List<string>? Steps { get; set; }
}

public class NpcRecord : IPatchable
{
    public bool? Deleted { get; set; }

    public string Name { get; set; }

    /// <summary>Отношение к герою: дружелюбный / нейтральный / враждебный / неизвестно</summary>
    public string Attitude { get; set; }

    /// <summary>Что герой знает о NPC: кто он, как выглядит, чем занят (показывается в журнале)</summary>
    public string Notes { get; set; }

    /// <summary>true — герой встретил NPC или знает о нём: только такие видны в журнале.</summary>
    public bool? Met { get; set; }

    /// <summary>Заметки мастера: истинные мотивы, тайны, планы NPC — только для ИИ, герою не показываются.</summary>
    public string? DmNotes { get; set; }

    /// <summary>
    /// Память о разговорах с героем: каждая строка — факт с явным автором («Нурс сказал: …»).
    /// Движок только дописывает новые строки (Storage.MergeInto), старые не теряются при перезаписи notes.
    /// </summary>
    public List<string>? Memory { get; set; }
}
