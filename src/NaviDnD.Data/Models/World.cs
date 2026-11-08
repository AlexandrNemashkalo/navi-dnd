namespace NaviDnD.Data.Models;

// World map (tab [F2]): square grid of cells (cell ≈ half a day's journey), x - to the east, y - to the south
// (y = 0 - north). The geometry is built by the code (WorldGenerator): terrain, rivers, lakes, settlements,
// roads, kingdoms, adventure places; names and descriptions later gives a AI. Stored by a library of worlds
// (Storage/Worlds/<id>/world.json) - one world for many games; the hero’s knowledge about the world is separate
// from the game.
public class WorldMap
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Seed { get; set; }
    public string Size { get; set; } = WorldSizes.Small;
    // Generation parameters (new game profile): -1 / 0 / 1 - land↔water, colder↔warmer (for old worlds - 0).
    public int Water { get; set; }
    public int Climate { get; set; }
    public int Continents { get; set; }   // 0 - according to world size
    public int Mountains { get; set; }
    public int Forests { get; set; }
    public int Deserts { get; set; }
    public int RiverAmount { get; set; }
    public string? Description { get; set; }
    public string? DmNotes { get; set; }
    // World language (L.Russian/L.English): interface language when created; descriptions, names, journal and
    // GM's story are in it, changing the interface language does not change it.
    public string Language { get; set; } = L.Russian;
    // File format version of the world and its locations (locations/*.json) - StorageMigration.
    public int FormatVersion { get; set; } = StorageFormat.Current;
    public int Width { get; set; }
    public int Height { get; set; }

    // Code lines WorldBiomes, line 0 - north.
    public List<string> Biomes { get; set; } = [];
    // Height 0..9 with one symbol per cell - to wash the terrain.
    public List<string> Heights { get; set; } = [];
    // River: '0' - none, '1'..'9' - magnitude (stream → large river).
    public List<string> Rivers { get; set; } = [];
    // Road: '0' - none, '1' - trail, '2' - highway.
    public List<string> Roads { get; set; } = [];
    // Cell owner: 'a'.. - kingdom index, '.' - no one's land/water.
    public List<string> Owners { get; set; } = [];

    // Places - generator blanks (capitals are visible immediately, the rest are spare cells for add_place).
    public List<WorldPlace> Places { get; set; } = [];
    public List<WorldKingdom> Kingdoms { get; set; } = [];
    public List<WorldFeature> Features { get; set; } = [];
    // Chronicle: lore and places invented by the GM in the games of this world (common for all games in the world).
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
    public const string Small = "small";   // small continent, 1–2 kingdoms
    public const string Large = "large";   // large continent, 4–6 kingdoms
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
    public bool Hidden { get; set; }   // GM's plan: the AI is in the reports, the hero is not on the map
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
        Capital => "capital",
        City => "city",
        Town => "town",
        Village => "village",
        Castle => "fortress",
        Port => "port",
        Ruins => "ruins",
        Dungeon => "dungeon",
        Cave => "cave",
        Shrine => "shrine",
        _ => type,
    };
}

public class WorldKingdom
{
    public string Name { get; set; } = "";
    public List<int> Color { get; set; } = [200, 60, 60];
    public int Capital { get; set; } = -1;   // index in Places
    public string? Description { get; set; }
    public string? DmNotes { get; set; }
}

// Chronicle of the world: concept, kingdoms (by index), GM's places (with paths and saved locations).
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
    public int Id { get; set; }          // kingdom index in WorldMap.Kingdoms
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
    public int Slot { get; set; } = -1;  // generator blank (index WorldMap.Places), -1 - own cell
    public string? Description { get; set; }
    public string? DmNotes { get; set; }
    // The path to the place (cells x0, y0, x1, y1,...) - is drawn when the place is known to the hero.
    public List<int>? Trail { get; set; }
    // Saved location of the place (file in Storage/Worlds/<id>/locations) - “everything as it was” upon return.
    public string? Location { get; set; }
}

// A natural area with a name (forest, mountains, lake, swamp, desert...) is a label on the map and a reference point for the AI.
public class WorldFeature
{
    public string Name { get; set; } = "";
    public char Biome { get; set; }
    public int X { get; set; }     // signature point (cell inside the area)
    public int Y { get; set; }
    public int Size { get; set; }  // cells
    public string? Description { get; set; }
}

public sealed record WorldBiome(char Code, string Name, List<int> Color, string DetailGlyph, List<int>? GlyphColor = null, bool Water = false);

public static class WorldBiomes
{
    public const char Ocean = '~', Sea = '-', Lake = 'l', Beach = 'b', Plains = '.', Grass = ',', Forest = 'f',
        DeepForest = 'F', Hills = 'h', Mountains = 'm', Peaks = 'M', Swamp = 's', Desert = 'd', Tundra = 't', Snow = 'w', SnowForest = 'S';

    public static readonly IReadOnlyDictionary<char, WorldBiome> All = new[]
    {
        new WorldBiome(Ocean, "Ocean", [30, 52, 92], " ≈", [52, 78, 120], Water: true),
        new WorldBiome(Sea, "Coastal waters", [44, 78, 122], " ~", [70, 104, 148], Water: true),
        new WorldBiome(Lake, "Lake", [52, 92, 138], " ~", [84, 124, 168], Water: true),
        new WorldBiome(Beach, "Coast", [176, 164, 118], "  "),
        new WorldBiome(Plains, "Plains", [112, 142, 78], "  "),
        new WorldBiome(Grass, "Meadows", [96, 132, 70], " ,", [128, 162, 92]),
        new WorldBiome(Forest, "Forest", [58, 104, 56], "♣ ", [96, 150, 84]),
        new WorldBiome(DeepForest, "Thicket", [40, 78, 44], "♠♣", [74, 120, 70]),
        new WorldBiome(Hills, "Hills", [128, 122, 86], "∩ ", [160, 150, 112]),
        new WorldBiome(Mountains, "Mountains", [112, 104, 96], "▲ ", [170, 160, 150]),
        new WorldBiome(Peaks, "Snowy peaks", [196, 198, 204], "▲ ", [240, 242, 246]),
        new WorldBiome(Swamp, "Swamp", [78, 96, 70], "≈,", [104, 124, 92]),
        new WorldBiome(Desert, "Desert", [228, 208, 140], " ∙", [244, 228, 172]),
        new WorldBiome(Tundra, "Tundra", [140, 150, 138], " ,", [170, 178, 166]),
        new WorldBiome(Snow, "Snowfields", [214, 220, 226], "  "),
        new WorldBiome(SnowForest, "Snowy forest", [122, 146, 138], "♠ ", [226, 232, 236]),
    }.ToDictionary(b => b.Code);

    public static WorldBiome Get(char code) => All.GetValueOrDefault(code) ?? All[Ocean];
    public static bool IsWater(char code) => Get(code).Water;
}
