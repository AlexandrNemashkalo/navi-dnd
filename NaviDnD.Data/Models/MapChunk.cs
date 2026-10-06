using System.Text.Json.Serialization;

namespace NaviDnD.Data.Models;

// Блок локации 20×15 в сетке поля MapConfig.MaxSize×MaxSize (MapConfig.Chunks). Геометрию блока
// генерирует код (ChunkGenerator), нейронка только наполняет его существами и объектами (PopulateChunk).
// Набор блоков локации задаёт нейронка планом в начале игры (plan_location → LocationGrower.CreatePlanned):
// геометрия всех блоков строится сразу, наполнение — когда герой подходит к блоку.
public class MapChunk
{
    public const int Cols = 20;
    public const int Rows = 15;
    public const int GridCols = MapConfig.MaxSize / Cols; // 5
    public const int GridRows = MapConfig.MaxSize / Rows; // 6

    public int X { get; set; }               // индекс блока по горизонтали, 0..GridCols-1
    public int Y { get; set; }               // индекс блока по вертикали (строки карты растут вверх), 0..GridRows-1
    public int Seed { get; set; }
    public string Theme { get; set; } = "dungeon";
    public List<ChunkExit> Exits { get; set; } = [];
    public bool Populated { get; set; }      // нейронка уже расставила существ/объекты
    public string? Purpose { get; set; }     // назначение блока по плану локации (что здесь по сюжету)

    // Рельеф открытой местности (OutdoorGenerator): Rows строк по Cols кодов TerrainCatalog, строка 0 —
    // нижняя (OriginRow+1). null — блок из комнат (подземелье). Пробел — пустота.
    public List<string>? Terrain { get; set; }

    // Этаж: 0 — земля, 1+ — верхние этажи, −1… — подвалы. Этажи одного здания стоят в плане друг над
    // другом, а на поле лежат в разных местах сетки; на экране виден только этаж героя.
    public int Z { get; set; }

    // Верхний этаж здания: клетка сетки блока первого этажа под ним — вид из окон наружу (двор, улица)
    // рисуется по карте первого этажа (MapObjectsProvider.ApplyOutsideView).
    public int? GroundX { get; set; }
    public int? GroundY { get; set; }

    public static bool IsOutdoorTheme(string? theme) => theme is "forest" or "plains" or "hills" or "mountain" or "swamp" or "desert" or "snow" or "taiga";

    // Здание: комнаты вплотную с общими стенами, двери между ними, мебель (BuildingGenerator).
    public const string Building = "building";

    // Поселение: несколько домов, улицы, площадь с колодцем, огороды (BuildingGenerator.GenerateVillage).
    public const string Village = "village";

    // Земля под открытым небом с проходом по краю без преград: местность, двор здания, поселение.
    public static bool IsOpenGround(string? theme) => IsOutdoorTheme(theme) || theme is Building or Village;

    // Тип здания (building): tavern, house, shop, temple, manor, barracks, tower… — по нему планировка и мебель.
    public string? Style { get; set; }

    // Природная пещера: комнаты-залы без плитки и дверей (dungeon — рукотворное: склеп, руины, крепость).
    public const string Cave = "cave";

    // Клетка (OriginCol+1, OriginRow+1) — левая нижняя клетка блока.
    [JsonIgnore] public int OriginCol => X * Cols;
    [JsonIgnore] public int OriginRow => Y * Rows;

    public bool Contains(int col, int row) =>
        col > OriginCol && col <= OriginCol + Cols && row > OriginRow && row <= OriginRow + Rows;
}

public enum ChunkSide { Left, Right, Bottom, Top }

// Выход на краю блока: шов в клетке Offset вдоль стороны (1..Rows для Left/Right, 1..Cols для Bottom/Top).
// Соседний блок получает вход на противоположной стороне в том же Offset — коридоры сходятся через границу.
// External — вход снаружи (откуда пришёл герой в начале игры): за ним блоков нет.
public class ChunkExit
{
    public ChunkSide Side { get; set; }
    public int Offset { get; set; }
    public bool External { get; set; }

    public static ChunkSide Opposite(ChunkSide side) => side switch
    {
        ChunkSide.Left => ChunkSide.Right,
        ChunkSide.Right => ChunkSide.Left,
        ChunkSide.Bottom => ChunkSide.Top,
        _ => ChunkSide.Bottom,
    };

    public static (int dx, int dy) Step(ChunkSide side) => side switch
    {
        ChunkSide.Left => (-1, 0),
        ChunkSide.Right => (1, 0),
        ChunkSide.Bottom => (0, -1),
        _ => (0, 1),
    };

    // Последняя клетка коридора выхода внутри блока (в координатах поля).
    public (int col, int row) AnchorCell(MapChunk chunk) => Side switch
    {
        ChunkSide.Left => (chunk.OriginCol + 1, chunk.OriginRow + Offset),
        ChunkSide.Right => (chunk.OriginCol + MapChunk.Cols, chunk.OriginRow + Offset),
        ChunkSide.Bottom => (chunk.OriginCol + Offset, chunk.OriginRow + 1),
        _ => (chunk.OriginCol + Offset, chunk.OriginRow + MapChunk.Rows),
    };
}
