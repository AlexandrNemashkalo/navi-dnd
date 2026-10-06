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
                    ? JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(CurrentPath, Encoding.UTF8)) ?? []
                    : [];
            }
            catch { _notes = []; }
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
            File.WriteAllText(CurrentPath, JsonSerializer.Serialize(All, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }
        catch { /* не сохранилось — заметки живут до конца сеанса */ }
    }
}
