using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using NaviDnD.Helpers;

namespace NaviDnD.McpServer;

// Справочник заклинаний DnD5e (NaviDnD.Helpers.SpellDatabase, GameData/DnD5e_spells_BD.dtn) — до
// этого тула ИИ сам придумывал русское название и описание заклинания, из-за чего имя у героя
// (hero.spells[].name) часто не совпадало с каноничным русским названием в базе, и карточка
// заклинания (MouseUiHelper.SetSelectedSpell) не находила данные по имени — показывала только
// круг, без школы/времени/дистанции/текста. Поиск — по АНГЛИЙСКОМУ названию (ИИ обычно надёжно
// помнит его из обучения, в отличие от точного русского перевода), результат — русское имя,
// которое нужно использовать как есть.
[McpServerToolType]
public sealed class SpellTools(McpServerConfig config)
{
    private static readonly object _logLock = new();

    [McpServerTool]
    [Description("""
        Ищет заклинание D&D 5e в справочнике по АНГЛИЙСКОМУ названию — НЕ выдумывай и не описывай
        заклинание сам, сначала найди его здесь. query = английское название или его часть
        ("fireball", "cure wounds"). level = круг заклинания (0 = заговор) — если указан, в
        результат попадут ТОЛЬКО заклинания этого круга; не уверены — не указывайте.
        Возвращает через запятую названия совпадений на языке игры (lang) по убыванию релевантности —
        возьмите лучшее и используйте БУКВАЛЬНО как hero.spells[].name, не меняя ни буквы. Пустая строка —
        ничего не нашлось (в этом круге, если он указан): только тогда можно придумать собственное
        (гомбрю) заклинание и его описание.
        """)]
    public string FindSpell(
        string query,
        [Description("Круг заклинания (0 = заговор) — жёсткий фильтр, не обязателен")] int? level = null)
    {
        Log($"find_spell → \"{query}\" level={level?.ToString() ?? "?"}");
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return "";

            var normalizedQuery = NormalizeForCompare(query);
            var queryWords = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (queryWords.Length == 0)
                return "";

            var scored = new List<(SpellInfo spell, int score)>();
            foreach (var spell in SpellDatabase.AllSpells)
            {
                if (string.IsNullOrEmpty(spell.NameEn)) continue;
                if (level.HasValue && spell.Level != level.Value) continue;

                string normalizedName = NormalizeForCompare(spell.NameEn);
                var nameWords = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                int score = ScoreMatch(normalizedName, nameWords, queryWords, normalizedQuery);
                if (score <= 0) continue;
                scored.Add((spell, score));
            }

            var names = scored
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.spell.Name, StringComparer.Ordinal)
                .Take(15)
                .Select(x => x.spell.Localized.Name)
                .ToList();

            string result = string.Join(", ", names);
            Log($"find_spell ← {names.Count} results: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Log($"find_spell ← (ERROR) {ex.Message}");
            return "";
        }
    }

    private static string NormalizeForCompare(string s) =>
        s.ToLowerInvariant().Replace('-', ' ').Replace('\'', ' ').Replace(',', ' ');

    private static int ScoreMatch(string normalizedName, string[] nameWords, string[] queryWords, string queryNormalized)
    {
        if (normalizedName == queryNormalized) return 100;
        if (queryWords.All(nameWords.Contains)) return 80;
        if (normalizedName.StartsWith(queryNormalized, StringComparison.Ordinal)) return 60;
        if (queryWords.All(t => normalizedName.Contains(t, StringComparison.Ordinal))) return 40;
        return 0;
    }

    private void Log(string message)
    {
        if (config.LogPath is null) return;
        lock (_logLock)
            File.AppendAllText(config.LogPath, $"  MCP {message}\n", new UTF8Encoding(false));
    }
}
