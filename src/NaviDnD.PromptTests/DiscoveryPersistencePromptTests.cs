using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class DiscoveryPersistencePromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task EvidenceCorrectsAnOldRumorAndTheWitnessRemembersItAfterReload()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        state.World = new() { Place = "Bridge" };
        state.History = [];
        state.Combat = null;
        state.Hero!.Position = [9,8];
        state.Hero.Inventory = [];
        state.Narrative = new()
        {
            PlotThreads = [new() { Name = "Bridge load limit", Status = PlotThreadStatuses.Rumor,
                Description = "Rada passed on an unverified claim that three carts can use the bridge at once.",
                Steps = ["Rada repeated a traveller's claim of three carts, admitting she had not checked it."] }],
            Npcs = [new() { Name = "Rada", Met = true, Attitude = "friendly", Notes = "A well-meaning traveller.",
                Memory = ["Rada told the hero a hearsay claim of three carts at once, without checking the official notice."],
                DmNotes = "Rada made an honest mistake. She is willing to correct it when shown reliable evidence, without a check, accusation, conspiracy or new obligation." }]
        };
        state.Map = new() { Chunks = [new() { Theme = "plains", Populated = true,
            Terrain = Enumerable.Repeat(new string('.',20),15).ToList() }], Rooms = [], Doors = [], Furniture = [],
            Entities = [new() { Name = "Rada", Symbol = "RAD", Position = [9,9], Hp = "10/10" }],
            Objects = [new() { Name = "Official load plaque", Symbol = "[P]", Position = [10,8], AiInfo = [
                "The authentic current public plaque plainly reads ONE CART AT A TIME. This disproves the old three-cart hearsay; no repair or further investigation is needed to establish the current rule. Reading needs no roll."] }] };
        var reading = await Act(storage, "Read the official load plaque and correct the existing Bridge load limit story with what it establishes. I do not cross the bridge or take a job.");
        Assert.False(reading.RollDiceTriggered);
        CheckKnowledge(state);
        state.History = [];
        var restored = new Storage();
        restored.ApplySavedState(JsonSerializer.Serialize(state));
        var discussion = await Act(restored, "Rada, look at this official plaque with me. You mentioned three carts earlier, but I found a different limit. Let's correct the mistake; I'm not blaming you or taking on a task.");
        Assert.False(discussion.RollDiceTriggered);
        CheckKnowledge(restored.WorldState);
        var witness = Assert.Single(restored.WorldState.Narrative!.Npcs, n => n.Name == "Rada" && n.Deleted != true);
        Assert.Equal("friendly", witness.Attitude);
        Assert.True(witness.Memory!.Count >= 2);
        Assert.Contains(witness.Memory, line => System.Text.RegularExpressions.Regex.IsMatch(line, @"\b(one|1|single) cart", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        static void CheckKnowledge(WorldState current)
        {
            var story = Assert.Single(current.Narrative!.PlotThreads, t => t.Deleted != true && t.Hidden != true);
            Assert.Equal(PlotThreadStatuses.Revealed, story.Status);
            Assert.Matches(@"(?i)\b(one|1|single) cart", story.Description);
            Assert.Contains(story.Steps!, step => step.Contains("three carts"));
            Assert.Empty(current.Narrative.CurrentArc);
            Assert.Empty(current.Hero!.Inventory);
            Assert.False(current.Combat?.Active == true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JournalDistinguishesHearsayFromConfirmedDiscoveryWithoutAcceptingAJob(bool confirmed)
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        state.World = new() { Place = "Bridge" };
        state.Narrative = new() { PlotThreads = [new() { Name = "Bridge load limit", Status = PlotThreadStatuses.Rumor,
            Description = "The bridge's load limit is not yet known." }] };
        state.History = [];
        state.Combat = null;
        state.Hero!.Position = [9,8];
        state.Hero.Inventory = [];
        state.Map = new() { Chunks = [new() { Theme = "plains", Populated = true,
            Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }], Rooms = [], Furniture = [], Doors = [],
            Objects = confirmed ? [new() { Name = "Official load plaque", Symbol = "[P]", Position = [10,8], AiInfo = [
                "The current official load limit is clearly engraved here: THREE CARTS. The plaque is authentic and reading it is certain, without a roll.",
                "Secret GM fact: the maker's private passphrase ASH-19 is not written here and cannot be learned by reading the plaque."] }] : [],
            Entities = confirmed ? [] : [new() { Name = "Rada", Symbol = "RAD", Position = [10,8], Hp = "10/10", AiInfo = [
                "Rada heard from an unnamed traveller that the bridge permits THREE CARTS at once. She has not checked any official notice and clearly presents this as hearsay, not verified knowledge.",
                "Secret GM fact: the maker's private passphrase ASH-19 is unknown to Rada and the hero."] }] };
        var reply = await Act(storage, confirmed
            ? "Read the official load plaque and update the Bridge load limit entry in the Stories section of my journal with what I learn. I do not cross the bridge or accept any job."
            : "Rada, what have you heard about this bridge's load limit? Update the Bridge load limit entry in the Stories section of my journal with what I learn. I do not cross the bridge or accept any job.");
        Assert.False(reply.RollDiceTriggered);
        var thread = Assert.Single(state.Narrative.PlotThreads, t => t.Deleted != true && t.Hidden != true);
        Assert.Equal(confirmed ? PlotThreadStatuses.Revealed : PlotThreadStatuses.Rumor, thread.Status);
        string known = (thread.Description ?? "") + " " + string.Join(" ", thread.Steps ?? []);
        Assert.True(known.Contains("three", StringComparison.OrdinalIgnoreCase) || known.Contains('3'));
        if (!confirmed) Assert.Contains("Rada", known, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ASH-19", thread.Description ?? "");
        Assert.DoesNotContain("ASH-19", string.Join(" ", thread.Steps ?? []));
        Assert.DoesNotContain("ASH-19", string.Join(" ", state.History.Select(h => h.Text)));
        Assert.Empty(state.Narrative.CurrentArc);
        Assert.Empty(state.Hero.Inventory);
        Assert.False(state.Combat?.Active == true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CarriedDocumentRetainsItsExactClueWithoutReplacingAnAcceptedGoal(bool acceptedGoal)
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var ws = storage.WorldState;
        ws.Map = new() { Chunks = [new() { Theme = "plains", Populated = true,
            Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }],
            Objects = [], Entities = [], Rooms = [], Doors = [], Furniture = [] };
        ws.World = new() { Place = "Roadside camp" };
        ws.Hero!.Position = [9, 8];
        ws.Hero.Inventory = [new() { Name = "Surveyor's rubbing", Quantity = 1,
            Description = "An ordinary readable charcoal copy of an inscription, with no magic or trap. The hero has not read it yet; its contents are unknown to them.",
            Text = "The west sluice opens with the phrase AMBER-73. The east gate remains sealed." }];
        const string goal = "Open the west sluice";
        const string previousStep = "Chose to reopen the west sluice; obtained the surveyor's rubbing.";
        ws.Narrative = acceptedGoal ? new() { CurrentArc = goal, PlotThreads = [new()
        {
            Name = goal, Status = PlotThreadStatuses.Active,
            Description = "The hero chose to reopen the west sluice. Its phrase is still unknown and the sluice is still closed.",
            Steps = [previousStep]
        }] } : new();
        ws.History = [];
        ws.Combat = null;
        var restored = new Storage();
        restored.ApplySavedState(JsonSerializer.Serialize(ws));
        const string action = "Read my Surveyor's rubbing and tell me the exact phrase for opening the west sluice. I do not travel or use the phrase yet.";
        string context = new AiContextBuilder(restored.WorldState).MinimalState(action);
        Assert.DoesNotContain("AMBER-73", context);
        var reply = await Act(restored, action);
        Assert.False(reply.RollDiceTriggered);
        Assert.Contains("AMBER-73", JsonSerializer.Serialize(restored.WorldState.History), StringComparison.OrdinalIgnoreCase);
        var item = Assert.Single(restored.WorldState.Hero!.Inventory, i => i.Deleted != true);
        Assert.Equal(ws.Hero.Inventory[0].Text, item.Text);
        Assert.Equal(1, item.Quantity);
        Assert.Equal("Roadside camp", restored.WorldState.World!.Place);
        var narrative = restored.WorldState.Narrative!;
        var thread = Assert.Single(narrative.PlotThreads.Where(t => t.Deleted != true));
        Assert.Contains("AMBER-73", (thread.Description ?? "") + string.Join(" ",thread.Steps ?? []));
        if (acceptedGoal)
        {
            Assert.Equal(goal,narrative.CurrentArc);
            Assert.Equal(goal,thread.Name);
            Assert.Equal(PlotThreadStatuses.Active,thread.Status);
            Assert.Contains(previousStep,thread.Steps!);
        }
        else
        {
            Assert.Empty(narrative.CurrentArc);
            Assert.Equal(PlotThreadStatuses.Revealed,thread.Status);
        }
        Assert.False(restored.WorldState.Combat?.Active == true);
        var knownSteps = thread.Steps?.ToList() ?? [];
        string knownName = thread.Name;
        restored.WorldState.History = [];
        var later = new Storage();
        later.ApplySavedState(JsonSerializer.Serialize(restored.WorldState));
        var reminder = await Act(later, "Remind me of the exact opening phrase I already learned from the surveyor's rubbing. I am only checking my memory, not using it or setting off anywhere.");
        Assert.False(reminder.RollDiceTriggered);
        Assert.Contains("AMBER-73",JsonSerializer.Serialize(later.WorldState.History));
        var sameStory = Assert.Single(later.WorldState.Narrative!.PlotThreads.Where(t => t.Deleted != true));
        Assert.Equal(knownName,sameStory.Name);
        Assert.Equal(thread.Status,sameStory.Status);
        Assert.Equal(knownSteps,sameStory.Steps ?? []);
        Assert.Equal(narrative.CurrentArc,later.WorldState.Narrative.CurrentArc);
        Assert.Equal("Roadside camp",later.WorldState.World!.Place);
        Assert.Equal(1,Assert.Single(later.WorldState.Hero!.Inventory).Quantity);
    }

    [Fact]
    public async Task FailedClueInspectionLeavesAnotherEstablishedApproachAfterReload()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var ws = storage.WorldState;
        ws.World = new() { Place = "Old workshop" };
        ws.Map = new() { Chunks = [new() { Theme = "plains", Populated = true,
            Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }],
            Objects = [new() { Name = "Weathered plate", Symbol = "[P]", Position = [10, 8],
                AiInfo = ["A fixed brass plate with worn raised lettering. Reading it by eye requires DC 20 Investigation. A charcoal rubbing on paper reliably reveals the complete code CINDER-42 without a roll. A failed visual inspection cannot damage the plate. The lettering and code are not yet known to the hero."] }],
            Entities = [], Rooms = [], Doors = [], Furniture = [] };
        ws.Hero!.Position = [9, 8];
        ws.Hero.Inventory = [new() { Name = "Paper", Quantity = 2 }, new() { Name = "Charcoal", Quantity = 2 }];
        ws.Narrative = new() { CurrentArc = "Identify the workshop code", PlotThreads = [new()
        {
            Name = "Identify the workshop code", Status = "active", Description = "The weathered plate may hold the workshop code.",
            DmNotes = "The raised lettering can be copied with a charcoal rubbing. Learning CINDER-42 completes this chosen goal; it does not require using the code or accepting another job."
        }] };
        ws.History = [];
        ws.Combat = null;
        var failed = await Act(storage, "Inspect the weathered lettering by eye to read the workshop code.", 2);
        Assert.True(failed.RollDiceTriggered);
        Assert.DoesNotContain("CINDER-42", JsonSerializer.Serialize(ws.History), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("active", ws.Narrative.PlotThreads[0].Status);
        Assert.Single(ws.Map.Objects!, o => o.Deleted != true);
        ws.History = [];
        var restored = new Storage();
        restored.ApplySavedState(JsonSerializer.Serialize(ws));
        var rubbing = await Act(restored, "Use one sheet of my paper and my charcoal to take a rubbing of the raised letters on the plate.");
        Assert.False(rubbing.RollDiceTriggered);
        Assert.Contains("CINDER-42", JsonSerializer.Serialize(restored.WorldState.History), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("completed", restored.WorldState.Narrative!.PlotThreads[0].Status);
        Assert.Contains("CINDER-42", restored.WorldState.Narrative.PlotThreads[0].Description, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(restored.WorldState.Narrative.CurrentArc);
        Assert.Single(restored.WorldState.Map.Objects!, o => o.Deleted != true);
        Assert.False(restored.WorldState.Combat?.Active == true);
    }

    [Fact]
    public async Task CollectedContainerContentsDoNotReturnAfterReloadWithoutHistory()
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        var ws = storage.WorldState;
        ws.World = new() { Place = "Old shelter" };
        ws.Map = new() { Chunks = [new() { Theme = "forest", Populated = true,
                Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }],
            Objects = [new() { Name = "Wooden box", Symbol = "[B]", Position = [10, 8],
                AiInfo = ["An unlocked abandoned box containing exactly one brass token. No traps, no other contents. The token is a mundane keepsake, not a quest item."] }],
            Entities = [], Rooms = [], Doors = [], Furniture = [] };
        ws.Hero!.Position = [9, 8];
        ws.Hero.Inventory = [];
        ws.Narrative = new() { World = "A quiet rural district. Ordinary objects and traces of daily life." };
        ws.History = [];
        ws.Combat = null;
        await Act(storage, "Open the wooden box and take everything inside, leaving the box where it is.");
        Assert.Equal(1, ws.Hero.Inventory.Where(i => i.Deleted != true).Sum(i => i.Quantity ?? 1));
        Assert.Contains("token", ws.Hero.Inventory.Single(i => i.Deleted != true).Name, StringComparison.OrdinalIgnoreCase);
        Assert.Single(ws.Map.Objects!, o => o.Deleted != true);
        ws.History = [];
        var reloaded = new Storage();
        reloaded.ApplySavedState(JsonSerializer.Serialize(ws, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        await Act(reloaded, "Look inside the same wooden box again and take anything inside, leaving the box in place.");
        var after = reloaded.WorldState;
        Assert.Equal(1, after.Hero!.Inventory.Where(i => i.Deleted != true).Sum(i => i.Quantity ?? 1));
        Assert.Contains("token", after.Hero.Inventory.Single(i => i.Deleted != true).Name, StringComparison.OrdinalIgnoreCase);
        Assert.Single(after.Map.Objects!, o => o.Deleted != true);
        Assert.Empty(after.Narrative!.CurrentArc);
        Assert.False(after.Combat?.Active == true);
    }

    private async Task<AutoAnswerResult> Act(Storage storage, string action, int roll = 15)
    {
        var visible = Enumerable.Range(1, 20).SelectMany(c => Enumerable.Range(1, 15).Select(r => (c, r))).ToHashSet();
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(storage.WorldState, visible).MinimalState(action), rollValue: roll);
        output.WriteLine(action + "\n" + reply.RawResponse);
        PromptScenario.ApplyAndParse(storage, reply.RawResponse, retainHistory: true);
        return reply;
    }
}
