namespace NaviDnD.Data.Models;

public class Narrative
{
    /// <summary>Setting: tone, atmosphere, description of the world</summary>
    public string World { get; set; } = "";

    /// <summary>Current quest/main arc</summary>
    public string CurrentArc { get; set; } = "";

    /// <summary>Adventure parameters chosen by the player (genre, emphasis, length, difficulty, pace) - the GM follows them throughout the game</summary>
    public string Style { get; set; } = "";

    /// <summary>Active and completed story threads</summary>
    public List<PlotThread> PlotThreads { get; set; } = [];

    /// <summary>Stored NPC: attitude and key facts</summary>
    public List<NpcRecord> Npcs { get; set; } = [];

    /// <summary>
    ///Types of monsters that the hero identified (MonsterKey from map.entities) - by type, not by specific
    ///entities: once studied a goblin, all goblins are further recognizable. The entire list is replaced
    ///patch (not PatchByIndex) - they never write id/deleted. here
    /// </summary>
    public List<string>? KnownMonsters { get; set; }
}

public class PlotThread : IPatchable
{
    public bool? Deleted { get; set; }

    public string Name { get; set; }

    /// <summary>"active" | "revealed" | "completed"</summary>
    public string Status { get; set; }

    /// <summary>What the hero knows about the task (shown in the journal).</summary>
    public string Description { get; set; }

    /// <summary>true - the hero does not yet know about the thread (conspiracy, future twist): it is not in the magazine.</summary>
    public bool? Hidden { get; set; }

    /// <summary>GM's notes: hidden truth, plot plans - only for AI, not shown to the hero.</summary>
    public string? DmNotes { get; set; }

    /// <summary>
    ///Steps during the quest (“I found out that the headman has the key”) - hero’s journal. Like npcs[i].memory: engine
    ///only appends new lines (Storage.MergeInto).
    /// </summary>
    public List<string>? Steps { get; set; }
}

public class NpcRecord : IPatchable
{
    public bool? Deleted { get; set; }

    public string Name { get; set; }

    // Stable keys; gender is supplied as a fact, voice and pitch are assigned only by the engine.
    public string? Gender { get; set; } // male | female | null
    public NpcSpeechVoice? SpeechVoice { get; set; }

    /// <summary>Attitude towards the hero: friendly / neutral / hostile / unknown</summary>
    public string Attitude { get; set; }

    /// <summary>What the hero knows about NPC: who he is, what he looks like, what he does (shown in the magazine)</summary>
    public string Notes { get; set; }

    /// <summary>true - the hero met NPC or knows about him: only such ones are visible in the magazine.</summary>
    public bool? Met { get; set; }

    /// <summary>GM's notes: true motives, secrets, plans of NPC - only for AI, not shown to the hero.</summary>
    public string? DmNotes { get; set; }

    /// <summary>
    ///Memory of conversations with the hero: each line is a fact with an obvious author (“Nurse said: …”).
    ///The engine only adds new lines (Storage.MergeInto), old ones are not lost when overwriting notes.
    /// </summary>
    public List<string>? Memory { get; set; }
}

public class NpcSpeechVoice
{
    public string Language { get; set; } = "ru";
    public string Speaker { get; set; } = "eugene";
    public string Pitch { get; set; } = "medium";
}
