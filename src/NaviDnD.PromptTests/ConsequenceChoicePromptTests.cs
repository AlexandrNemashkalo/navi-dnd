using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class ConsequenceChoicePromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AgreedDeliveryPaymentIsAwardedOnceAndRememberedAfterReload()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var ws = storage.WorldState;
        ws.World = null;
        ws.History = [];
        ws.Combat = null;
        ws.Hero!.Position = [9,8];
        ws.Hero.Inventory = [new() { Name = "Silver coins", Quantity = 3 },
            new() { Name = "Delivery acknowledgment", Quantity = 1, Text = "I received Oren's invoice in Reedbank. Signed: Mara Venn." }];
        ws.Map = new() { Chunks = [new() { Theme = "plains", Populated = true,
            Terrain = Enumerable.Repeat(new string('.',20),15).ToList() }], Rooms = [], Doors = [], Furniture = [], Objects = [],
            Entities = [new() { Name = "Oren", Symbol = "ORN", Position = [10,8], Hp = "10/10",
                AiInfo = ["Friendly potter. Promised exactly two silver coins for delivering his invoice and returning Mara's written acknowledgment. Delivery happened; he has not yet paid. He has the agreed coins ready, with no further condition or obstacle."] }] };
        ws.Narrative = new() { CurrentArc = "Deliver Oren's invoice",
            PlotThreads = [new() { Name = "Deliver Oren's invoice", Status = PlotThreadStatuses.Active,
                Description = "Invoice delivered to Mara Venn in Reedbank. Return her acknowledgment to Oren for the agreed two silver.",
                Steps = ["Mara received the invoice and signed an acknowledgment."] }],
            Npcs = [new() { Name = "Oren", Met = true, Attitude = "friendly", Notes = "Potter who commissioned the delivery.",
                Memory = ["Oren and the hero agreed on two silver coins upon return with Mara's written acknowledgment. Payment remains outstanding."] }] };
        await Act(storage, "Oren, here is Mara's signed acknowledgment. I hand it to you and accept the two silver we agreed on.");
        Check(ws);
        ws.History = [];
        var restored = new Storage();
        restored.ApplySavedState(JsonSerializer.Serialize(ws));
        await Act(restored, "Oren, just to check my accounts: how much did you pay me for that delivery? I am asking about the payment already made.");
        Check(restored.WorldState);

        static void Check(WorldState state)
        {
            Assert.Equal(5, Assert.Single(state.Hero!.Inventory, i => i.Deleted != true && (i.Quantity ?? 1) > 0).Quantity);
            Assert.Equal(PlotThreadStatuses.Completed, Assert.Single(state.Narrative!.PlotThreads, t => t.Deleted != true).Status);
            Assert.Empty(state.Narrative.CurrentArc);
            Assert.False(state.Combat?.Active == true);
        }
    }

    [Fact]
    public async Task DecliningAnOfferedErrandSurvivesReloadWithoutBecomingAnObligation()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        state.World = null;
        state.History = [];
        state.Combat = null;
        state.Hero!.Position = [9,8];
        state.Hero.Inventory = [];
        state.Map = new()
        {
            Chunks = [new() { Theme = "plains", Populated = true, Terrain = Enumerable.Repeat(new string('.',20),15).ToList() }],
            Rooms = [], Doors = [], Furniture = [], Objects = [],
            Entities = [new() { Name = "Oren", Symbol = "ORN", Position = [10,8], Hp = "10/10",
                AiInfo = ["A friendly potter. He asked whether the hero would deliver an ordinary invoice to the next village, Reedbank. This is optional paid work, without urgency, danger or moral obligation. The invoice remains in his drawer until a willing courier accepts it."] }]
        };
        state.Narrative = new()
        {
            PlotThreads = [new() { Name = "Deliver Oren's invoice", Status = PlotThreadStatuses.Offered,
                Description = "Oren offered paid work delivering an invoice to the next village. No agreement has been made.", Steps = [] }],
            Npcs = [new() { Name = "Oren", Met = true, Attitude = "friendly", Notes = "Local potter.",
                Memory = ["Oren offered the hero an optional invoice-delivery job. The hero has not answered yet."] }]
        };
        await Act(storage, "Thank you for offering, Oren, but I don't want the delivery job. I'm staying around here for now. Please find someone else.");
        Check(storage.WorldState);
        var remembered = Assert.Single(state.Narrative.Npcs, n => n.Name == "Oren");
        Assert.True(remembered.Memory!.Count > 1);
        output.WriteLine("REFUSAL MEMORY: " + string.Join(" | ", remembered.Memory));
        state.History = [];
        state.Time.Day++;
        var restored = new Storage();
        restored.ApplySavedState(JsonSerializer.Serialize(state));
        await Act(restored, "Hello again, Oren. I'm taking a quiet walk around the village. What are you making today?");
        Check(restored.WorldState);

        // A refusal is remembered, but the player can reconsider an opportunity that is still available.
        restored.WorldState.History = [];
        var reconsidered = new Storage();
        reconsidered.ApplySavedState(JsonSerializer.Serialize(restored.WorldState));
        await Act(reconsidered, "Oren, I've changed my mind about delivering your invoice. If you still have it, I accept the job. Please hand it to me now; I'll set out later, not during this conversation.");
        var accepted = reconsidered.WorldState;
        var sameThread = Assert.Single(accepted.Narrative!.PlotThreads, t => t.Deleted != true);
        Assert.Equal("Deliver Oren's invoice", sameThread.Name);
        Assert.Equal(PlotThreadStatuses.Active, sameThread.Status);
        Assert.NotEmpty(accepted.Narrative.CurrentArc);
        Assert.Single(accepted.Hero!.Inventory, i => i.Deleted != true && (i.Quantity ?? 1) > 0);
        Assert.Equal(new[] { 9,8 }, accepted.Hero.Position);
        Assert.False(accepted.Combat?.Active == true);

        string beforeDetails = sameThread.Description;
        var priorSteps = sameThread.Steps?.ToArray() ?? [];
        accepted.History = [];
        var clarified = new Storage();
        clarified.ApplySavedState(JsonSerializer.Serialize(accepted));
        await Act(clarified, "Before I leave, Oren: who exactly receives this invoice, where can I find them, and what payment are we agreeing on? Please give me the practical details. I stay here to listen.");
        var ready = clarified.WorldState;
        var detailedThread = Assert.Single(ready.Narrative!.PlotThreads, t => t.Deleted != true);
        Assert.Equal(sameThread.Name, detailedThread.Name);
        Assert.Equal(PlotThreadStatuses.Active, detailedThread.Status);
        Assert.NotEqual(beforeDetails, detailedThread.Description);
        Assert.True((detailedThread.Steps?.Count ?? 0) > priorSteps.Length);
        Assert.All(priorSteps, step => Assert.Contains(step, detailedThread.Steps!));
        Assert.Single(ready.Hero!.Inventory, i => i.Deleted != true && (i.Quantity ?? 1) > 0);
        Assert.Equal(new[] { 9,8 }, ready.Hero.Position);
        Assert.False(ready.Combat?.Active == true);
        output.WriteLine("DELIVERY DETAILS: " + detailedThread.Description + " | " + string.Join(" | ", detailedThread.Steps!));
        Assert.Contains("Reedbank", detailedThread.Description + " " + string.Join(" ", detailedThread.Steps!), StringComparison.OrdinalIgnoreCase);

        static void Check(WorldState current)
        {
            Assert.Empty(current.Narrative!.CurrentArc);
            Assert.DoesNotContain(current.Narrative.PlotThreads, t => t.Deleted != true && t.Status == PlotThreadStatuses.Active);
            Assert.Empty(current.Hero!.Inventory.Where(i => i.Deleted != true && (i.Quantity ?? 1) > 0));
            Assert.False(current.Combat?.Active == true);
            Assert.Equal(new[] { 9,8 }, current.Hero.Position);
            Assert.DoesNotContain(current.Narrative.Npcs, n => n.Name == "Oren" && n.Attitude == "hostile");
        }
    }

    [Fact]
    public async Task ReturningAfterACompletedCommissionDoesNotRepeatItsRewardOrReopenIt()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        state.World = null;
        state.Time.Day = 12;
        state.Combat = null;
        state.History = [];
        state.Hero!.Position = [9,8];
        state.Hero.Inventory = [new() { Name = "Heron brooch", Quantity = 1,
            Description = "A unique silver brooch received from Sela after returning her missing ledger on day 4." }];
        state.Map = new()
        {
            Chunks = [new() { Theme = "plains", Populated = true, Terrain = Enumerable.Repeat(new string('.',20),15).ToList() }],
            Rooms = [], Doors = [], Furniture = [], Objects = [],
            Entities = [new() { Name = "Sela", Symbol = "SEL", Position = [10,8], Hp = "10/10",
                AiInfo = ["A peaceful bookbinder at her ordinary shop. The ledger is safely back in her locked cabinet; no new theft or danger has occurred. Today she is binding ordinary account books."] }]
        };
        state.Narrative = new()
        {
            CurrentArc = "",
            PlotThreads = [new() { Name = "Return Sela's ledger", Status = PlotThreadStatuses.Completed,
                Description = "Sela's missing ledger was returned on day 4 and she paid the agreed reward.",
                Steps = ["Returned the ledger intact.", "Received the unique Heron brooch as full payment."] }],
            Npcs = [new() { Name = "Sela", Met = true, Attitude = "friendly", Notes = "Bookbinder whose ledger the hero recovered.",
                Memory = ["On day 4 the hero returned my ledger. I gave them my unique Heron brooch as the agreed full reward. Our commission is settled."] }]
        };
        var restored = new Storage();
        restored.ApplySavedState(JsonSerializer.Serialize(state));
        await Act(restored, "I stop by Sela's shop after being away for a week. Hello Sela, how have you been? How is business going?");
        var after = restored.WorldState;
        var thread = Assert.Single(after.Narrative!.PlotThreads, t => t.Name == "Return Sela's ledger" && t.Deleted != true);
        Assert.Equal(PlotThreadStatuses.Completed, thread.Status);
        Assert.Equal(state.Narrative.PlotThreads[0].Steps, thread.Steps);
        Assert.Empty(after.Narrative.CurrentArc);
        Assert.DoesNotContain(after.Narrative.PlotThreads, t => t.Deleted != true && t.Status == PlotThreadStatuses.Active);
        var brooch = Assert.Single(after.Hero!.Inventory.Where(i => i.Deleted != true && (i.Quantity ?? 1) > 0));
        Assert.Equal("Heron brooch", brooch.Name);
        Assert.Equal(1, brooch.Quantity);
        Assert.False(after.Combat?.Active == true);
        Assert.Equal(new[] { 9,8 }, after.Hero.Position);
    }

    [Theory]
    [InlineData(4, true, false)]
    [InlineData(6, false, false)]
    [InlineData(6, true, true)]
    public async Task ARevisitedWorkshopChangesOnlyAfterItsEstablishedPrerequisites(int day, bool delivered, bool repaired)
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        state.World = null;
        state.Time.Day = day;
        state.Time.PartOfDay = "day";
        state.Combat = null;
        state.History = [];
        state.Hero!.Position = [9, 8];
        state.Hero.Inventory = [];
        state.Map = new()
        {
            Chunks = [new() { Theme = "plains", Populated = true, Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }],
            Rooms = [], Doors = [], Furniture = [],
            Entities = [new() { Name = "Tarin", Symbol = "TAR", Position = [10,8], Hp = "10/10",
                AiInfo = ["The miller repairs his own cart in this peaceful workshop; the hero is a returning acquaintance, not an employee."] }],
            Objects = [new() { Name = "Mill cart", Symbol = "CRT", Position = [10,9],
                Effects = [new() { Name = "Broken axle", Description = "Cannot carry flour until the axle is repaired." }],
                AiInfo = ["On day 2 Tarin planned to finish the axle repair by the start of day 5 if the ordered replacement timber arrived. With the delivery recorded, ordinary work is certain to finish on day 5; there are no other obstacles. Without that delivery the repair cannot begin, however long the hero stays away.",
                    delivered ? "The ordered replacement timber was delivered to Tarin on day 3. It belongs to the workshop and is already allocated to the axle repair." : "The supplier has not delivered the replacement timber. There is no spare wood and no alternative source has been arranged."] }]
        };
        state.Narrative = new()
        {
            Npcs = [new() { Name = "Tarin", Met = true, Attitude = "friendly", Notes = "Miller repairing his own flour cart.",
                Memory = ["On day 2 Tarin told the hero that his cart needed a replacement axle; he expected to finish by day 5 if his timber order arrived. The hero took no job and made no promise."] }]
        };
        var restored = new Storage();
        restored.ApplySavedState(JsonSerializer.Serialize(state));
        await Act(restored, "I return to Tarin's workshop and greet him. How has the cart repair gone since my last visit? I inspect its current condition and listen; I do not take supplies, perform repairs or accept a job.");
        var after = restored.WorldState;
        var cart = Assert.Single(after.Map.Objects!, o => o.Symbol == "CRT" && o.Deleted != true);
        Assert.Equal(!repaired, cart.Effects?.Any(e => e.Deleted != true && e.Name == "Broken axle") == true);
        Assert.Empty(after.Narrative!.CurrentArc);
        Assert.DoesNotContain(after.Narrative.PlotThreads, t => t.Deleted != true && t.Status == PlotThreadStatuses.Active);
        Assert.Empty(after.Hero!.Inventory);
        Assert.False(after.Combat?.Active == true);
        Assert.Equal(day, after.Time.Day);
        Assert.Contains(after.Narrative.Npcs, n => n.Name == "Tarin" && n.Memory!.Count >= (repaired ? 2 : 1));
    }

    [Fact]
    public async Task ChoosingPublicBridgeLeavesMillersCartUnavailableAcrossALaterVisit()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var ws = storage.WorldState;
        const string decision = "Allocate the communal timber";
        const string cartCondition = "Broken axle";
        const string bridgeCondition = "Missing bridge boards";
        const string constraints = "The hero has explicit communal authority to allocate the only spare timber bundle, enough for exactly one repair: the public bridge or Tarin's mill cart. Both urgent needs are legitimate: families need the bridge and Tarin needs the cart for flour deliveries. Neither NPC is a villain. The carpenters are present, repair is certain and takes only a few minutes once authorized, with no check or further quest. Allocation consumes the entire communal bundle, never gives it to the hero, and resolves the allocation thread. If the bridge is chosen, remove its missing-boards effect; Tarin's Broken axle effect remains indefinitely until new timber is actually obtained. No replacement supplies arrive automatically over the following days. Record the chosen repair and the unmet need in both NPCs' memories. Disappointment can change attitudes without causing hostility or combat.";
        ws.World = new() { Place = "Village communal yard" };
        ws.Map = new() { Chunks = [new() { Theme = "plains", Populated = true,
            Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }], Rooms = [], Doors = [], Furniture = [],
            Entities = [new() { Name = "Mira", Symbol = "MIR", Position = [10, 8], Hp = "10/10", AiInfo = ["Keeper of the communal store; supports repairing the public bridge."] },
                new() { Name = "Tarin", Symbol = "TAR", Position = [10, 9], Hp = "10/10", AiInfo = ["Miller; needs his cart for flour deliveries and accepts the hero's authority to choose."] }],
            Objects = [new() { Name = "Communal spare timber", Symbol = "TIM", Position = [9, 9],
                    AiInfo = ["The single communal bundle. Consume this whole object when either repair is authorized; no replacement stock exists."] },
                new() { Name = "Tarin's mill cart", Symbol = "CRT", Position = [11, 9],
                    AiInfo = ["The cart is unusable until its axle is repaired with new timber. Time passing alone cannot repair it."],
                    Effects = [new() { Name = cartCondition, Description = "The cart cannot carry flour; repair requires the entire timber bundle." }] },
                new() { Name = "Public footbridge", Symbol = "BRG", Position = [11, 8],
                    Effects = [new() { Name = bridgeCondition, Description = "Closed until the entire spare timber bundle replaces its missing boards." }] }] };
        ws.Hero!.Position = [9, 8];
        ws.Hero.Inventory = [];
        string hp = ws.Hero.Hp;
        ws.History = [];
        ws.Combat = null;
        ws.Narrative = new() { World = "A peaceful village whose limited communal resources require difficult but legitimate choices.",
            CurrentArc = decision,
            PlotThreads = [new() { Name = decision, Status = PlotThreadStatuses.Active,
                Description = "Choose whether the only spare timber repairs the public bridge or the mill cart.", DmNotes = constraints }],
            Npcs = [new() { Name = "Mira", Met = true, Attitude = "neutral", Notes = "Communal store keeper.", DmNotes = constraints, Memory = [] },
                new() { Name = "Tarin", Met = true, Attitude = "friendly", Notes = "Miller with an unusable cart.", DmNotes = constraints, Memory = [] }] };

        await Act(storage, "I exercise the authority the village gave me: use all the communal spare timber to repair the public footbridge now. I understand this leaves Tarin's mill cart unusable and his flour deliveries delayed. I tell Mira and Tarin why I choose the public crossing, acknowledge Tarin's real loss, and wait for the carpenters to finish this certain repair. I am not accepting another task.");
        CheckOutcome(ws);
        foreach (string name in new[] { "Mira", "Tarin" })
        {
            var npc = Assert.Single(ws.Narrative.Npcs.Where(n => n.Name == name && n.Deleted != true));
            Assert.NotEmpty(npc.Memory!);
            output.WriteLine(name + " consequence memory: " + string.Join(" | ", npc.Memory!));
        }
        Assert.NotEmpty(ws.Narrative.PlotThreads[0].Steps!);

        ws.History = [];
        var restored = new Storage();
        restored.ApplySavedState(JsonSerializer.Serialize(ws));
        restored.WorldState.Time.Day += 2;
        await Act(restored, "Hello Tarin. Two days have passed since our timber decision. Is your mill cart available for flour deliveries now? I am only asking how things stand; I bring no supplies, request no gift and accept no new task.");
        CheckOutcome(restored.WorldState);

        // Consequences can lead to a new chosen goal without reopening or undoing the resolved decision.
        restored.WorldState.History = [];
        var nextVisit = new Storage();
        nextVisit.ApplySavedState(JsonSerializer.Serialize(restored.WorldState));
        await Act(nextVisit, "Tarin, I still stand by repairing the public bridge, but I do not want to leave you stranded. I promise to find replacement timber for your cart: that is what I want to work on next. For now I stay here and tell you my decision; I have not found or brought any wood yet.");
        var chosen = nextVisit.WorldState;
        Assert.Equal(PlotThreadStatuses.Completed, Assert.Single(chosen.Narrative!.PlotThreads,
            t => t.Name == decision && t.Deleted != true).Status);
        Assert.NotEmpty(chosen.Narrative.CurrentArc);
        Assert.Contains(chosen.Narrative.PlotThreads, t => t.Deleted != true && t.Name != decision && t.Status == PlotThreadStatuses.Active);
        Assert.Contains(Assert.Single(chosen.Map.Objects!, o => o.Symbol == "CRT" && o.Deleted != true).Effects!,
            e => e.Deleted != true && e.Name == cartCondition);
        Assert.DoesNotContain(Assert.Single(chosen.Map.Objects!, o => o.Symbol == "BRG" && o.Deleted != true).Effects ?? [],
            e => e.Deleted != true && e.Name == bridgeCondition);
        Assert.DoesNotContain(chosen.Map.Objects!, o => o.Deleted != true && o.Symbol == "TIM");
        Assert.Empty(chosen.Hero!.Inventory);
        Assert.False(chosen.Combat?.Active == true);
        Assert.Equal(hp, chosen.Hero.Hp);

        void CheckOutcome(WorldState state)
        {
            var thread = Assert.Single(state.Narrative!.PlotThreads.Where(t => t.Name == decision && t.Deleted != true));
            Assert.Equal(PlotThreadStatuses.Completed, thread.Status);
            Assert.Empty(state.Narrative.CurrentArc);
            Assert.DoesNotContain(state.Narrative.PlotThreads, t => t.Deleted != true && t.Status == PlotThreadStatuses.Active);
            Assert.False(state.Combat?.Active == true);
            Assert.Equal(hp, state.Hero!.Hp);
            Assert.DoesNotContain(state.Hero.Inventory, i => i.Deleted != true && (i.Quantity ?? 1) > 0);
            var cart = Assert.Single(state.Map.Objects!.Where(o => o.Symbol == "CRT" && o.Deleted != true));
            Assert.Contains(cart.Effects ?? [], e => e.Deleted != true && e.Name == cartCondition);
            var bridge = Assert.Single(state.Map.Objects.Where(o => o.Symbol == "BRG" && o.Deleted != true));
            Assert.DoesNotContain(bridge.Effects ?? [], e => e.Deleted != true && e.Name == bridgeCondition);
            Assert.DoesNotContain(state.Map.Objects, o => o.Deleted != true && o.Symbol == "TIM");
        }
    }

    private async Task Act(Storage storage, string action)
    {
        var visible = Enumerable.Range(1, 20).SelectMany(c => Enumerable.Range(1, 15).Select(r => (c, r))).ToHashSet();
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(storage.WorldState, visible).MinimalState(action));
        output.WriteLine(reply.ToolTrace);
        output.WriteLine(reply.RawResponse);
        Assert.False(reply.RollDiceTriggered);
        var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse, retainHistory: true);
        Assert.NotEmpty(parsed.HistoryTexts);
    }
}
