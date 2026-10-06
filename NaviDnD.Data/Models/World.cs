namespace NaviDnD.Data.Models;

// Карта мира (вкладка [F2]): квадратная сетка клеток (клетка ≈ полдня пути), x — на восток, y — на юг
// (y = 0 — север). Геометрию строит код (WorldGenerator): рельеф, реки, озёра, поселения, дороги,
// королевства, места приключений; имена и описания позже даёт нейронка. Хранится библиотекой миров
// (Storage/Worlds/<id>/world.json) — один мир на много игр; знания героя о мире — у игры отдельно.
public class WorldMap
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Seed { get; set; }
    public string Size { get; set; } = WorldSizes.Small;
    // Параметры генерации (анкета новой игры): -1 / 0 / 1 — суша↔вода, холоднее↔теплее (у старых миров — 0).
    public int Water { get; set; }
    public int Climate { get; set; }
    public int Continents { get; set; }   // 0 — по размеру мира
    public int Mountains { get; set; }
    public int Forests { get; set; }
    public int Deserts { get; set; }
    public int RiverAmount { get; set; }
    public string? Description { get; set; }
    public string? DmNotes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    // Строки кодов WorldBiomes, строка 0 — север.
    public List<string> Biomes { get; set; } = [];
    // Высота 0..9 одним символом на клетку — для отмывки рельефа.
    public List<string> Heights { get; set; } = [];
    // Река: '0' — нет, '1'..'9' — величина (ручей → большая река).
    public List<string> Rivers { get; set; } = [];
    // Дорога: '0' — нет, '1' — тропа, '2' — тракт.
    public List<string> Roads { get; set; } = [];
    // Владелец клетки: 'a'.. — индекс королевства, '.' — ничья земля/вода.
    public List<string> Owners { get; set; } = [];

    // Places — заготовки генератора (столицы видны сразу, остальные — запасные клетки для add_place).
    public List<WorldPlace> Places { get; set; } = [];
    public List<WorldKingdom> Kingdoms { get; set; } = [];
    public List<WorldFeature> Features { get; set; } = [];
    // Хроника: лор и места, придуманные мастером в играх этого мира (общая для всех игр мира).
    public WorldChronicle Chronicle { get; set; } = new();

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    public char BiomeAt(int x, int y) => InBounds(x, y) ? Biomes[y][x] : WorldBiomes.Ocean;
    public int HeightAt(int x, int y) => InBounds(x, y) ? Heights[y][x] - '0' : 0;
    public int RiverAt(int x, int y) => InBounds(x, y) ? Rivers[y][x] - '0' : 0;
    public int RoadAt(int x, int y) => InBounds(x, y) ? Roads[y][x] - '0' : 0;
    public int OwnerAt(int x, int y) => InBounds(x, y) && Owners[y][x] != '.' ? Owners[y][x] - 'a' : -1;
}

public static class WorldSizes
{
    public const string Small = "small";   // маленький материк, 1–2 королевства
    public const string Large = "large";   // большой континент, 4–6 королевств
}

public class WorldPlace
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = WorldPlaceTypes.Village;
    public int X { get; set; }
    public int Y { get; set; }
    public int Kingdom { get; set; } = -1;
    public string? Description { get; set; }
    public string? DmNotes { get; set; }
    public bool Hidden { get; set; }   // замысел мастера: в сводках ИИ есть, на карте героя нет
}

public static class WorldPlaceTypes
{
    public const string Capital = "capital";
    public const string City = "city";
    public const string Town = "town";
    public const string Village = "village";
    public const string Castle = "castle";
    public const string Port = "port";
    public const string Ruins = "ruins";
    public const string Dungeon = "dungeon";
    public const string Cave = "cave";
    public const string Shrine = "shrine";

    public static bool IsSettlement(string type) => type is Capital or City or Town or Village or Port;

    public static string Label(string type) => type switch
    {
        Capital => "столица",
        City => "город",
        Town => "городок",
        Village => "деревня",
        Castle => "крепость",
        Port => "порт",
        Ruins => "руины",
        Dungeon => "подземелье",
        Cave => "пещера",
        Shrine => "святилище",
        _ => type,
    };
}

public class WorldKingdom
{
    public string Name { get; set; } = "";
    public List<int> Color { get; set; } = [200, 60, 60];
    public int Capital { get; set; } = -1;   // индекс в Places
    public string? Description { get; set; }
    public string? DmNotes { get; set; }
}

// Хроника мира: концепция, королевства (по индексу), места мастера (с тропами и сохранёнными локациями).
public class WorldChronicle
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? DmNotes { get; set; }
    public List<GameKingdom> Kingdoms { get; set; } = [];
    public List<GamePlace> Places { get; set; } = [];
}

public class GameKingdom
{
    public int Id { get; set; }          // индекс королевства в WorldMap.Kingdoms
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? DmNotes { get; set; }
}

public class GamePlace
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Slot { get; set; } = -1;  // заготовка генератора (индекс WorldMap.Places), -1 — своя клетка
    public string? Description { get; set; }
    public string? DmNotes { get; set; }
    // Тропа к месту (клетки x0,y0,x1,y1,…) — рисуется, когда место известно герою.
    public List<int>? Trail { get; set; }
    // Сохранённая локация места (файл в Storage/Worlds/<id>/locations) — «всё как было» при возвращении.
    public string? Location { get; set; }
}

// Природная область с названием (лес, горы, озеро, болото, пустыня…) — подпись на карте и ориентир для ИИ.
public class WorldFeature
{
    public string Name { get; set; } = "";
    public char Biome { get; set; }
    public int X { get; set; }     // точка подписи (клетка внутри области)
    public int Y { get; set; }
    public int Size { get; set; }  // клеток
    public string? Description { get; set; }
}

public sealed record WorldBiome(char Code, string Name, List<int> Color, string DetailGlyph, List<int>? GlyphColor = null, bool Water = false);

public static class WorldBiomes
{
    public const char Ocean = '~', Sea = '-', Lake = 'l', Beach = 'b', Plains = '.', Grass = ',', Forest = 'f',
        DeepForest = 'F', Hills = 'h', Mountains = 'm', Peaks = 'M', Swamp = 's', Desert = 'd', Tundra = 't', Snow = 'w', SnowForest = 'S';

    public static readonly IReadOnlyDictionary<char, WorldBiome> All = new[]
    {
        new WorldBiome(Ocean, "Океан", [30, 52, 92], " ≈", [52, 78, 120], Water: true),
        new WorldBiome(Sea, "Прибрежные воды", [44, 78, 122], " ~", [70, 104, 148], Water: true),
        new WorldBiome(Lake, "Озеро", [52, 92, 138], " ~", [84, 124, 168], Water: true),
        new WorldBiome(Beach, "Побережье", [176, 164, 118], "  "),
        new WorldBiome(Plains, "Равнина", [112, 142, 78], "  "),
        new WorldBiome(Grass, "Луга", [96, 132, 70], " ,", [128, 162, 92]),
        new WorldBiome(Forest, "Лес", [58, 104, 56], "♣ ", [96, 150, 84]),
        new WorldBiome(DeepForest, "Чаща", [40, 78, 44], "♠♣", [74, 120, 70]),
        new WorldBiome(Hills, "Холмы", [128, 122, 86], "∩ ", [160, 150, 112]),
        new WorldBiome(Mountains, "Горы", [112, 104, 96], "▲ ", [170, 160, 150]),
        new WorldBiome(Peaks, "Снежные пики", [196, 198, 204], "▲ ", [240, 242, 246]),
        new WorldBiome(Swamp, "Болото", [78, 96, 70], "≈,", [104, 124, 92]),
        new WorldBiome(Desert, "Пустыня", [228, 208, 140], " ∙", [244, 228, 172]),
        new WorldBiome(Tundra, "Тундра", [140, 150, 138], " ,", [170, 178, 166]),
        new WorldBiome(Snow, "Снега", [214, 220, 226], "  "),
        new WorldBiome(SnowForest, "Заснеженный лес", [122, 146, 138], "♠ ", [226, 232, 236]),
    }.ToDictionary(b => b.Code);

    public static WorldBiome Get(char code) => All.GetValueOrDefault(code) ?? All[Ocean];
    public static bool IsWater(char code) => Get(code).Water;
}
