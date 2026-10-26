namespace NaviDnD.Helpers;

// Search in game reference books by English name: equipment, spells, monsters, icons (game-icons.net or Dungeon
// Crawl tiles - IconPack setting). Common for MCP tools (find_item/find_spell/find_monster/find_icon) and for
// links in the wizard's answer (CatalogReferences): same match - same result.
public static class CatalogSearch
{
    private static readonly string IconsBasePath = Path.Combine(AppConfig.AssetDirectory("icons"), "000000", "ffffff", "1x1");
    private static readonly Lazy<List<string>> IconSlugs = new(LoadIconSlugs);
    private static readonly Lazy<List<DcssTile>> DcssTiles = new(LoadDcssTiles);

    // Picture pack of the current game (WorldState.IconPack) - new pictures are searched in it. Slugs of both packs
    // are drawn regardless of it: the slug itself says where the file is.
    public const string GameIconsPack = "game-icons", DcssPack = "dcss";
    public static string IconPack { get; set; } = GameIconsPack;
    public static string NormalizePack(string? pack) => pack == DcssPack ? DcssPack : GameIconsPack;

    // Dungeon Crawl Stone Soup 32×32 tiles (opengameart.org/node/77499, CC0): icons/dcss/<folder>/<name>.png, slug
    // "dcss/<folder>/<name>". Not in the search: paper-doll layers of the player, overlays, tentacle segments,
    // numbers and blood - parts of a picture, not a picture.
    public const string DcssPrefix = "dcss/";
    public static string DcssBasePath => Path.Combine(AppConfig.AssetDirectory("icons"), "dcss");
    private static readonly string[] DcssSkipped = ["player/", "monster/tentacles/", "misc/", "gui/tabs/"];
    private static readonly string[] DcssKeptPlayer = ["player/base/", "player/transform/", "player/felids/"];

    // "long-sword_01" → "long sword 01": hyphens, underscores, apostrophes and commas are word separators.
    public static string Normalize(string s) =>
        s.ToLowerInvariant().Replace('-', ' ').Replace('_', ' ').Replace('\'', ' ').Replace(',', ' ');

    // 100 - the same name, 80 - all query words are in the title, 60 - the name begins with the query, 40 - all
    // query words are found inside the title, 0 - does not match.
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

    // Matches in descending order: score, then by sort key (stable order for equality).
    public static List<T> Find<T>(IEnumerable<T> items, Func<T, string?> englishName, Func<T, string> order, string query, int take, int minScore = 1)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return items
            .Select(item => (item, score: englishName(item) is { Length: > 0 } name ? Score(name, query) : 0))
            .Where(x => x.score >= minScore)
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

    // Icon slugs for one term ("longsword") from the pack (by default - of the current game); category - only in
    // its folders of Dungeon Crawl (game-icons has no folders by kind).
    // minScore - the weakest match accepted (Score): single words of a query - not inside another word.
    public static List<string> Icons(string term, int take = 20, string? pack = null, string? category = null, int minScore = 1) => (pack ?? IconPack) == DcssPack
        ? DcssIcons(term, take, category != null ? DcssCategories.GetValueOrDefault(category) : null, minScore)
        : Find(IconSlugs.Value, slug => slug[(slug.IndexOf('/') + 1)..], slug => slug, term, take, minScore);

    // Kinds of pictures for the GM: "icon":"weapon: longsword,sword" - the search goes only among weapons (otherwise
    // "sword" finds a statue or a spell icon). The list is the same in the prompts.
    public static readonly Dictionary<string, string[]> DcssCategories = new()
    {
        ["creature"] = ["monster/", "player/base/", "player/transform/"],
        ["weapon"] = ["item/weapon/"], ["armor"] = ["item/armor/"], ["shield"] = ["item/armor/shields/"],
        ["helmet"] = ["item/armor/headgear/"], ["cloak"] = ["item/armor/back/"], ["boots"] = ["item/armor/feet/"],
        ["gloves"] = ["item/armor/hands/"], ["potion"] = ["item/potion/"], ["scroll"] = ["item/scroll/"],
        ["ring"] = ["item/ring/"], ["amulet"] = ["item/amulet/"], ["wand"] = ["item/wand/"],
        ["staff"] = ["item/staff/", "item/rod/"], ["book"] = ["item/book/"], ["food"] = ["item/food/"],
        ["treasure"] = ["item/gold/", "item/misc/"], ["item"] = ["item/"], ["feature"] = ["dungeon/"],
        ["spell"] = ["gui/spells/", "effect/"],
    };

    // Categories whose name is itself a picture ("potion", "ring") - the last fallback term of the query.
    private static readonly HashSet<string> GenericCategories = ["creature", "item", "feature", "spell", "treasure"];

    // By the tile name ("orc_warrior_old" → "orc warrior") or its folders: rings are named by material
    // ("item/ring/agate"), and "ring" is found only in the folder. With an equal match - the order of the loaded
    // list (LoadDcssTiles): ring before the spell "ring_of_flames".
    private static List<string> DcssIcons(string term, int take, string[]? folders = null, int minScore = 1)
    {
        if (string.IsNullOrWhiteSpace(term)) return [];
        return DcssTiles.Value
            .Where(tile => folders == null || folders.Any(f => tile.Slug.AsSpan(DcssPrefix.Length).StartsWith(f)))
            .Select(tile => (tile.Slug, score: Math.Max(Score(tile.Name, term), Score(tile.Path, term)) / (tile.Fallback ? 2 : 1)))
            .Where(x => x.score >= minScore)
            .OrderByDescending(x => x.score)
            .Take(take)
            .Select(x => x.Slug)
            .ToList();
    }

    // Fallback - spell and interface icons, effects: only when there are no creatures, items or dungeon
    // ("sword" - a weapon, not the spell component "gui/spells/components/sword").
    private sealed record DcssTile(string Slug, string Name, string Path, bool Fallback);
    private static readonly string[] DcssFallbackFolders = ["gui/", "effect/"];

    // The GM asks for pictures of creatures and items: they go before the walls, spell icons and interface
    // ("potion" - a potion, not the god's ability "gozag_potion_petition").
    private static readonly string[] DcssFolderOrder = ["monster/", "item/", "player/", "dungeon/", "emissaries/", "effect/", "gui/"];

    // Comma-separated variants from exact to general ("longsword,sword"): slugs of the first term that has them.
    // A known category before the colon ("weapon: longsword,sword") narrows the search, the category word itself
    // ("potion") is a late variant; an unknown one is ignored. A picture is found almost always, steps:
    //  1. the terms as is (Dungeon Crawl: then any tile of a look-named kind - potion, ring; then the same terms in
    //     game-icons: the pack has no everyday gear - pouch, clothes, incense);
    //  2. the singular ("wolves" → "wolf") and single words of the terms, longest first ("leather pouch" → "leather",
    //     "pouch"), then the category word ("weapon");
    //  A match - only a whole word or the beginning of a name, not a piece inside another word ("lute" is not "flute").
    //  3. defaultCategory != null (the engine resolving "icon" of the GM, not the MCP tools - for them an empty
    //     result is honest): the default picture of the category, of the query or this one (a bag for an item, a
    //     person for a creature).
    public static (string? Term, List<string> Slugs) IconsFor(string query, string? pack = null, string? defaultCategory = null)
    {
        string? category = null;
        int colon = query.IndexOf(':');
        if (colon >= 0)
        {
            string prefix = query[..colon].Trim().ToLowerInvariant();
            if (DcssCategories.ContainsKey(prefix)) category = prefix;
            query = query[(colon + 1)..];
        }
        var terms = query.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (terms.Count == 0) return (null, []);
        bool dcss = (pack ?? IconPack) == DcssPack;
        string? categoryTerm = category != null && !GenericCategories.Contains(category) && !terms.Contains(category) ? category : null;
        bool lookNamed = categoryTerm != null && LookNamedCategories.Contains(categoryTerm);
        string[] packs = dcss ? [DcssPack, GameIconsPack] : [GameIconsPack];
        var steps = (dcss ? terms.SelectMany(t => DcssAliases.TryGetValue(Normalize(t).Trim(), out var alias) ? [alias, t] : new[] { t }) : terms)
                .Select(t => (t, packs[0]))
            .Concat(lookNamed ? [(categoryTerm!, packs[0])] : [])
            .Concat(dcss ? terms.Select(t => (t, GameIconsPack)) : [])
            .Concat(LooseWords(terms).SelectMany(w => packs.Select(p => (w, p))))
            // game-icons has no folders: the word "food" there is a food truck - the default picture is more accurate.
            .Concat(dcss && categoryTerm != null && !lookNamed ? [(categoryTerm, DcssPack)] : []);
        foreach (var (term, stepPack) in steps)
            if (Icons(term, pack: stepPack, category: category, minScore: 60) is { Count: > 0 } slugs) return (term, slugs);

        if (defaultCategory == null) return (null, []);
        var defaults = dcss ? DcssDefaults : GameIconsDefaults;
        return ("default", [defaults.GetValueOrDefault(category ?? defaultCategory) ?? defaults.GetValueOrDefault(defaultCategory) ?? defaults["item"]]);
    }

    // Singulars, then single words (longest first) of the terms; without filler words and terms already tried.
    private static IEnumerable<string> LooseWords(List<string> terms)
    {
        var words = new List<string>();
        foreach (string term in terms)
            words.AddRange(Singulars(Normalize(term).Trim()));
        foreach (string word in terms.SelectMany(t => Normalize(t).Split(' ', StringSplitOptions.RemoveEmptyEntries)).OrderByDescending(w => w.Length))
        {
            if (word.Length < 3 || FillerWords.Contains(word)) continue;
            words.Add(word);
            words.AddRange(Singulars(word));
        }
        var tried = terms.Select(t => Normalize(t).Trim()).ToHashSet();
        return words.Where(tried.Add);
    }

    // "wolves" → "wolf", "berries" → "berry", "boxes" → "box", "coins" → "coin".
    private static IEnumerable<string> Singulars(string word)
    {
        if (word.Length <= 3 || word.EndsWith("ss")) yield break;
        if (word.EndsWith("ves")) yield return word[..^3] + "f";
        if (word.EndsWith("ies")) yield return word[..^3] + "y";
        if (word.EndsWith('s')) yield return word[..^1];
        if (word.EndsWith("es")) yield return word[..^2];
    }

    // Dungeon Crawl names some common creatures by a kind ("skeleton_bat" is a bat): the ordinary one is preferred.
    private static readonly Dictionary<string, string> DcssAliases = new()
    {
        ["skeleton"] = "skeleton humanoid", ["skeletons"] = "skeleton humanoid",
        ["zombie"] = "zombie small", ["zombies"] = "zombie small",
    };

    private static readonly HashSet<string> FillerWords =
        ["the", "and", "with", "small", "large", "big", "little", "old", "new", "simple", "common", "plain", "fine", "rusty", "worn"];

    // Default pictures of the categories: the last step of IconsFor.
    private static readonly Dictionary<string, string> DcssDefaults = new()
    {
        ["creature"] = "dcss/monster/human_new", ["item"] = "dcss/item/misc/misc_box", ["treasure"] = "dcss/item/gold/gold_pile",
        ["feature"] = "dcss/dungeon/chest", ["spell"] = "dcss/item/book/book_indigo", ["food"] = "dcss/item/food/bread_ration_new",
    };
    private static readonly Dictionary<string, string> GameIconsDefaults = new()
    {
        ["creature"] = "delapouite/person", ["item"] = "lorc/swap-bag", ["treasure"] = "delapouite/two-coins",
        ["feature"] = "delapouite/chest", ["spell"] = "delapouite/spell-book", ["food"] = "delapouite/bread",
        ["weapon"] = "lorc/broadsword", ["armor"] = "delapouite/leather-armor", ["shield"] = "willdabeast/round-shield",
        ["helmet"] = "lorc/visored-helm", ["cloak"] = "delapouite/cape", ["boots"] = "lorc/boots", ["gloves"] = "delapouite/gloves",
        ["potion"] = "lorc/potion-ball", ["scroll"] = "lorc/scroll-unfurled", ["ring"] = "delapouite/ring",
        ["amulet"] = "lorc/gem-pendant", ["wand"] = "lorc/crystal-wand", ["staff"] = "lorc/wizard-staff", ["book"] = "lorc/open-book",
    };

    // For the test: all default pictures exist.
    public static IEnumerable<string> DefaultIcons => DcssDefaults.Values.Concat(GameIconsDefaults.Values);

    private static readonly HashSet<string> LookNamedCategories = ["potion", "scroll", "ring", "amulet", "wand", "book"];

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

    // Sorted: folder (DcssFolderOrder), then the short name ("skeleton" before "skeleton_humanoid_large"), then slug.
    // Name: "orc_warrior_old" → "orc_warrior", "long_sword_1_new" → "long_sword" - old/new art versions and
    // numbered variants are the same thing.
    private static List<DcssTile> LoadDcssTiles()
    {
        string root = DcssBasePath;
        if (!Directory.Exists(root)) return [];
        var result = new List<(DcssTile Tile, int Rank)>();
        foreach (var file in Directory.GetFiles(root, "*.png", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/')[..^".png".Length];
            if (DcssSkipped.Any(relative.StartsWith) && !DcssKeptPlayer.Any(relative.StartsWith)) continue;
            string fileName = relative[(relative.LastIndexOf('/') + 1)..];
            if (fileName.StartsWith("overlay_")) continue;   // statue/monster overlay layers
            string name = System.Text.RegularExpressions.Regex.Replace(fileName, @"(_(old|new)|_?\d+)+$", "");
            int rank = Array.FindIndex(DcssFolderOrder, relative.StartsWith);
            result.Add((new DcssTile(DcssPrefix + relative, name.Length > 0 ? name : fileName, relative.Replace('/', ' '),
                    DcssFallbackFolders.Any(relative.StartsWith)),
                rank < 0 ? DcssFolderOrder.Length : rank));
        }
        return result
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Tile.Name.Length)
            .ThenBy(x => x.Tile.Slug, StringComparer.Ordinal)
            .Select(x => x.Tile)
            .ToList();
    }
}
