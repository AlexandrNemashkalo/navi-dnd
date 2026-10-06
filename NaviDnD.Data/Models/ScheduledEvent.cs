namespace NaviDnD.Data.Models;

public class ScheduledEvent : IPatchable
{
    public bool? Deleted { get; set; }
    public string Name { get; set; }
    public int FireAtRound { get; set; }
    public string Effect { get; set; }
    public List<string>? AiInfo { get; set; }
}
