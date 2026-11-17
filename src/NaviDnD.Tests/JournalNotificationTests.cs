using NaviDnD.Data.Models;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class JournalNotificationTests
{
    private static WorldState Scene() => new() { Narrative = new() {
        PlotThreads = [new() { Name = "Bridge", Status = "active", Description = "A broken bridge", Steps = ["Found the bank"] },
            new() { Name = "Secret", Hidden = true, Description = "Unknown plot" }],
        Npcs = [new() { Name = "Keeper", Met = true, Notes = "Works here", Attitude = "neutral", Memory = ["Met at the bridge"] },
            new() { Name = "Stranger", Met = false, Notes = "Not encountered" }] } };

    [Theory]
    [InlineData("description")]
    [InlineData("step")]
    [InlineData("attitude")]
    [InlineData("notes")]
    [InlineData("memory")]
    public void UpdatedVisibleFactNotifiesEvenWhenRecordCountsStayTheSame(string field)
    {
        var ws = Scene();
        string before = JournalDisplay.Stamp(ws);
        switch (field)
        {
            case "description": ws.Narrative.PlotThreads[0].Description = "The bridge has been repaired"; break;
            case "step": ws.Narrative.PlotThreads[0].Steps![0] = "Found the eastern bank"; break;
            case "attitude": ws.Narrative.Npcs[0].Attitude = "friendly"; break;
            case "notes": ws.Narrative.Npcs[0].Notes = "Now helps travelers cross"; break;
            case "memory": ws.Narrative.Npcs[0].Memory![0] = "Promised free passage"; break;
        }
        Assert.NotEqual(before, JournalDisplay.Stamp(ws));
    }

    [Fact]
    public void HiddenPlansDoNotSignalButRevealingThemDoes()
    {
        var ws = Scene();
        string before = JournalDisplay.Stamp(ws);
        ws.Narrative.PlotThreads[0].DmNotes = "A later storm may damage the bridge";
        ws.Narrative.PlotThreads[1].Description = "A secret plan changed";
        ws.Narrative.Npcs[0].DmNotes = "Private intentions changed";
        ws.Narrative.Npcs[1].Notes = "Secret identity changed";
        Assert.Equal(before, JournalDisplay.Stamp(ws));
        ws.Narrative.PlotThreads[1].Hidden = false;
        Assert.NotEqual(before, JournalDisplay.Stamp(ws));
        before = JournalDisplay.Stamp(ws);
        ws.Narrative.Npcs[1].Met = true;
        Assert.NotEqual(before, JournalDisplay.Stamp(ws));
    }
}
