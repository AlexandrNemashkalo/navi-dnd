namespace NaviDnD.MapGen.Models;

public class DungeonRequest
{
    public List<RoomRequest> Rooms { get; set; } = [];
    public List<PassageRequest> Passages { get; set; } = [];
    /// <summary>Exits/entrances to another map - corridor to the edge of the grid.</summary>
    public List<MapTransition> Transitions { get; set; } = [];
}

public class MapTransition
{
    /// <summary>
    ///An off-grid coordinate that indicates the direction of the transition.
    ///North: [col, 0], South: [col, 16], West: [0, row], East: [21, row].
    /// </summary>
    public List<int> EdgePoint { get; set; } = [];
    /// <summary>The name of the room to which the corridor extends.</summary>
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
    /// <summary>Opening = direct contact of rooms; Corridor = bellhop Room.</summary>
    public PassageType Type { get; set; } = PassageType.Corridor;
    /// <summary>Width of the corridor in cells (1–3). For Opening it is ignored.</summary>
    public int Width { get; set; } = 1;
    /// <summary>Corridor shape. For Opening it is ignored.</summary>
    public CorridorShape Shape { get; set; } = CorridorShape.LShaped;
    /// <summary>Draw a door at the entrance to the target room.</summary>
    public bool HasDoor { get; set; } = false;
}

public enum PassageType
{
    /// <summary>A straight opening between two adjoining rooms, without a corridor.</summary>
    Opening,
    /// <summary>Corridor Room connecting rooms.</summary>
    Corridor,
}

public enum CorridorShape
{
    Straight,  // minimum turns (H→V or V→H)
    LShaped,   // BFS-shortest path (usually one turn)
    Winding,   // winding random path
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
