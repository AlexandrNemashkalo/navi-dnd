namespace NaviDnD.Data.Models;

public class Area : IPatchable
{
    public bool? Deleted { get; set; }
    public string Name { get; set; }
    public List<int> Color { get; set; }
    public List<List<int>> Positions { get; set; }
    public int? StepCostFt { get; set; }
    public AreaTriggers? Triggers { get; set; }
    public List<string>? AiInfo { get; set; }
}

public class AreaTriggers
{
    public TriggerEffect? OnExplored { get; set; }
    public TriggerEffect? OnRound { get; set; }
}
