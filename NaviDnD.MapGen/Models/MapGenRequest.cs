namespace NaviDnD.MapGen.Models;

public class MapGenRequest
{
    public MapType Type { get; set; } = MapType.Cave;
    public int Cols { get; set; } = 20;
    public int Rows { get; set; } = 15;
    public int? Seed { get; set; } // одинаковый seed — одинаковая карта (блоки локации); null — случайный
    public DungeonRequest? Dungeon { get; set; }
}

public enum MapType
{
    Cave,
    Dungeon,
    Building,
    OpenArea,
}
