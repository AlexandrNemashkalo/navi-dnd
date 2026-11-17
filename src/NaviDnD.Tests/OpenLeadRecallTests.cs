using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class OpenLeadRecallTests
{
    [Theory]
    [InlineData(PlotThreadStatuses.Offered)]
    [InlineData(PlotThreadStatuses.Revealed)]
    [InlineData(PlotThreadStatuses.Rumor)]
    public void OlderLocalLeadSurvivesManyCompletedStoriesWithoutBecomingAGoal(string status)
    {
        var state = new WorldState { Hero = new() { Position = [3,3] }, World = new() { Place = "Reedbank" }, Narrative = new()
        {
            PlotThreads = [new() { Name = "Old mill lead", Status = status, Description = "An unresolved lead in Reedbank." },
                ..Enumerable.Range(1,35).Select(i => new PlotThread { Name = $"Settled story {i}",
                    Status = PlotThreadStatuses.Completed, Description = "A finished matter in Reedbank." })]
        } };
        string before = System.Text.Json.JsonSerializer.Serialize(state);
        string Index(string action) => new AiContextBuilder(state).MinimalState(action).Split('\n')
            .First(line => line.StartsWith("narrative.plotThreads: ",StringComparison.Ordinal));
        Assert.Contains("Old mill lead#0",Index("What could I do around here?"));
        Assert.StartsWith("narrative.plotThreads: Settled story 35#35",Index("Remind me about Settled story 35."));
        Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(state));
        state.Narrative.PlotThreads[0].Hidden = true;
        Assert.DoesNotContain("Old mill lead#0",Index("What could I do around here?"));
    }
}
