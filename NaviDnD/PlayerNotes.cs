using System.Text;
using System.Text.Json;

namespace NaviDnD;

// Your player notes (journal → [F10] NOTES). They lie in a SEPARATE file next to the game (game{N}.notes.json),
// not in WorldState:; neither the AI context (AiContextBuilder), nor the MCP- server (reads only the game
// file)) see them and cannot be seen. They are added and deleted without a AI; the GM can only add an entry
// (answer key "playerNote", Storage) - he cannot read them.
public static class PlayerNotes
{
    public sealed class Note
    {
        public string Text { get; set; } = "";
        public int Day { get; set; }
        public string? PartOfDay { get; set; }
        public DateTime Created { get; set; }
        public bool ByMaster { get; set; } // the entry was added by the GM (AI) - he himself does not read the notes
    }

    // File: { formatVersion, notes: [...] } (StorageFormat; the former bare array is rewritten by migration).
    public sealed class NotesFile
    {
        public int FormatVersion { get; set; } = StorageFormat.Current;
        public List<Note> Notes { get; set; } = [];
    }

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    private static List<Note>? _notes;
    private static string? _loadedFor;

    public static string PathFor(string savePath) => Path.ChangeExtension(savePath, ".notes.json");

    private static string CurrentPath => PathFor(Storage.SavePath);

    // Notes for the current game (re-read when changing games).
    public static List<Note> All
    {
        get
        {
            if (_notes != null && _loadedFor == CurrentPath) return _notes;
            _loadedFor = CurrentPath;
            try
            {
                _notes = File.Exists(CurrentPath)
                    ? JsonSerializer.Deserialize<NotesFile>(File.ReadAllText(CurrentPath, Encoding.UTF8), _json)?.Notes ?? []
                    : [];
            }
            catch
            {
                // Unreadable (damaged, not translated by migration) - a copy is nearby so that the next entry does not erase the notes.
                _notes = [];
                try { File.Copy(CurrentPath, CurrentPath + ".unreadable", overwrite: false); } catch { }
            }
            return _notes;
        }
    }

    public static void Add(string text, int day, string? partOfDay, bool byMaster = false)
    {
        All.Add(new Note { Text = text.Trim(), Day = day, PartOfDay = partOfDay, Created = DateTime.Now, ByMaster = byMaster });
        Save();
    }

    public static void RemoveAt(int index)
    {
        if (index < 0 || index >= All.Count) return;
        All.RemoveAt(index);
        Save();
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(CurrentPath, JsonSerializer.Serialize(new NotesFile { Notes = All }, _json), Encoding.UTF8);
        }
        catch { /*not saved - notes live until the end of the session*/ }
    }
}
