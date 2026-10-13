namespace NaviDnD.Helpers;

// Поиск по справочникам игры по английскому названию: снаряжение, заклинания, монстры, иконки game-icons.net.
// Общий для MCP-инструментов (find_item/find_spell/find_monster/find_icon) и для ссылок в ответе мастера
// (CatalogReferences): одинаковое совпадение — одинаковый результат.
public static class CatalogSearch
{
    private static readonly string IconsBasePath = Path.Combine(AppConfig.AssetDirectory("icons"), "000000", "ffffff", "1x1");
    private static readonly Lazy<List<string>> IconSlugs = new(LoadIconSlugs);

    // "long-sword_01" → "long sword 01": дефисы, подчёркивания, апострофы и запятые — разделители слов.
    public static string Normalize(string s) =>
        s.ToLowerInvariant().Replace('-', ' ').Replace('_', ' ').Replace('\'', ' ').Replace(',', ' ');

    // 100 — то же название, 80 — все слова запроса есть в названии, 60 — название начинается с запроса,
    // 40 — все слова запроса встречаются внутри названия, 0 — не подходит.
    public static int Score(string name, string query)
    {
        string normalizedName = Normalize(name), normalizedQuery = Normalize(query);
        var nameWords = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var queryWords = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryWords.Length == 0) return 0;
        normalizedQuery = string.Join(' ', queryWords);
        normalizedName = string.Join(' ', nameWords);
        if (normalizedName == normalizedQuery) return 100;
        if (queryWords.All(nameWords.Contains)) return 80;
        if (normalizedName.StartsWith(normalizedQuery, StringComparison.Ordinal)) return 60;
        if (queryWords.All(t => normalizedName.Contains(t, StringComparison.Ordinal))) return 40;
        return 0;
    }

    // Совпадения по убыванию: score, затем по ключу сортировки (стабильный порядок при равенстве).
    public static List<T> Find<T>(IEnumerable<T> items, Func<T, string?> englishName, Func<T, string> order, string query, int take)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return items
            .Select(item => (item, score: englishName(item) is { Length: > 0 } name ? Score(name, query) : 0))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => order(x.item), StringComparer.Ordinal)
            .Take(take)
            .Select(x => x.item)
            .ToList();
    }

    public static List<EquipmentInfo> Items(string query, int take = 8) =>
        Find(EquipmentDatabase.AllItems, i => i.NameEn, i => i.Name, query, take);

    public static List<SpellInfo> Spells(string query, int? level = null, int take = 15) =>
        Find(SpellDatabase.AllSpells.Where(s => level == null || s.Level == level), s => s.NameEn, s => s.Name, query, take);

    public static List<MonsterInfo> Monsters(string query, int take = 15) =>
        Find(MonsterDatabase.AllMonsters, m => m.NameEn, m => m.Name, query, take);

    // Слаги иконок для одного термина ("longsword").
    public static List<string> Icons(string term, int take = 20) =>
        Find(IconSlugs.Value, slug => slug[(slug.IndexOf('/') + 1)..], slug => slug, term, take);

    // Варианты через запятую от точного к общему ("longsword,sword"): слаги первого термина, у которого они есть.
    public static (string? Term, List<string> Slugs) IconsFor(string query)
    {
        foreach (string term in query.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Icons(term) is { Count: > 0 } slugs) return (term, slugs);
        return (null, []);
    }

    private static List<string> LoadIconSlugs()
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
}
