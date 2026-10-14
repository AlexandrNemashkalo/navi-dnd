namespace NaviDnD;

// File protocol select_target (how AskPlayerRequest): MCP-server writes a request, the game shows the mouse
// selection on the map and adds Result. Parameters - pure geometry, without the rules of a specific TTRPG.
public class TargetSelectionRequest
{
    public List<RequestHistoryEntry> History { get; set; } = [];
    public string Mode { get; set; } = "creature"; // creature | point
    public int? RangeFt { get; set; }
    public int Count { get; set; } = 1;
    public bool AllowRepeat { get; set; }
    public string? Shape { get; set; }             // sphere | cube | cone | line; null - without area
    public int? SizeFt { get; set; }
    public bool Answered { get; set; }
    public TargetSelectionResult? Result { get; set; }
}

public class TargetSelectionResult
{
    public bool Cancelled { get; set; }
    public bool TimedOut { get; set; }
    public List<SelectedTarget> Targets { get; set; } = [];
}

public class SelectedTarget
{
    public int? Id { get; set; }                   // index in map.entities / map.objects - for the patch; from the hero null
    public string? Symbol { get; set; }            // creature in the selected cell; null - empty cell
    public string? Name { get; set; }
    public List<int> Position { get; set; } = [];
    public int DistanceFt { get; set; }
    // Only if Shape: is specified
    public List<List<int>>? AreaCells { get; set; }        // all cells of the area [col,row]
    public List<SelectedTarget>? InArea { get; set; }      // creatures in area (including hero)
    public List<SelectedTarget>? ObjectsInArea { get; set; } // visible map objects in area
}
