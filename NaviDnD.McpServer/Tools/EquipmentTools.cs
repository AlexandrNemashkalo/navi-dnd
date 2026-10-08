using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using NaviDnD.Helpers;

namespace NaviDnD.McpServer;

// Справочник снаряжения DnD5e (NaviDnD.Helpers.EquipmentDatabase, GameData/DnD5e_equipment_BD.dtn +
// DnD5e_magic_equipment_BD.dtn) — по аналогии с SpellTools.FindSpell: ИИ ищет по АНГЛИЙСКОМУ
// названию при генерации инвентаря/лута, чтобы не путать похожие предметы (например Dart 1d4 vs
// Javelin 1d6 — оба "метательное", легко перепутать по памяти). В отличие от заклинаний тут нет
// отдельной карточки-справочника в игре — hero.inventory[].description ИИ пишет сам один раз,
// поэтому тул возвращает не только каноничное русское имя, но и саму механику (урон/КД/свойства/
// вес/цена), перевод в короткую русскую строку description — на ИИ, как обычно.
[McpServerToolType]
public sealed class EquipmentTools(McpServerConfig config)
{
    private static readonly object _logLock = new();

    [McpServerTool]
    [Description("""
        Ищет снаряжение D&D 5e (обычное И магическое) в справочнике по АНГЛИЙСКОМУ названию — НЕ
        выдумывай механику похожего предмета по памяти, сначала найди точный здесь. query =
        английское название или его часть ("javelin", "flame tongue").
        Возвращает по одной строке на совпадение (по убыванию релевантности): имя на языке игры (lang) —
        бери БУКВАЛЬНО как inventory[].name — и механика (урон/тип, свойства, КД, вес, цена, для
        магических — редкость/настройка/эффект). Перепиши механику в короткую строку description
        на языке игры сам, как и остальной текст. Пустой результат — предмета нет в справочнике:
        тогда придумай сам, ничего не ищи повторно.
        """)]
    public string FindItem(string query)
    {
        Log($"find_item → \"{query}\"");
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return "";

            var normalizedQuery = NormalizeForCompare(query);
            var queryWords = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (queryWords.Length == 0)
                return "";

            var scored = new List<(EquipmentInfo item, int score)>();
            foreach (var item in EquipmentDatabase.AllItems)
            {
                if (string.IsNullOrEmpty(item.NameEn)) continue;
                string normalizedName = NormalizeForCompare(item.NameEn);
                var nameWords = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                int score = ScoreMatch(normalizedName, nameWords, queryWords, normalizedQuery);
                if (score <= 0) continue;
                scored.Add((item, score));
            }

            var lines = scored
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.item.Name, StringComparer.Ordinal)
                .Take(8)
                .Select(x => Summarize(x.item))
                .ToList();

            string result = string.Join("\n", lines);
            Log($"find_item ← {lines.Count} results");
            return result;
        }
        catch (Exception ex)
        {
            Log($"find_item ← (ERROR) {ex.Message}");
            return "";
        }
    }

    private static string Summarize(EquipmentInfo i)
    {
        var parts = new List<string>();
        if (i.DamageVal.Length > 0) parts.Add($"{i.DamageVal} {i.DamageType}".Trim());
        if (i.Props.Count > 0) parts.Add(string.Join(", ", i.Props));
        if (i.Ac.Length > 0) parts.Add($"AC {i.Ac}");
        if (i.Weight.Length > 0) parts.Add($"вес {i.Weight}");
        if (i.Cost.Length > 0) parts.Add($"цена {i.Cost}");
        if (i.Rarity.Length > 0) parts.Add($"редкость: {i.Rarity}");
        if (i.Category == "магическое")
            parts.Add(i.Attunement.Length > 0 ? $"настройка: {i.Attunement}" : "настройка: не требуется");
        if (i.Text.Length > 0) parts.Add(i.Text);

        string type = i.TypeAdditions.Length > 0 ? $"{i.Type} {i.TypeAdditions}" : i.Type;
        return $"{i.LocalizedName}{(L.WorldIsEnglish ? "" : $" ({i.NameEn})")} [{i.Category}, {type}]: {string.Join("; ", parts)}";
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
