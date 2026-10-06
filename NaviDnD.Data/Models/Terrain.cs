namespace NaviDnD.Data.Models;

// Вид рельефа клетки (открытая местность): как выглядит и как влияет на движение/обзор. Рельеф лежит
// в блоке локации строками кодов (MapChunk.Terrain), генерирует его код (OutdoorGenerator) — карта не
// голая ещё до наполнения нейронкой. Физика (проходимость, обзор, стоимость шага) — без правил системы.
public sealed record TerrainKind(
    char Code,
    string Name,
    string[] Glyphs,          // варианты символа клетки (ширина клетки — 3 символа), выбор — по координатам
    string LegendGlyph,
    List<int> Fg,
    List<int> Bg,
    bool BlocksMove = false,
    bool BlocksSight = false,
    int StepCostFt = 5,
    string? Cover = null,
    // Внутренний рельеф (пол подземелья): рисуется поверх цвета комнаты — Fg/Bg тогда цели смешивания
    // с ним (FgMix/BgMix), дневного света и обзора открытой местности не даёт.
    bool Indoor = false,
    double FgMix = 0,
    double BgMix = 0,
    List<int>? LegendColor = null,
    // Соседние клетки того же вида рисуются сплошным брусом (стойка, скамья): символ ряда и столбика.
    string? Join = null,
    // Клетки предмета в ряд: левый конец, середина, правый конец (стол: «╤══» + «══╤» — одна столешница).
    string[]? RowGlyphs = null);

public static class TerrainCatalog
{
    public static readonly IReadOnlyDictionary<char, TerrainKind> Kinds = new[]
    {
        new TerrainKind('.', "Трава", ["   ", "   ", " · ", "  '", "'  ", "   ", " ' "], " · ", [96, 118, 70], [56, 72, 42]),
        new TerrainKind(',', "Высокая трава", [" , ", ",' ", " ,'", "' ,", ", ,"], ",',", [124, 146, 78], [60, 78, 44]),
        new TerrainKind('"', "Кусты", ["∙∙ ", " ∙∙", "∙ ∙", "∙∙∙"], "∙∙∙", [104, 150, 76], [48, 66, 38], StepCostFt: 10, Cover: "половинное"),
        new TerrainKind('T', "Деревья", [" ♣ ", " ♠ ", "♣  ", "  ♣", " ♣ "], " ♣ ", [74, 138, 62], [42, 58, 34], BlocksMove: true, BlocksSight: true),
        new TerrainKind('F', "Чаща", ["♣♠♣", "♠♣♠", "♣♣♠", "♠♠♣"], "♣♠♣", [56, 104, 50], [28, 42, 26], BlocksMove: true, BlocksSight: true),
        new TerrainKind('^', "Скалы", [" ▲ ", "▲ ▲", "^▲^", "▲^ ", " ^▲"], "▲^▲", [134, 126, 116], [72, 68, 64], BlocksMove: true, BlocksSight: true),
        new TerrainKind('o', "Валуны", [" ● ", " ● ", "●  "], " ● ", [150, 144, 134], [62, 74, 48], BlocksMove: true, Cover: "три четверти"),
        new TerrainKind('g', "Осыпь", [" ∙ ", "∙ ∙", "  ∙", "∙  "], "∙ ∙", [130, 124, 112], [84, 80, 72], StepCostFt: 10),
        new TerrainKind('s', "Каменистая земля", ["   ", " · ", "   ", "·  "], " · ", [124, 118, 108], [78, 76, 70]),
        new TerrainKind('~', "Мелководье", [" ~ ", "~ ~", "  ~", "~  "], "~ ~", [104, 146, 176], [48, 74, 96], StepCostFt: 10),
        new TerrainKind('W', "Глубокая вода", ["≈≈≈", "≈ ≈", " ≈≈", "≈≈ "], "≈≈≈", [82, 122, 162], [30, 50, 80], BlocksMove: true),
        new TerrainKind(':', "Топь", [" ░ ", "░ ░", "  ░", "░  "], "░░░", [96, 104, 66], [52, 58, 40], StepCostFt: 10),
        new TerrainKind('#', "Ограда", ["═╪═", "╪══", "══╪"], "═╪═", [168, 132, 90], [56, 72, 42], BlocksMove: true, Cover: "половинное"),
        new TerrainKind('V', "Колодец", ["(◦)"], "(◦)", [150, 170, 196], [70, 66, 60], BlocksMove: true, Cover: "половинное"),
        new TerrainKind('G', "Грядки", [":·:", "·:·", "::·"], ":·:", [120, 160, 70], [66, 50, 34], StepCostFt: 10),
        // Крыша соседнего дома в виде из окна верхнего этажа (внутрь сверху не видно).
        new TerrainKind('Z', "Крыша", ["   ", " ▁ ", "   "], "▁▁▁", [130, 86, 68], [96, 60, 48]),
        new TerrainKind('=', "Тропа", ["   ", " · ", "   ", "·  "], " · ", [128, 112, 82], [92, 80, 58]),
        // Пустыня, снега, тайга — местность по биому карты мира (OutdoorGenerator: desert / snow / taiga).
        new TerrainKind('a', "Песок", ["   ", " · ", "   ", "  ·"], " · ", [214, 190, 130], [150, 126, 78]),
        new TerrainKind('d', "Барханы", [" ⌒ ", "⌒  ", "  ⌒", "⌒ ⌒"], "⌒ ⌒", [226, 204, 146], [168, 140, 86], StepCostFt: 10),
        new TerrainKind('c', "Колючки", [" ¥ ", "¥  ", "  ¥"], " ¥ ", [120, 150, 84], [150, 126, 78], StepCostFt: 10, Cover: "половинное"),
        new TerrainKind('n', "Снег", ["   ", " · ", "  ∙", "·  "], " · ", [236, 240, 246], [176, 186, 198], StepCostFt: 10),
        new TerrainKind('e', "Ели в снегу", [" ♠ ", "♠  ", "  ♠", " ♠ "], " ♠ ", [44, 92, 70], [176, 186, 198], BlocksMove: true, BlocksSight: true),

        // Подземелье (DungeonDecorator): пол и детали поверх цвета комнаты.
        new TerrainKind('p', "Каменные плиты", ["   "], "   ", [255, 255, 255], [0, 0, 0], Indoor: true, FgMix: 0.14),
        new TerrainKind('K', "Треснувшие плиты", [" / ", "\\  ", "  /", " \\ "], " / ", [0, 0, 0], [0, 0, 0], Indoor: true, FgMix: 0.35, LegendColor: [120, 116, 110]),
        new TerrainKind('q', "Грубый камень", ["   ", " ∙ ", "∙  ", "  ∙", " ' "], " ∙ ", [255, 255, 255], [0, 0, 0], Indoor: true, FgMix: 0.16),
        new TerrainKind('k', "Трещины", [" / ", "\\  ", "  /", " \\ "], " / ", [0, 0, 0], [0, 0, 0], Indoor: true, FgMix: 0.35, LegendColor: [120, 116, 110]),
        new TerrainKind('m', "Мох", [" , ", ",, ", " ,,", ",' "], ",,,", [96, 160, 74], [46, 84, 40], Indoor: true, FgMix: 0.6, BgMix: 0.18, LegendColor: [96, 160, 74]),
        new TerrainKind('u', "Лужа", [" ~ ", "~  ", "  ~", "~ ~"], "~ ~", [130, 170, 210], [36, 58, 92], Indoor: true, FgMix: 0.5, BgMix: 0.4, LegendColor: [130, 170, 210]),
        new TerrainKind('b', "Кости", [" ° ", "°∙ ", " ∙°"], " ° ", [226, 214, 184], [0, 0, 0], Indoor: true, FgMix: 0.55, LegendColor: [226, 214, 184]),
        new TerrainKind('r', "Обломки", ["∙∙ ", " ∙∙", "∙ ∙", "∙∙∙"], "∙∙∙", [255, 255, 255], [0, 0, 0], StepCostFt: 10, Indoor: true, FgMix: 0.32, LegendColor: [150, 144, 136]),
        // Здания (BuildingGenerator): пол, мебель, лестницы между этажами.
        new TerrainKind('w', "Дощатый пол", ["   "], "   ", [255, 255, 255], [0, 0, 0], Indoor: true, FgMix: 0.10),
        // Мебель (Furniture, FurnitureCatalog): стол и кровать — сплошным цветом (у кровати — подушка в «голове»),
        // стойка/скамья — брусом; соседние клетки одного предмета и линии между ними рисуются сплошь (Join).
        new TerrainKind('Y', "Стол", ["╤═╤"], "╤═╤", [214, 170, 110], [0, 0, 0], BlocksMove: true, Cover: "половинное", Indoor: true, FgMix: 0.7, LegendColor: [214, 170, 110], Join: "═ ", RowGlyphs: ["╤══", "═══", "══╤"]),
        new TerrainKind('H', "Шкаф", ["┃≡┃"], "┃≡┃", [176, 132, 86], [0, 0, 0], BlocksMove: true, BlocksSight: true, Indoor: true, FgMix: 0.75, LegendColor: [176, 132, 86]),
        new TerrainKind('E', "Кровать", ["   "], "███", [190, 170, 140], [132, 60, 58], BlocksMove: true, Indoor: true, FgMix: 0.7, BgMix: 0.7, LegendColor: [150, 70, 66], Join: "  "),
        new TerrainKind('O', "Очаг", ["┃▲┃"], "┃▲┃", [230, 120, 60], [0, 0, 0], BlocksMove: true, Indoor: true, FgMix: 0.8, LegendColor: [230, 120, 60]),
        new TerrainKind('X', "Бочки", ["o o", " oo", "oo "], "o o", [170, 130, 90], [0, 0, 0], BlocksMove: true, Cover: "половинное", Indoor: true, FgMix: 0.7, LegendColor: [170, 130, 90], Join: "o "),
        new TerrainKind('B', "Стойка", ["═══"], "═══", [222, 178, 118], [112, 74, 40], BlocksMove: true, Cover: "половинное", Indoor: true, FgMix: 0.7, BgMix: 0.65, LegendColor: [190, 140, 86], Join: "═║"),
        new TerrainKind('L', "Прилавок", ["═══"], "═══", [222, 186, 130], [124, 90, 52], BlocksMove: true, Cover: "половинное", Indoor: true, FgMix: 0.7, BgMix: 0.6, LegendColor: [196, 156, 100], Join: "═║"),
        new TerrainKind('A', "Алтарь", ["▟█▙"], "▟█▙", [220, 210, 180], [0, 0, 0], BlocksMove: true, Cover: "половинное", Indoor: true, FgMix: 0.8, LegendColor: [220, 210, 180]),
        new TerrainKind('N', "Скамья", ["━━━"], "━━━", [190, 150, 100], [96, 68, 42], BlocksMove: true, Cover: "половинное", Indoor: true, FgMix: 0.75, BgMix: 0.5, LegendColor: [160, 120, 80], Join: "━┃"),
        new TerrainKind('U', "Лестница вверх", [" ▲ "], " ▲ ", [255, 230, 150], [40, 34, 24], Indoor: true, FgMix: 0.85, BgMix: 0.35, LegendColor: [255, 230, 150]),
        new TerrainKind('D', "Лестница вниз", [" ▼ "], " ▼ ", [255, 230, 150], [10, 8, 6], Indoor: true, FgMix: 0.85, BgMix: 0.45, LegendColor: [255, 230, 150]),
        new TerrainKind('S', "Сталагмит", [" ▲ ", " ▲ ", "▲  ", "  ▲"], " ▲ ", [255, 255, 255], [0, 0, 0], BlocksMove: true, Cover: "половинное", Indoor: true, FgMix: 0.4, LegendColor: [160, 148, 130]),
        new TerrainKind('P', "Колонна", ["▐█▌"], "▐█▌", [255, 255, 255], [0, 0, 0], BlocksMove: true, BlocksSight: true, Indoor: true, FgMix: 0.38, BgMix: 0.3, LegendColor: [170, 164, 156]),
        new TerrainKind('R', "Завал", ["▓▒▓", "▒▓▒", "▓▓▒"], "▓▒▓", [255, 255, 255], [0, 0, 0], BlocksMove: true, Cover: "три четверти", Indoor: true, FgMix: 0.3, BgMix: 0.2, LegendColor: [150, 144, 136]),
    }.ToDictionary(k => k.Code);

    // Сам пол подземелья (плиты, грубый камень) в легенду не выносится — фон, а не деталь. Всё с
    // заметным символом (мох, лужа, кости, трещины, колонны…) — подписывается.
    public static bool InLegend(TerrainKind kind) => kind.Code is not ('p' or 'q' or 'w');

    public static bool IsStair(char code) => code is 'U' or 'D';

    public static TerrainKind? Get(char code) => Kinds.GetValueOrDefault(code);

    // Фиксированная картинка вида (game-icons) — в блоке картинок при наведении на клетку.
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

    // Свойства вида одной строкой («непроходимо, закрывает обзор») или null — обычная клетка.
    public static string? Properties(TerrainKind kind)
    {
        var props = new List<string>();
        if (kind.BlocksMove) props.Add("непроходимо");
        if (kind.BlocksSight) props.Add("закрывает обзор");
        if (kind.StepCostFt > 5) props.Add($"трудная ×{kind.StepCostFt / 5}");
        if (kind.Cover != null) props.Add($"укрытие {kind.Cover}");
        return props.Count == 0 ? null : string.Join(", ", props);
    }

    // Символ клетки — детерминированно по координатам (рисунок не «мерцает» между кадрами).
    public static string Glyph(TerrainKind kind, int col, int row)
    {
        uint h = unchecked((uint)(col * 73856093) ^ (uint)(row * 19349663));
        h ^= h >> 13;
        return kind.Glyphs[(int)(h % (uint)kind.Glyphs.Length)];
    }

    // Описание для легенды: «Деревья — непроходимо, закрывает обзор».
    public static string Describe(TerrainKind kind)
    {
        var props = new List<string>();
        if (kind.BlocksMove) props.Add("непроходимо");
        if (kind.BlocksSight) props.Add("закрывает обзор");
        if (kind.StepCostFt > 5) props.Add($"×{kind.StepCostFt / 5}");
        if (kind.Cover != null) props.Add($"укрытие {kind.Cover}");
        return props.Count == 0 ? kind.Name : $"{kind.Name} — {string.Join(", ", props)}";
    }
}
