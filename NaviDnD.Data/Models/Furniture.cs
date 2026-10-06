namespace NaviDnD.Data.Models;

// Мебель — предмет обстановки на одной или нескольких клетках (MapConfig.Furniture): барная стойка в ряд,
// кровать 1×2, стол 2×2… Устроена как зона (Area: name, positions, aiInfo — тот же формат для ИИ), но лежит
// отдельным списком: у неё своя физика по виду (проходимость, обзор, укрытие из TerrainCatalog —
// MapConfig.TerrainAt сначала смотрит мебель, потом пол), и в легенду/триггеры зон она не попадает.
// Рисуется сплошь, включая линии сетки между своими клетками. Расставляет код (BuildingGenerator), ИИ видит
// её (map.furniture) и меняет патчем по id: сломать (deleted), перевернуть/обыскать (state, name),
// передвинуть (positions), собрать новую (kind + positions). Первая клетка — «голова» (у кровати — подушка).
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
        new FurnitureKind("counter", 'B', "Барная стойка", "delapouite/bar-stool"),
        new FurnitureKind("stall", 'L', "Прилавок", "delapouite/shop"),
        new FurnitureKind("table", 'Y', "Стол", "delapouite/table"),
        new FurnitureKind("bed", 'E', "Кровать", "delapouite/bed", HeadBg: [214, 206, 188]),
        new FurnitureKind("cabinet", 'H', "Шкаф", "delapouite/bookshelf"),
        new FurnitureKind("hearth", 'O', "Очаг", "delapouite/fireplace"),
        new FurnitureKind("barrels", 'X', "Бочки", "delapouite/barrel"),
        new FurnitureKind("altar", 'A', "Алтарь", "delapouite/star-altar"),
        new FurnitureKind("bench", 'N', "Скамья", "delapouite/park-bench"),
    }.ToDictionary(k => k.Kind);

    public static FurnitureKind? Get(string? kind) => kind != null ? Kinds.GetValueOrDefault(kind) : null;

    public static FurnitureKind? ByCode(char code) => Kinds.Values.FirstOrDefault(k => k.Code == code);

    // Физика и вид клетки предмета.
    public static TerrainKind? Terrain(Furniture f) => Get(f.Kind) is { } k ? TerrainCatalog.Get(k.Code) : null;

    public static string DisplayName(Furniture f) => !string.IsNullOrEmpty(f.Name) ? f.Name : Get(f.Kind)?.Name ?? f.Kind;
}
