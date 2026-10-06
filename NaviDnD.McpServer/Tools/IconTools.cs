using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using NaviDnD;

namespace NaviDnD.McpServer;

// Библиотека иконок game-icons.net на диске: icons/000000/ffffff/1x1/<author>/<name>.svg —
// см. NaviDnD/Helpers/SvgToBrailleConverter.cs, которая резолвит ТОТ ЖЕ слаг "author/name" в файл
// для рендера в браиль-арт. Список слагов кешируется один раз лениво на время жизни процесса.
[McpServerToolType]
public sealed class IconTools(McpServerConfig config)
{
    private static readonly string IconsBasePath =
        Path.Combine(AppConfig.AssetDirectory("icons"), "000000", "ffffff", "1x1");

    private static readonly Lazy<List<string>> _slugs = new(LoadSlugs);


    private static List<string> LoadSlugs()
    {
        if (!Directory.Exists(IconsBasePath)) return [];
        var result = new List<string>();
        foreach (var authorDir in Directory.GetDirectories(IconsBasePath))
        {
            string author = Path.GetFileName(authorDir);
            foreach (var file in Directory.GetFiles(authorDir, "*.svg"))
                result.Add($"{author}/{Path.GetFileNameWithoutExtension(file)}");
        }
        return result;
    }

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
            var terms = query.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (terms.Length == 0)
                return Error("empty query");

            foreach (var term in terms)
            {
                var slugs = SearchOne(term);
                if (slugs.Count == 0) continue;

                string hit = string.Join(",", slugs);
                Log($"find_icon ← matchedTerm:\"{term}\" ({slugs.Count} results): {hit}");
                return hit;
            }

            Log($"find_icon ← no term matched in \"{query}\"");
            return "";
        }
        catch (Exception ex)
        {
            Log($"find_icon ← (ERROR) {ex.Message}");
            return Error(ex.Message);
        }
    }

    private static List<string> SearchOne(string term)
    {
        string normalized = NormalizeForCompare(term);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];

        var scored = new List<(string slug, int score)>();
        foreach (var slug in _slugs.Value)
        {
            string name = slug[(slug.IndexOf('/') + 1)..];
            string normalizedName = NormalizeForCompare(name);
            var nameWords = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            int score = ScoreMatch(normalizedName, nameWords, words, normalized);
            if (score > 0) scored.Add((slug, score));
        }

        return scored
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.slug, StringComparer.Ordinal)
            .Take(20)
            .Select(x => x.slug)
            .ToList();
    }

    // "long-sword_01" → "long sword 01" — дефисы/подчёркивания как разделители слов, чтобы
    // "long sword" и "longsword" совпадали по словам, а не только по точной подстроке.
    private static string NormalizeForCompare(string s) =>
        s.ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');

    private static int ScoreMatch(string normalizedName, string[] nameWords, string[] queryWords, string queryNormalized)
    {
        if (normalizedName == queryNormalized) return 100;
        if (queryWords.All(nameWords.Contains)) return 80;
        if (normalizedName.StartsWith(queryNormalized, StringComparison.Ordinal)) return 60;
        if (queryWords.All(t => normalizedName.Contains(t, StringComparison.Ordinal))) return 40;
        return 0;
    }

    private static string Error(string message) => $"{{\"error\":\"{message}\"}}";

    private void Log(string message)
    {
        SearchToolLog.Write(config.LogPath, message);
    }
}
