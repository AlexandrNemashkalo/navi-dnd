namespace NaviDnD.Data.Models;

// Furniture is a piece of furniture on one or several cells (MapConfig.Furniture): bar counter in a row, bed
// 1×2, table 2×2... It is arranged as a zone (Area: name, positions, aiInfo - the same format for AI), but lies
// in a separate list: it has its own physics in appearance (passability, review, shelter from TerrainCatalog -
// MapConfig.TerrainAt first looks at the furniture, then the floor), and it does not fall into the
// legend/triggers of the zones. It is drawn completely, including the grid lines between its cells. Places the
// code (BuildingGenerator), the AI sees it (map.furniture)) and changes it with a patch on id: to break
// (deleted), flip/search). (state, name), move (positions), assemble a new one (kind + positions).) The first
// cell is the “head” (by the bed there is a pillow).
public class Furniture : Area
{
    public string Kind { get; set; } = "table";
    public string? State { get; set; }
    public string? Image { get; set; }
}

public sealed record FurnitureKind(string Kind, char Code, string Name, string Image, List<int>? HeadBg = null);

public static class FurnitureCatalog
{
    public static readonly IReadOnlyDictionary<string, FurnitureKind> Kinds = new[]
    {
        new FurnitureKind("counter", 'B', "Bar counter", "delapouite/bar-stool"),
        new FurnitureKind("stall", 'L', "Stall", "delapouite/shop"),
        new FurnitureKind("table", 'Y', "Table", "delapouite/table"),
        new FurnitureKind("bed", 'E', "Bed", "delapouite/bed", HeadBg: [214, 206, 188]),
        new FurnitureKind("cabinet", 'H', "Cabinet", "delapouite/bookshelf"),
        new FurnitureKind("hearth", 'O', "Hearth", "delapouite/fireplace"),
        new FurnitureKind("barrels", 'X', "Barrels", "delapouite/barrel"),
        new FurnitureKind("altar", 'A', "Altar", "delapouite/star-altar"),
        new FurnitureKind("bench", 'N', "Bench", "delapouite/park-bench"),
    }.ToDictionary(k => k.Kind);

    public static FurnitureKind? Get(string? kind) => kind != null ? Kinds.GetValueOrDefault(kind) : null;

    public static FurnitureKind? ByCode(char code) => Kinds.Values.FirstOrDefault(k => k.Code == code);

    // Physics and type of object cell.
    public static TerrainKind? Terrain(Furniture f) => Get(f.Kind) is { } k ? TerrainCatalog.Get(k.Code) : null;

    public static string DisplayName(Furniture f) => !string.IsNullOrEmpty(f.Name) ? f.Name : Get(f.Kind)?.Name ?? f.Kind;
}
