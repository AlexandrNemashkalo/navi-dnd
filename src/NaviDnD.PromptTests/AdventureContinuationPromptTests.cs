using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class AdventureContinuationPromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task FailedFavorDoesNotEraseAnEstablishedFriendship()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var ws = storage.WorldState;
        ws.World = new() { Place = "Village mill" };
        ws.Map = new() { Chunks = [new() { Theme = "plains", Populated = true,
            Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }],
            Rooms = [], Objects = [], Doors = [], Furniture = [],
            Entities = [new() { Name = "Tarin", Symbol = "TAR", Position = [10, 8], Hp = "10/10" }] };
        ws.Hero!.Position = [9, 8];
        ws.Hero.Inventory = [];
        ws.History = [];
        ws.Combat = null;
        ws.Narrative = new() { Npcs = [new() { Name = "Tarin", Met = true, Attitude = "friendly",
            Notes = "An old friend who helped the hero through a difficult winter.",
            Memory = ["The hero and Tarin have shared years of mutual help and respectful disagreement."],
            DmNotes = "Tarin needs his cart for today's flour delivery. Lending it requires finding another way to deliver the flour; a polite request alone faces DC 20 Persuasion. The cart belongs to Tarin." }] };
        const string action = "Politely ask Tarin to lend me his cart for today. I explain that it would save me a long walk, but understand he may need it.";
        var visible = Enumerable.Range(1, 20).SelectMany(c => Enumerable.Range(1, 15).Select(r => (c, r))).ToHashSet();
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(ws, visible).MinimalState(action), rollValue: 2);
        output.WriteLine(reply.RawResponse);
        Assert.True(reply.RollDiceTriggered);
        var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse, retainHistory: true);
        Assert.NotEmpty(parsed.HistoryTexts);
        Assert.Equal("friendly", ws.Narrative.Npcs[0].Attitude);
        Assert.DoesNotContain(ws.Hero.Inventory, i => i.Deleted != true && i.Name.Contains("cart", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(ws.Narrative.CurrentArc);
        Assert.DoesNotContain(ws.Narrative.PlotThreads, t => t.Status == "active");
        Assert.False(ws.Combat?.Active == true);
    }

    [Fact]
    public async Task VoluntaryHelpChangesRelationshipAndIsRememberedWithoutAnAssignedQuest()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var ws = storage.WorldState;
        ws.World = new() { Place = "Village mill" };
        ws.Map = new() { Chunks = [new() { Theme = "plains", Populated = true,
            Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }],
            Rooms = [], Objects = [], Doors = [], Furniture = [],
            Entities = [new() { Name = "Tarin", Symbol = "TAR", Position = [10, 8], Hp = "10/10" }] };
        ws.Hero!.Position = [9, 8];
        ws.Hero.Inventory = [new() { Name = "Saffron bun", Quantity = 2 }];
        ws.History = [];
        ws.Combat = null;
        ws.Narrative = new() { Npcs = [new() { Name = "Tarin", Met = true, Attitude = "neutral",
            Notes = "The miller missed his meal while keeping the mill running.",
            DmNotes = "Tarin gladly accepts freely offered food. A thoughtful meal makes him friendly and he remembers who helped. He has no task to assign, payment to demand, or reward to grant. He continues his own work at the mill." }] };
        await Act(storage, "I freely give Tarin one saffron bun for his missed meal, with no favor or payment expected.");
        Assert.Equal("friendly", ws.Narrative.Npcs[0].Attitude);
        Assert.Equal(1, ws.Hero.Inventory.Single(i => i.Deleted != true).Quantity);
        Assert.NotEmpty(ws.Narrative.Npcs[0].Memory!);
        Assert.Empty(ws.Narrative.CurrentArc);
        Assert.DoesNotContain(ws.Narrative.PlotThreads, t => t.Status == "active");
        ws.History = [];
        var restored = new Storage();
        restored.ApplySavedState(System.Text.Json.JsonSerializer.Serialize(ws));
        restored.WorldState.Time.Day++;
        await Act(restored, "Hello, Tarin. Do you remember what food I brought you yesterday?");
        var after = restored.WorldState;
        Assert.Equal("friendly", after.Narrative!.Npcs[0].Attitude);
        Assert.Equal(1, after.Hero!.Inventory.Single(i => i.Deleted != true).Quantity);
        Assert.Contains("saffron", System.Text.Json.JsonSerializer.Serialize(after.History), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(after.Narrative.CurrentArc);
        Assert.False(after.Combat?.Active == true);
    }

    [Fact]
    public async Task FinishedChosenGoalClearsAndRumorsDoNotBecomeAcceptedQuests()
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        var ws = storage.WorldState;
        ws.World = new() { Place = "Village ferry" };
        ws.Map = new() { Chunks = [new() { Theme = "plains", Populated = true,
                Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }],
            Entities = [new() { Name = "Mira", Symbol = "MIR", Position = [10, 8], Hp = "10/10",
                AiInfo = ["Friendly ferry keeper. One coil of rope completes the accepted repair request. No further requirement or roll; Mira finishes the repair herself."] }],
            Objects = [], Rooms = [], Doors = [], Furniture = [] };
        ws.Hero!.Position = [9, 8];
        ws.Hero.Inventory = [new() { Name = "Coil of rope", Quantity = 1 }];
        ws.History = [];
        ws.Combat = null;
        ws.Narrative = new() { CurrentArc = "Repair the village ferry", World = "A peaceful rural district with independent local lives.",
            PlotThreads = [new() { Name = "Ferry repairs", Status = "active", Description = "Bring Mira one coil of rope.",
                DmNotes = "Delivery fully resolves this request. The repaired ferry stays available; no additional repair task." }],
            Npcs = [new() { Name = "Mira", Met = true, Attitude = "friendly", Notes = "The ferry keeper." }] };
        await Act(storage, "Give Mira my coil of rope to complete the ferry repair request.");
        Assert.Empty(ws.Narrative.CurrentArc);
        Assert.Equal("completed", ws.Narrative.PlotThreads[0].Status);
        Assert.False(string.IsNullOrWhiteSpace(ws.Narrative.PlotThreads[0].Description));
        Assert.NotEqual("Bring Mira one coil of rope.", ws.Narrative.PlotThreads[0].Description);
        Assert.NotEmpty(ws.Narrative.PlotThreads[0].Steps!);
        Assert.Empty(ws.Hero.Inventory.Where(i => i.Deleted != true && (i.Quantity ?? 1) > 0));
        await Act(storage, "Ask Mira what interesting places or local activities I could explore next. I am just gathering possibilities, not agreeing to help anyone yet.");
        Assert.Empty(ws.Narrative.CurrentArc);
        Assert.Equal("completed", ws.Narrative.PlotThreads[0].Status);
        Assert.DoesNotContain(ws.Narrative.PlotThreads.Skip(1), t => t.Status == "active");
        Assert.False(ws.Combat?.Active == true);

        // A later visit must use the resolved record, even if the entity still has its original setup notes.
        ws.Narrative.PlotThreads.AddRange(Enumerable.Range(0, 200).Select(i => new PlotThread
        {
            Name = $"Distant errand {i}", Status = "completed", Description = $"An unrelated delivery in district {i} is finished."
        }));
        ws.History = [];
        var restored = new Storage();
        restored.ApplySavedState(System.Text.Json.JsonSerializer.Serialize(ws));
        restored.WorldState.Time.Day += 5;
        await Act(restored, "Hello Mira. Is the ferry we repaired still available for crossing? I am only asking about its condition.");
        var returned = restored.WorldState;
        Assert.Equal("completed", returned.Narrative!.PlotThreads[0].Status);
        Assert.Empty(returned.Narrative.CurrentArc);
        Assert.DoesNotContain(returned.Narrative.PlotThreads, t => t.Status == "active");
        Assert.DoesNotContain(returned.Hero!.Inventory, i => i.Deleted != true && (i.Quantity ?? 1) > 0);
        Assert.False(returned.Combat?.Active == true);
    }

    private async Task Act(Storage storage, string action)
    {
        var visible = Enumerable.Range(1, 20).SelectMany(c => Enumerable.Range(1, 15).Select(r => (c, r))).ToHashSet();
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(storage.WorldState, visible).MinimalState(action));
        output.WriteLine(action + "\n" + reply.RawResponse);
        var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse, retainHistory: true);
        Assert.NotEmpty(parsed.HistoryTexts);
    }
}
