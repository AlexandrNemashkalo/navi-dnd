namespace NaviDnD.MapGen.Models;

public class DungeonRequest
{
    public List<RoomRequest> Rooms { get; set; } = [];
    public List<PassageRequest> Passages { get; set; } = [];
    /// <summary>Выходы/входы на другую карту — коридор к краю сетки.</summary>
    public List<MapTransition> Transitions { get; set; } = [];
}

public class MapTransition
{
    /// <summary>
    /// Координата за пределами сетки, указывает направление перехода.
    /// Север: [col, 0], Юг: [col, 16], Запад: [0, row], Восток: [21, row].
    /// </summary>
    public List<int> EdgePoint { get; set; } = [];
    /// <summary>Имя комнаты, к которой тянуть коридор.</summary>
    public string Room { get; set; } = "";
    public int Width { get; set; } = 1;
    public CorridorShape Shape { get; set; } = CorridorShape.Straight;
    public bool HasDoor { get; set; } = false;
}

public class RoomRequest
{
    public string Name { get; set; } = "";
    public RoomShape Shape { get; set; } = RoomShape.Rectangular;
    public RoomSize Size { get; set; } = RoomSize.Medium;
    public string? Zone { get; set; }
}

public class PassageRequest
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    /// <summary>Opening = прямое соприкосновение комнат; Corridor = коридорный Room.</summary>
    public PassageType Type { get; set; } = PassageType.Corridor;
    /// <summary>Ширина коридора в клетках (1–3). Для Opening игнорируется.</summary>
    public int Width { get; set; } = 1;
    /// <summary>Форма коридора. Для Opening игнорируется.</summary>
    public CorridorShape Shape { get; set; } = CorridorShape.LShaped;
    /// <summary>Нарисовать дверь на входе в целевую комнату.</summary>
    public bool HasDoor { get; set; } = false;
}

public enum PassageType
{
    /// <summary>Прямой проём между двумя соприкасающимися комнатами, без коридора.</summary>
    Opening,
    /// <summary>Коридорный Room, соединяющий комнаты.</summary>
    Corridor,
}

public enum CorridorShape
{
    Straight,  // минимум поворотов (H→V или V→H)
    LShaped,   // BFS-кратчайший путь (обычно один поворот)
    Winding,   // петляющий случайный путь
}

public enum RoomShape
{
    Rectangular,
    Circular,
    Irregular,
    LShaped,
}

public enum RoomSize
{
    Small,
    Medium,
    Large,
    Huge,
}
