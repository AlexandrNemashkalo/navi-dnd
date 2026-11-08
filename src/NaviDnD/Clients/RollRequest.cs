namespace NaviDnD;

public class RollRequest
{
    public List<RequestHistoryEntry> History { get; set; } = [];
    public int? Difficulty { get; set; }
    public bool Advantage { get; set; }
    public bool Disadvantage { get; set; }
    public List<RollModifier> Modifiers { get; set; } = [];
    public bool Answered { get; set; }
    public int? Roll1 { get; set; }
    public int? Roll2 { get; set; }
    public int? Answer { get; set; }
    public string? Critical { get; set; }
}

public class RollModifier
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
}
