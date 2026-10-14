namespace NaviDnD.Data.Models;

// Cell terrain type (open area): what it looks like and how it affects movement/viewing. The terrain lies in
// the location block with lines of codes (MapChunk.Terrain), its code generates (OutdoorGenerator) - the map is
// not bare even before it is filled with AI. Physics (passability, visibility, cost of a step) - without system
// rules.
public sealed record TerrainKind(
    char Code,
    string Name,
    string[] Glyphs,          // cell symbol options (cell width - 3 characters), selection - by coordinates
    string LegendGlyph,
    List<int> Fg,
    List<int> Bg,
    bool BlocksMove = false,
    bool BlocksSight = false,
    int StepCostFt = 5,
    string? Cover = null,
    // Internal terrain (dungeon floor): drawn over the color of the room - Fg/Bg then the purpose of mixing with
    // it (FgMix/BgMix), does not provide daylight and visibility into open areas.
    bool Indoor = false,
    double FgMix = 0,
    double BgMix = 0,
    List<int>? LegendColor = null,
    // Neighboring cells of the same type are drawn as a solid beam (rack, bench): a symbol of a row and a column.
    string? Join = null,
    // Object cells in a row: left end, middle, right end (table: “╤══” + “══╤” - one table top).
    string[]? RowGlyphs = null);

public static class TerrainCatalog
{
    public static readonly IReadOnlyDictionary<char, TerrainKind> Kinds = new[]
    {
        new TerrainKind('.', "Grass", ["   ", "   ", " · ", "  '", "'  ", "   ", " ' "], " · ", [96, 118, 70], [56, 72, 42]),
        new TerrainKind(',', "Tall grass", [" , ", ",' ", " ,'", "' ,", ", ,"], ",',", [124, 146, 78], [60, 78, 44]),
        new TerrainKind('"', "Bushes", ["∙∙ ", " ∙∙", "∙ ∙", "∙∙∙"], "∙∙∙", [104, 150, 76], [48, 66, 38], StepCostFt: 10, Cover: "half"),
        new TerrainKind('T', "Trees", [" ♣ ", " ♠ ", "♣  ", "  ♣", " ♣ "], " ♣ ", [74, 138, 62], [42, 58, 34], BlocksMove: true, BlocksSight: true),
        new TerrainKind('F', "Thicket", ["♣♠♣", "♠♣♠", "♣♣♠", "♠♠♣"], "♣♠♣", [56, 104, 50], [28, 42, 26], BlocksMove: true, BlocksSight: true),
        new TerrainKind('^', "Rocks", [" ▲ ", "▲ ▲", "^▲^", "▲^ ", " ^▲"], "▲^▲", [134, 126, 116], [72, 68, 64], BlocksMove: true, BlocksSight: true),
        new TerrainKind('o', "Boulders", [" ● ", " ● ", "●  "], " ● ", [150, 144, 134], [62, 74, 48], BlocksMove: true, Cover: "three-quarters"),
        new TerrainKind('g', "Scree", [" ∙ ", "∙ ∙", "  ∙", "∙  "], "∙ ∙", [130, 124, 112], [84, 80, 72], StepCostFt: 10),
        new TerrainKind('s', "Stony ground", ["   ", " · ", "   ", "·  "], " · ", [124, 118, 108], [78, 76, 70]),
        new TerrainKind('~', "Shallow water", [" ~ ", "~ ~", "  ~", "~  "], "~ ~", [104, 146, 176], [48, 74, 96], StepCostFt: 10),
        new TerrainKind('W', "Deep water", ["≈≈≈", "≈ ≈", " ≈≈", "≈≈ "], "≈≈≈", [82, 122, 162], [30, 50, 80], BlocksMove: true),
        new TerrainKind(':', "Bog", [" ░ ", "░ ░", "  ░", "░  "], "░░░", [96, 104, 66], [52, 58, 40], StepCostFt: 10),
        new TerrainKind('#', "Fence", ["═╪═", "╪══", "══╪"], "═╪═", [168, 132, 90], [56, 72, 42], BlocksMove: true, Cover: "half"),
        new TerrainKind('V', "Well", ["(◦)"], "(◦)", [150, 170, 196], [70, 66, 60], BlocksMove: true, Cover: "half"),
        new TerrainKind('G', "Garden beds", [":·:", "·:·", "::·"], ":·:", [120, 160, 70], [66, 50, 34], StepCostFt: 10),
        // The roof of a neighboring house as seen from an upper floor window (you can’t see the inside from above).
        new TerrainKind('Z', "Roof", ["   ", " ▁ ", "   "], "▁▁▁", [130, 86, 68], [96, 60, 48]),
        new TerrainKind('=', "Path", ["   ", " · ", "   ", "·  "], " · ", [128, 112, 82], [92, 80, 58]),
        // Desert, snow, taiga - terrain according to the world map biome (OutdoorGenerator: desert / snow / taiga).
        new TerrainKind('a', "Sand", ["   ", " · ", "   ", "  ·"], " · ", [214, 190, 130], [150, 126, 78]),
        new TerrainKind('d', "Dunes", [" ⌒ ", "⌒  ", "  ⌒", "⌒ ⌒"], "⌒ ⌒", [226, 204, 146], [168, 140, 86], StepCostFt: 10),
        new TerrainKind('c', "Thorns", [" ¥ ", "¥  ", "  ¥"], " ¥ ", [120, 150, 84], [150, 126, 78], StepCostFt: 10, Cover: "half"),
        new TerrainKind('n', "Snow", ["   ", " · ", "  ∙", "·  "], " · ", [236, 240, 246], [176, 186, 198], StepCostFt: 10),
        new TerrainKind('e', "Snowy firs", [" ♠ ", "♠  ", "  ♠", " ♠ "], " ♠ ", [44, 92, 70], [176, 186, 198], BlocksMove: true, BlocksSight: true),

        // Dungeon (DungeonDecorator): floor and details over room color.
        new TerrainKind('p', "Stone slabs", ["   "], "   ", [255, 255, 255], [0, 0, 0], Indoor: true, FgMix: 0.14),
        new TerrainKind('K', "Cracked slabs", [" / ", "\\  ", "  /", " \\ "], " / ", [0, 0, 0], [0, 0, 0], Indoor: true, FgMix: 0.35, LegendColor: [120, 116, 110]),
        new TerrainKind('q', "Rough stone", ["   ", " ∙ ", "∙  ", "  ∙", " ' "], " ∙ ", [255, 255, 255], [0, 0, 0], Indoor: true, FgMix: 0.16),
        new TerrainKind('k', "Cracks", [" / ", "\\  ", "  /", " \\ "], " / ", [0, 0, 0], [0, 0, 0], Indoor: true, FgMix: 0.35, LegendColor: [120, 116, 110]),
        new TerrainKind('m', "Moss", [" , ", ",, ", " ,,", ",' "], ",,,", [96, 160, 74], [46, 84, 40], Indoor: true, FgMix: 0.6, BgMix: 0.18, LegendColor: [96, 160, 74]),
        new TerrainKind('u', "Puddle", [" ~ ", "~  ", "  ~", "~ ~"], "~ ~", [130, 170, 210], [36, 58, 92], Indoor: true, FgMix: 0.5, BgMix: 0.4, LegendColor: [130, 170, 210]),
        new TerrainKind('b', "Bones", [" ° ", "°∙ ", " ∙°"], " ° ", [226, 214, 184], [0, 0, 0], Indoor: true, FgMix: 0.55, LegendColor: [226, 214, 184]),
        new TerrainKind('r', "Rubble", ["∙∙ ", " ∙∙", "∙ ∙", "∙∙∙"], "∙∙∙", [255, 255, 255], [0, 0, 0], StepCostFt: 10, Indoor: true, FgMix: 0.32, LegendColor: [150, 144, 136]),
        // Buildings (BuildingGenerator): floor, furniture, stairs between floors.
        new TerrainKind('w', "Plank floor", ["   "], "   ", [255, 255, 255], [0, 0, 0], Indoor: true, FgMix: 0.10),
        // Furniture (Furniture, FurnitureCatalog): table and bed - in solid color (by the bed there is a pillow
        // in the “head”), stand/bench - with timber; adjacent cells of the same object and the lines between
        // them are drawn solidly (Join).
        new TerrainKind('Y', "Table", ["╤═╤"], "╤═╤", [214, 170, 110], [0, 0, 0], BlocksMove: true, Cover: "half", Indoor: true, FgMix: 0.7, LegendColor: [214, 170, 110], Join: "═ ", RowGlyphs: ["╤══", "═══", "══╤"]),
        new TerrainKind('H', "Cabinet", ["┃≡┃"], "┃≡┃", [176, 132, 86], [0, 0, 0], BlocksMove: true, BlocksSight: true, Indoor: true, FgMix: 0.75, LegendColor: [176, 132, 86]),
        new TerrainKind('E', "Bed", ["   "], "███", [190, 170, 140], [132, 60, 58], BlocksMove: true, Indoor: true, FgMix: 0.7, BgMix: 0.7, LegendColor: [150, 70, 66], Join: "  "),
        new TerrainKind('O', "Hearth", ["┃▲┃"], "┃▲┃", [230, 120, 60], [0, 0, 0], BlocksMove: true, Indoor: true, FgMix: 0.8, LegendColor: [230, 120, 60]),
        new TerrainKind('X', "Barrels", ["o o", " oo", "oo "], "o o", [170, 130, 90], [0, 0, 0], BlocksMove: true, Cover: "half", Indoor: true, FgMix: 0.7, LegendColor: [170, 130, 90], Join: "o "),
        new TerrainKind('B', "Counter", ["═══"], "═══", [222, 178, 118], [112, 74, 40], BlocksMove: true, Cover: "half", Indoor: true, FgMix: 0.7, BgMix: 0.65, LegendColor: [190, 140, 86], Join: "═║"),
        new TerrainKind('L', "Stall", ["═══"], "═══", [222, 186, 130], [124, 90, 52], BlocksMove: true, Cover: "half", Indoor: true, FgMix: 0.7, BgMix: 0.6, LegendColor: [196, 156, 100], Join: "═║"),
        new TerrainKind('A', "Altar", ["▟█▙"], "▟█▙", [220, 210, 180], [0, 0, 0], BlocksMove: true, Cover: "half", Indoor: true, FgMix: 0.8, LegendColor: [220, 210, 180]),
        new TerrainKind('N', "Bench", ["━━━"], "━━━", [190, 150, 100], [96, 68, 42], BlocksMove: true, Cover: "half", Indoor: true, FgMix: 0.75, BgMix: 0.5, LegendColor: [160, 120, 80], Join: "━┃"),
        new TerrainKind('U', "Stairs up", [" ▲ "], " ▲ ", [255, 230, 150], [40, 34, 24], Indoor: true, FgMix: 0.85, BgMix: 0.35, LegendColor: [255, 230, 150]),
        new TerrainKind('D', "Stairs down", [" ▼ "], " ▼ ", [255, 230, 150], [10, 8, 6], Indoor: true, FgMix: 0.85, BgMix: 0.45, LegendColor: [255, 230, 150]),
        new TerrainKind('S', "Stalagmite", [" ▲ ", " ▲ ", "▲  ", "  ▲"], " ▲ ", [255, 255, 255], [0, 0, 0], BlocksMove: true, Cover: "half", Indoor: true, FgMix: 0.4, LegendColor: [160, 148, 130]),
        new TerrainKind('P', "Column", ["▐█▌"], "▐█▌", [255, 255, 255], [0, 0, 0], BlocksMove: true, BlocksSight: true, Indoor: true, FgMix: 0.38, BgMix: 0.3, LegendColor: [170, 164, 156]),
        new TerrainKind('R', "Rockfall", ["▓▒▓", "▒▓▒", "▓▓▒"], "▓▒▓", [255, 255, 255], [0, 0, 0], BlocksMove: true, Cover: "three-quarters", Indoor: true, FgMix: 0.3, BgMix: 0.2, LegendColor: [150, 144, 136]),
    }.ToDictionary(k => k.Code);

    // The dungeon floor itself (slabs, rough stone) is not included in the legend - it is a background, not a
    // detail. Anything with a noticeable symbol (moss, puddle, bones, cracks, columns...) is signed.
    public static bool InLegend(TerrainKind kind) => kind.Code is not ('p' or 'q' or 'w');

    public static bool IsStair(char code) => code is 'U' or 'D';

    public static TerrainKind? Get(char code) => Kinds.GetValueOrDefault(code);

    // Fixed image of the type (game-icons) - in the block of images when hovering over a cell.
    private static readonly Dictionary<char, string> Images = new()
    {
        ['.'] = "delapouite/grass",
        [','] = "delapouite/high-grass",
        ['"'] = "delapouite/berry-bush",
        ['T'] = "lorc/oak",
        ['F'] = "delapouite/forest",
        ['^'] = "lorc/mountains",
        ['o'] = "delapouite/stone-stack",
        ['g'] = "delapouite/stone-pile",
        ['s'] = "delapouite/stone-path",
        ['~'] = "lorc/water-splash",
        ['W'] = "lorc/waves",
        [':'] = "delapouite/swamp",
        ['#'] = "lorc/wooden-fence",
        ['V'] = "delapouite/well",
        ['G'] = "delapouite/carrot",
        ['Z'] = "delapouite/house",
        ['='] = "delapouite/trail",
        ['a'] = "delapouite/desert",
        ['d'] = "delapouite/sandstorm",
        ['c'] = "delapouite/cactus",
        ['n'] = "lorc/snowing",
        ['e'] = "lorc/pine-tree",
        ['p'] = "delapouite/path-tile",
        ['K'] = "lorc/cracked-glass",
        ['q'] = "lorc/stone-block",
        ['k'] = "lorc/cracked-glass",
        ['m'] = "delapouite/vines",
        ['u'] = "lorc/droplet-splash",
        ['b'] = "lorc/ribcage",
        ['r'] = "delapouite/stone-pile",
        ['w'] = "delapouite/planks",
        ['Y'] = "delapouite/table",
        ['H'] = "delapouite/bookshelf",
        ['E'] = "delapouite/bed",
        ['O'] = "delapouite/fireplace",
        ['X'] = "delapouite/barrel",
        ['B'] = "delapouite/bar-stool",
        ['L'] = "delapouite/shop",
        ['A'] = "delapouite/star-altar",
        ['N'] = "delapouite/park-bench",
        ['U'] = "delapouite/3d-stairs",
        ['D'] = "delapouite/3d-stairs",
        ['S'] = "delapouite/stalactites",
        ['P'] = "delapouite/ancient-columns",
        ['R'] = "delapouite/falling-rocks",
    };

    public static string? Image(TerrainKind kind) => Images.GetValueOrDefault(kind.Code);

    // View properties in one line (“impassable, blocks view”) for on-screen tooltips - in the interface
    // language; null is a regular cell.
    public static string? Properties(TerrainKind kind)
    {
        var props = new List<string>();
        if (kind.BlocksMove) props.Add(L.T("impassable"));
        if (kind.BlocksSight) props.Add(L.T("blocks sight"));
        if (kind.StepCostFt > 5) props.Add(L.F("difficult ×{0}", kind.StepCostFt / 5));
        if (kind.Cover != null) props.Add(L.F("{0} cover", L.T(kind.Cover)));
        return props.Count == 0 ? null : string.Join(", ", props);
    }

    // The cell symbol is deterministic in coordinates (the picture does not “flicker” between frames).
    public static string Glyph(TerrainKind kind, int col, int row)
    {
        uint h = unchecked((uint)(col * 73856093) ^ (uint)(row * 19349663));
        h ^= h >> 13;
        return kind.Glyphs[(int)(h % (uint)kind.Glyphs.Length)];
    }

    // Description for the legend: “Trees are impassable, blocking the view.”
    public static string Describe(TerrainKind kind)
    {
        var props = new List<string>();
        if (kind.BlocksMove) props.Add("impassable");
        if (kind.BlocksSight) props.Add("blocks sight");
        if (kind.StepCostFt > 5) props.Add($"×{kind.StepCostFt / 5}");
        if (kind.Cover != null) props.Add($"cover {kind.Cover}");
        return props.Count == 0 ? kind.Name : $"{kind.Name} — {string.Join(", ", props)}";
    }
}
