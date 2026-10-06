namespace NaviDnD;

// Файловый протокол select_target (как AskPlayerRequest): MCP-сервер пишет запрос, игра показывает
// выбор мышью на карте и дописывает Result. Параметры — чистая геометрия, без правил конкретной НРИ.
public class TargetSelectionRequest
{
    public List<RequestHistoryEntry> History { get; set; } = [];
    public string Mode { get; set; } = "creature"; // creature | point
    public int? RangeFt { get; set; }
    public int Count { get; set; } = 1;
    public bool AllowRepeat { get; set; }
    public string? Shape { get; set; }             // sphere | cube | cone | line; null — без области
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
    public int? Id { get; set; }                   // индекс в map.entities / map.objects — для патча; у героя null
    public string? Symbol { get; set; }            // существо в выбранной клетке; null — пустая клетка
    public string? Name { get; set; }
    public List<int> Position { get; set; } = [];
    public int DistanceFt { get; set; }
    // Только если задана Shape:
    public List<List<int>>? AreaCells { get; set; }        // все клетки области [col,row]
    public List<SelectedTarget>? InArea { get; set; }      // существа в области (включая героя)
    public List<SelectedTarget>? ObjectsInArea { get; set; } // видимые объекты карты в области
}
