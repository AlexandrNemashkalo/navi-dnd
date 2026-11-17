using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class LongCampaignContextTests
{
    [Fact]
    public void AcceptedGoalSurvivesUnrelatedHistoryWithoutAnExactArcTitle()
    {
        var state = Campaign();
        var goal = state.Narrative.PlotThreads[2];
        goal.Name = "The missing courier";
        goal.Status = PlotThreadStatuses.Active;
        goal.Description = "I promised to find the courier before returning home.";
        state.Narrative.CurrentArc = "Find out what happened to the messenger";
        string before = JsonSerializer.Serialize(state.Narrative);
        string context = new AiContextBuilder(state).MinimalState("Review my options before leaving.");
        Assert.Contains("The missing courier#2", context);
        Assert.Contains("Thread#2 The missing courier", context);
        Assert.Contains(goal.Description, context);
        Assert.True(context.Length < 8000);
        Assert.Equal(before, JsonSerializer.Serialize(state.Narrative));

        goal.Status = PlotThreadStatuses.Offered;
        context = new AiContextBuilder(state).MinimalState("Review my options before leaving.");
        Assert.DoesNotContain("Thread#2 The missing courier", context);
        goal.Status = PlotThreadStatuses.Active;
        goal.Hidden = true;
        context = new AiContextBuilder(state).MinimalState("Review my options before leaving.");
        Assert.DoesNotContain("Thread#2 The missing courier", context);
    }

    [Fact]
    public void PopulationRetainsLatestQuestConsequencesAheadOfLongEarlierSteps()
    {
        var state = Campaign(1);
        var thread = state.Narrative!.PlotThreads[0];
        thread.Name = "Restoring the sluice";
        thread.Description = "The sluice was repaired.";
        thread.DmNotes = "The repair is complete; honor the recorded outcome.";
        thread.Steps = [new string('x', 2000), "The wheel now works and the lower courtyard has drained."];
        string before = JsonSerializer.Serialize(thread);
        string context = new AiContextBuilder(state).PopulationState(new() { Purpose = "Explore the lower courtyard beside the sluice" });
        Assert.Contains("The wheel now works and the lower courtyard has drained.", context);
        Assert.Contains("newest first", context);
        Assert.Contains("truncated; retrieve full record", context);
        Assert.Equal(before, JsonSerializer.Serialize(thread));
        Assert.True(context.Length < 7600);
    }

    private static WorldState Campaign(int count = 1000) => new()
    {
        Narrative = new()
        {
            Npcs = Enumerable.Range(0, count).Select(i => new NpcRecord
            {
                Name = $"Citizen {i}", Notes = new string('n', 200), DmNotes = new string('p', 200), Memory = [$"Conversation {i}"]
            }).ToList(),
            PlotThreads = Enumerable.Range(0, count).Select(i => new PlotThread
            {
                Name = $"Old business {i}", Status = "completed", Description = new string('d', 200), DmNotes = new string('s', 200)
            }).ToList(),
        },
    };

    [Fact]
    public void LargeCampaignIndexIsBoundedAndKeepsExplicitOldNamesAndOriginalIds()
    {
        var state = Campaign();
        state.Narrative!.Npcs[17].Name = "Mira";
        state.Narrative.Npcs[17].Memory = ["Mira remembers the repaired bridge"];
        state.Narrative.PlotThreads[9].Name = "Bridge repairs";
        state.Narrative.PlotThreads[9].DmNotes = "Mira keeps the bridge open";
        state.Narrative.CurrentArc = "Bridge repairs";
        string before = JsonSerializer.Serialize(state.Narrative);
        string context = new AiContextBuilder(state).MinimalState("Ask Mira about the bridge");
        Assert.Contains("Mira#17", context);
        Assert.Contains("Bridge repairs#9", context);
        Assert.Contains("Mira remembers the repaired bridge", context);
        Assert.Contains("omitted records still exist", context);
        Assert.Contains("narrative.npcs:~topic", context);
        Assert.True(context.Length < 8000, $"Context grew to {context.Length} characters");
        Assert.Equal(before, JsonSerializer.Serialize(state.Narrative));
    }

    [Fact]
    public void PopulationRecallsAnOldPuzzleFactByItsTopicAmongUnrelatedRecords()
    {
        var state = Campaign();
        state.Narrative!.PlotThreads[3].Name = "A gift long ago";
        state.Narrative.PlotThreads[3].DmNotes = "The silver lantern opens the sluice; fire cannot replace it.";
        var chunk = new MapChunk { Purpose = "Explore the sluice using a silver lantern" };
        state.Map.Chunks = [chunk];
        string context = new AiContextBuilder(state).PopulationState(chunk);
        Assert.Contains("Thread#3 A gift long ago", context);
        Assert.Contains("fire cannot replace it", context);
        Assert.True(context.Length < 7600, $"Context grew to {context.Length} characters");
        Assert.Contains("Omitted records still exist", context);
        Assert.Equal(1000, state.Narrative.PlotThreads.Count);
    }

    [Fact]
    public void LongSelectedRecordsAreExplicitlyMarkedForFullRetrieval()
    {
        var state = Campaign(1);
        state.Narrative!.PlotThreads[0].Name = "The lock";
        state.Narrative.PlotThreads[0].DmNotes = new string('x', 12000) + "actual-solution-at-the-end";
        string context = new AiContextBuilder(state).PopulationState(new() { Purpose = "The lock" });
        Assert.Contains("Thread#0 The lock", context);
        Assert.Contains("truncated; retrieve full record", context);
        Assert.Contains("narrative.plotThreads:#ID", context);
        Assert.DoesNotContain("actual-solution-at-the-end", context);
        Assert.EndsWith("actual-solution-at-the-end", state.Narrative.PlotThreads[0].DmNotes);
    }

    [Fact]
    public void PopulationExcludesDeletedRecordsAndKeepsHiddenFactsLabelledForTheGm()
    {
        var state = Campaign(2);
        state.Narrative!.Npcs[0].Deleted = true;
        state.Narrative.Npcs[0].Name = "Deleted-person-marker";
        state.Narrative.PlotThreads[0].Hidden = true;
        state.Narrative.PlotThreads[0].Name = "Hidden plan";
        string context = new AiContextBuilder(state).PopulationState(new() { Purpose = "Hidden plan" });
        Assert.DoesNotContain("Deleted-person-marker", context);
        Assert.Contains("hidden:True", context);
        Assert.Contains("GM only; not hero knowledge", context);
        Assert.True(state.Narrative.PlotThreads[0].Hidden);
    }
}
