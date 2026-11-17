using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class LongCampaignPromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task InspectingSearchedFurnitureDoesNotRespawnItsRemovedClue()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        state.World = null;
        state.Narrative = new();
        state.History = [];
        state.Combat = null;
        state.Hero!.Position = [3,3];
        state.Hero.Inventory = [];
        state.Map = new() { Chunks = [new() { Theme = "plains", Populated = true,
            Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }], Rooms = [], Entities = [], Objects = [],
            Furniture = [new() { Kind = "table", Name = "Discarded bench", Positions = [[8,3]], Deleted = true },
                new() { Kind = "table", Name = "Repair bench", State = "searched", Positions = [[4,3]], AiInfo = [
                    "The open drawer was thoroughly searched. Its only letter was removed and delivered to the miller during the previous visit.",
                    "The drawer is visibly empty; no replacement letter, hidden compartment, loot, mechanism or hazard exists. Looking inside is certain and needs no check."] }] };
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(state, [(3,3),(4,3)])
                .MinimalState("I look inside the repair bench's open drawer. What is there now?"));
        output.WriteLine(reply.ToolTrace);
        output.WriteLine(reply.RawResponse);
        Assert.False(reply.RollDiceTriggered);
        Assert.False(reply.AskPlayerTriggered);
        var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse, retainHistory: true);
        Assert.Contains(parsed.HistoryTexts, text => text.Contains("empty", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(state.Hero.Inventory);
        Assert.DoesNotContain(state.Map.Objects!, item => item.Deleted != true);
        Assert.Equal("searched", Assert.Single(state.Map.Furniture!, f => f.Deleted != true).State);
        Assert.Empty(state.Narrative.CurrentArc);
        Assert.False(state.Combat?.Active == true);
    }

    [Fact]
    public async Task RussianParaphraseRecallsAnArchivedPromiseWithoutRechargingTheHero()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        state.World = null;
        state.Map = new()
        {
            Chunks = [new() { Theme = "plains", Populated = true, Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }],
            Rooms = [], Objects = [], Furniture = [],
            Doors = [new() { From = [10, 8], To = [11, 8], IsDoor = true, IsDoorOpen = false }],
            Entities = [new() { Name = "Тарин", Symbol = "TAR", Position = [10, 8], Hp = "12/12",
                AiInfo = ["The peaceful new keeper of the pontoon bridge honors his predecessor's recorded agreements. Retrieve the original terms before charging; he can open the adjacent gate. There is no danger or uncertainty in honoring a valid agreement."] }]
        };
        state.Hero!.Position = [9, 8];
        state.Hero.Inventory = [new() { Name = "Зелёная тесёмка", Quantity = 1 }, new() { Name = "Золотая монета", Quantity = 7 }];
        state.Combat = null;
        state.History = [];
        state.Narrative = new()
        {
            Npcs = Enumerable.Range(0, 300).Select(i => new NpcRecord
            {
                Name = $"Житель {i}", Met = true, Notes = "Старый знакомый из другого селения."
            }).ToList()
        };
        state.Narrative.Npcs[17] = new()
        {
            Name = "Мира", Met = true, Notes = "Бывшая смотрительница понтонного моста.",
            Memory = ["По договорённости КЕДР-42 герой вернул пропавшие доски, а Мира выдала ему зелёный шнурок: это пожизненный бесплатный пропуск через мост для самого героя. Все сменщики соблюдают обещание; шнурок лишь показывают, не отдают и не расходуют."]
        };
        var reloaded = new Storage();
        reloaded.ApplySavedState(JsonSerializer.Serialize(state));
        const string action = "Показываю зелёную тесёмку новому сторожу: раньше я помог здесь с ремонтом, и мне обещали больше не брать плату. Проверьте старые записи и пропустите меня на прежних условиях.";
        var visible = Enumerable.Range(1, 20).SelectMany(c => Enumerable.Range(1, 15).Select(r => (c, r))).ToHashSet();
        string request = "lang:ru\nspeechEnabled:false\n" + new AiContextBuilder(reloaded.WorldState, visible).MinimalState(action);
        Assert.DoesNotContain("КЕДР-42", request);
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", reloaded, request);
        output.WriteLine(reply.ToolTrace);
        output.WriteLine(reply.RawResponse);
        Assert.Contains("MCP get_world_state →", reply.ToolTrace);
        Assert.Contains(reply.ToolTrace.Split('\n'), line => line.Contains("MCP get_world_state ←", StringComparison.Ordinal)
            && line.Contains(JsonEncodedText.Encode("КЕДР-42").ToString(), StringComparison.Ordinal));
        Assert.False(reply.RollDiceTriggered);
        Assert.False(reply.AskPlayerTriggered);
        PromptScenario.ApplyAndParse(reloaded, reply.RawResponse, retainHistory: true);
        var after = reloaded.WorldState;
        Assert.True(after.Map.Doors![0].IsDoorOpen);
        Assert.Equal(1, after.Hero!.Inventory.Single(i => i.Name == "Зелёная тесёмка" && i.Deleted != true).Quantity);
        Assert.Equal(7, after.Hero.Inventory.Single(i => i.Name == "Золотая монета" && i.Deleted != true).Quantity);
        Assert.False(after.Combat?.Active == true);
        Assert.Empty(after.Narrative!.CurrentArc);
        Assert.Contains(after.Narrative.Npcs[17].Memory!, memory => memory.Contains("КЕДР-42"));
    }

    [Fact]
    public async Task InspectionRetrievesAnOlderObjectInscriptionWithoutInventingItsText()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        state.World = null;
        state.Narrative = new();
        state.History = [];
        state.Combat = null;
        state.Hero!.Position = [3,3];
        state.Hero.Inventory = [];
        state.Map = new() { Chunks = [new() { Theme = "plains", Populated = true,
            Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }], Rooms = [], Entities = [],
            Objects = [new() { Name = "Engraved pedestal", Symbol = "[P]", Position = [4,3], AiInfo = [
                "The plainly legible public inscription reads: CEDAR-47. It identifies the workshop's matching replacement part; reading it needs no check and changes nothing.",
                "The pedestal has been dusted.", "The stone base remains stable.", "A passerby removed a dry leaf.",
                "The original inscription remains intact and legible; its exact wording is recorded in the older notes."] }] };
        string before = JsonSerializer.Serialize(state.Map.Objects);
        string request = "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(state, [(3,3),(4,3)])
            .MinimalState("Read the intact inscription on the pedestal aloud. I only inspect it and do not operate anything.");
        Assert.DoesNotContain("CEDAR-47", request);
        Assert.Contains("aiInfoOmitted", request);
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, request);
        output.WriteLine(reply.ToolTrace);
        output.WriteLine(reply.RawResponse);
        Assert.Contains("MCP get_world_state →", reply.ToolTrace);
        Assert.Contains("CEDAR-47", reply.ToolTrace);
        var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse, retainHistory: true);
        Assert.Contains(parsed.HistoryTexts, text => text.Contains("CEDAR-47", StringComparison.Ordinal));
        Assert.Equal(before, JsonSerializer.Serialize(state.Map.Objects));
        Assert.Empty(state.Hero.Inventory);
        Assert.Empty(state.Narrative.CurrentArc);
        Assert.False(state.Combat?.Active == true);
    }

    [Fact]
    public async Task ArchivedPassageAgreementIsHonoredByANewKeeperWithoutHistory()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        state.World = new() { Place = "Ferry checkpoint" };
        state.Map = new()
        {
            Chunks = [new() { Theme = "plains", Populated = true, Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }],
            Rooms = [], Objects = [], Furniture = [],
            Doors = [new() { From = [10, 8], To = [11, 8], IsDoor = true, IsDoorOpen = false }],
            Entities = [new() { Name = "Tarin", Symbol = "TAR", Position = [10, 8], Hp = "12/12",
                AiInfo = ["The peaceful new gatekeeper honors the recorded ferry agreements of his predecessor. Verify the existing agreement before deciding whether to charge; he can open the gate beside him."] }]
        };
        state.Hero!.Position = [9, 8];
        state.Hero.Inventory = [new() { Name = "Carved token", Quantity = 1 }, new() { Name = "Ration", Quantity = 3 }];
        state.Combat = null;
        state.History = [];
        state.Narrative = new()
        {
            Npcs = Enumerable.Range(0, 300).Select(i => new NpcRecord
            {
                Name = $"Resident {i}", Met = true, Notes = "An ordinary past acquaintance."
            }).ToList()
        };
        state.Narrative.Npcs[17] = new()
        {
            Name = "Mira", Met = true, Notes = "The retired ferry clerk.",
            Memory = ["The hero already paid for the carved token under the CEDAR-42 agreement: unlimited free passage for this hero, honored by replacement gatekeepers. The token is shown, never consumed or surrendered."]
        };
        var reloaded = new Storage();
        reloaded.ApplySavedState(JsonSerializer.Serialize(state));
        string action = "I show the carved token to the new ferry keeper. Please check the agreement from my earlier visit and let me through on its terms.";
        var visible = Enumerable.Range(1, 20).SelectMany(c => Enumerable.Range(1, 15).Select(r => (c, r))).ToHashSet();
        string request = "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(reloaded.WorldState, visible).MinimalState(action);
        Assert.DoesNotContain("CEDAR-42", request);
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", reloaded, request);
        output.WriteLine(reply.ToolTrace);
        output.WriteLine(reply.RawResponse);
        Assert.Contains("MCP get_world_state →", reply.ToolTrace);
        Assert.Contains("CEDAR-42", reply.ToolTrace);
        PromptScenario.ApplyAndParse(reloaded, reply.RawResponse, retainHistory: true);
        var after = reloaded.WorldState;
        Assert.True(after.Map.Doors![0].IsDoorOpen);
        Assert.Equal(1, after.Hero!.Inventory.Single(i => i.Name == "Carved token" && i.Deleted != true).Quantity);
        Assert.Equal(3, after.Hero.Inventory.Single(i => i.Name == "Ration" && i.Deleted != true).Quantity);
        Assert.False(after.Combat?.Active == true);
        Assert.Empty(after.Narrative!.CurrentArc);
        Assert.Contains(after.Narrative.Npcs[17].Memory!, memory => memory.Contains("CEDAR-42"));
    }

    [Fact]
    public async Task PopulationRetrievesTruncatedOldPuzzleBeforePlacingItsMechanism()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        state.Narrative = new()
        {
            PlotThreads = Enumerable.Range(0, 200).Select(i => new PlotThread
            {
                Name = $"Completed errand {i}", Status = "completed", Description = "An unrelated delivery was completed."
            }).ToList()
        };
        state.Narrative.PlotThreads[7] = new()
        {
            Name = "Drowned lock", Status = "active",
            Description = string.Concat(Enumerable.Repeat("The old chamber bears traces of generations of patient repair. ", 30)),
            DmNotes = "The chamber contains a sluice control mechanism. Only a silver lantern opens the sluice; fire cannot replace it. The lantern is elsewhere; do not create it here."
        };
        var chunk = new MapChunk { X = 1, Y = 0, Theme = "building", Purpose = "Drowned lock: place its established sluice control mechanism, preserving its exact requirement. No creatures or portable keys." };
        state.Map.Cols = 40;
        state.Map.Chunks = [new() { X = 0, Y = 0, Theme = "building", Populated = true }, chunk];
        state.Map.Rooms = [new() { Name = "Entrance", Positions = [[3, 3]] }, new() { Name = "Chamber", Positions = [[23, 3], [24, 3], [23, 4], [24, 4]] }];
        state.Map.Entities = [];
        state.Map.Objects = [];
        state.Map.Doors = [];
        state.Map.Furniture = [];
        state.Hero.Position = [3, 3];
        var context = new AiContextBuilder(state);
        string request = "lang:en\nspeechEnabled:false\n" + context.PopulationState(chunk) + "\n" + context.LocationPlan(chunk) + "\n" + context.ChunkState(chunk);
        Assert.DoesNotContain("silver lantern", request, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("truncated; retrieve full record", request);
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "PopulateChunk", storage, request);
        output.WriteLine(reply.ToolTrace);
        output.WriteLine(reply.RawResponse);
        Assert.Contains("get_world_state", reply.ToolTrace);
        PromptScenario.ApplyAndParse(storage, reply.RawResponse);
        string populated = JsonSerializer.Serialize(state.Map);
        Assert.Contains("silver lantern", populated, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(state.Map.Entities!);
        Assert.Equal(new[] { 3, 3 }, state.Hero.Position);
        Assert.All(state.Map.Objects!, item => Assert.Contains(state.Map.Rooms[1].Positions!, cell => cell.SequenceEqual(item.Position)));
        Assert.Equal(200, state.Narrative.PlotThreads.Count);
    }

}
