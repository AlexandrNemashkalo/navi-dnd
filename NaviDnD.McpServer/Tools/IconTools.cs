using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using NaviDnD.Helpers;

namespace NaviDnD.McpServer;

// Библиотека иконок game-icons.net на диске: icons/000000/ffffff/1x1/<author>/<name>.svg —
// см. NaviDnD/Helpers/SvgToBrailleConverter.cs, которая резолвит ТОТ ЖЕ слаг "author/name" в файл
// для рендера в браиль-арт. Поиск и кеш слагов — CatalogSearch (им же игра разрешает "icon" в ответе мастера).
[McpServerToolType]
public sealed class IconTools(McpServerConfig config)
{
    [McpServerTool]
    [Description("""
        Ищет слаги иконок game-icons.net для поля "image". query = варианты через запятую от
        точного к общему ("longsword,sword") — пробует каждый по очереди, пока не найдётся
        совпадение; один вызов, не повторяй. Термины: 1-2 обычных английских существительных, тип
        предмета, а не художественное название ("sword", а не "Blade of Kings"). Общий набор
        иконок, не заточен под D&D — экзотические предметы может не найти.
        Возвращает список подходящих слагов через запятую (author/name) — выбери сам, какое
        название подходит лучше. Пустой результат → совпадений нет, пропусти "image", не угадывай.
        """)]
    public string FindIcon([Description("варианты через запятую от точного к общему, например \"longsword,sword\"")] string query)
    {
        Log($"find_icon → \"{query}\"");
        try
        {
            if (query.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length == 0)
                return Error("empty query");
            var (term, slugs) = CatalogSearch.IconsFor(query);
            if (term == null)
            {
                Log($"find_icon ← no term matched in \"{query}\"");
                return "";
            }
            string hit = string.Join(",", slugs);
            Log($"find_icon ← matchedTerm:\"{term}\" ({slugs.Count} results): {hit}");
            return hit;
        }
        catch (Exception ex)
        {
            Log($"find_icon ← (ERROR) {ex.Message}");
            return Error(ex.Message);
        }
    }

    private static string Error(string message) => $"{{\"error\":\"{message}\"}}";

    private void Log(string message)
    {
        SearchToolLog.Write(config.LogPath, message);
    }
}
