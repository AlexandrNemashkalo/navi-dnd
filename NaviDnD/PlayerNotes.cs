using System.Text;
using System.Text.Json;

namespace NaviDnD;

// Свои заметки игрока (журнал → [F10] ЗАМЕТКИ). Лежат ОТДЕЛЬНЫМ файлом рядом с игрой
// (game{N}.notes.json), не в WorldState: ни контекст ИИ (AiContextBuilder), ни MCP-сервер (читает только
// файл игры) их не видят и увидеть не могут. Добавляются и удаляются без нейронки; мастер может только
// дописать запись (ключ ответа "playerNote", Storage) — прочитать их он не может.
public static class PlayerNotes
{
    public sealed class Note
    {
        public string Text { get; set; } = "";
        public int Day { get; set; }
        public string? PartOfDay { get; set; }
        public DateTime Created { get; set; }
        public bool ByMaster { get; set; } // запись добавил мастер (ИИ) — сам он заметки не читает
    }

    // Файл: { formatVersion, notes: [...] } (StorageFormat; прежний голый массив переписывает миграция).
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

    // Заметки текущей игры (перечитываются при смене игры).
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
                // Не прочитался (повреждён, не переведён миграцией) — копия рядом, чтобы следующая запись не стёрла заметки.
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
        catch { /* не сохранилось — заметки живут до конца сеанса */ }
    }
}
