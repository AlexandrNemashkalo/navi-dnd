using NaviDnD.Data.Models;

namespace NaviDnD;

// Speaker IDs supported by the bundled v5_ru and v3_en models; no model-selected SSML voice.
public static class NpcVoices
{
    public static readonly string[] RussianMale = ["aidar", "eugene"];
    public static readonly string[] RussianFemale = ["baya", "kseniya", "xenia"];
    public static readonly string[] EnglishMale = ["en_57", "en_31", "en_20", "en_23"];
    public static readonly string[] EnglishFemale = ["en_21", "en_11", "en_6"];
    private static readonly string[] Pitches = ["low", "medium", "high"];

    public static void Assign(WorldState world)
    {
        if (world.Narrative?.Npcs == null) return;
        foreach (var npc in world.Narrative.Npcs.Where(n => n.Deleted != true && !string.IsNullOrWhiteSpace(n.Name)))
        {
            if (npc.SpeechVoice != null) continue;
            // Duplicate records with the same name share the first assigned voice.
            var existing = world.Narrative.Npcs.FirstOrDefault(n => n.SpeechVoice != null && string.Equals(n.Name?.Trim(),npc.Name.Trim(),StringComparison.OrdinalIgnoreCase));
            npc.SpeechVoice = existing?.SpeechVoice ?? Create(npc.Name, npc.Gender, world.Language);
        }
    }

    public static NpcSpeechVoice Create(string name, string? gender, string language)
    {
        bool en = language == "en";
        var male = en ? EnglishMale : RussianMale;
        var female = en ? EnglishFemale : RussianFemale;
        var pool = gender?.ToLowerInvariant() switch { "female" => female, "male" => male, _ => [.. male, .. female] };
        uint hash=2166136261;
        foreach(char c in name.Trim().ToUpperInvariant()) hash=unchecked((hash^c)*16777619);
        return new() { Language=en?"en":"ru", Speaker=pool[hash%(uint)pool.Length], Pitch=Pitches[(hash/17)%3] };
    }

    public static NpcSpeechVoice? Find(WorldState? world,string? author) => world?.Narrative?.Npcs
        .FirstOrDefault(n=>n.Deleted!=true && string.Equals(n.Name?.Trim(),author?.Trim(),StringComparison.OrdinalIgnoreCase))?.SpeechVoice;
}
