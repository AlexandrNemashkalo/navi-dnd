using System.Reflection;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class TravelChoicePromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LocalEntryDoesNotChargeAnotherJourneyOrGrantRest()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        string id = "local-entry-prompt-" + Guid.NewGuid().ToString("N");
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary)
            .GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        worlds[id] = new() { Id = id, Width = 1, Height = 1, Biomes = ["."], Owners = ["."], Roads = ["0"], Rivers = ["0"],
            Chronicle = new() { Places = [new() { Name = "Orchard", Type = WorldPlaceTypes.Village,
                Description = "A peaceful public orchard beside a hamlet, with a shaded path and benches. No danger or urgent obligation awaits the visitor." }] } };
        try
        {
            state.World = new() { Id = id, X = 0, Y = 0, Known = ["Orchard"] };
            state.Map = new();
            state.Narrative = new();
            state.History = [];
            state.Combat = null;
            state.Hero!.Hp = "5/20";
            state.Hero.Inventory = [new() { Name = "Rations", Quantity = 3 }];
            state.Hero.Effects = [new() { Name = "Tired feet", UntilLongRest = true }];
            state.Time = new() { Day = 4, PartOfDay = PartsOfDay.Evening, TotalRounds = 77 };
            state.ScheduledEvents = [new() { Name = "Distant bell", FireAtRound = 200 }];
            Assert.True(LocalPlaceEntry.TryEnter(state, (0,0), out string report));
            // Route metadata lives only in this process; retain the resulting arrival for the MCP fixture.
            state.World = new() { Place = state.World.Place, X = 0, Y = 0, Known = ["Orchard"], Visited = ["Orchard"] };
            string time = System.Text.Json.JsonSerializer.Serialize(state.Time);
            string events = System.Text.Json.JsonSerializer.Serialize(state.ScheduledEvents);
            var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
                "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(state, []).MinimalState(report));
            output.WriteLine(report);
            output.WriteLine(reply.RawResponse);
            var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse);
            Assert.NotEmpty(parsed.HistoryTexts);
            Assert.Equal(time, System.Text.Json.JsonSerializer.Serialize(state.Time));
            Assert.Equal(events, System.Text.Json.JsonSerializer.Serialize(state.ScheduledEvents));
            Assert.Equal("5/20", state.Hero.Hp);
            Assert.Equal(3, Assert.Single(state.Hero.Inventory).Quantity);
            Assert.Contains(state.Hero.Effects, e => e.Deleted != true && e.Name == "Tired feet");
            Assert.Equal("Orchard", state.World.Place);
            Assert.Equal(0, state.World.X);
            Assert.Equal(0, state.World.Y);
            Assert.Empty(state.Narrative.CurrentArc);
            Assert.False(state.Combat?.Active == true);
            Assert.False(reply.RollDiceTriggered);
        }
        finally { worlds.Remove(id); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PeacefulRoadEncounterLeavesRestAndCommitmentToPlayer(bool atDestination)
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        var ws = storage.WorldState;
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary)
            .GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        string id = "travel-choice-" + Guid.NewGuid().ToString("N");
        worlds[id] = new WorldMap { Id = id, Name = "Road", Width = 12, Height = 3,
            Biomes = ["............", "............", "............"],
            Owners = ["............", "............", "............"],
            Roads = ["222222222222", "222222222222", "222222222222"],
            Rivers = ["000000000000", "000000000000", "000000000000"],
            Chronicle = new() { Places = [new() { Name = "Мельница", Type = WorldPlaceTypes.Village, X = 2, Y = 1 }] } };
        try
        {
            ws.World = new() { Id = id, X = 1, Y = 1, Known = ["Мельница"] };
            ws.Map = new();
            ws.History = [];
            ws.Combat = null;
            ws.ScheduledEvents = [];
            ws.Narrative = new() { World = "Тихая обжитая дорога между деревнями. Путники живут своими делами.",
                Style = "Спокойное исследование и разговоры без навязанных обязательств." };
            ws.Hero!.Hp = "5/20";
            ws.Hero.Inventory = [new() { Name = "Дорожный паёк", Quantity = 3 }];
            ws.Hero.Effects = [new() { Name = "Усталые ноги", UntilLongRest = true }];
            ws.Time = new() { Day = 2, PartOfDay = PartsOfDay.Morning, TotalRounds = 1 };
            int targetX = atDestination ? 2 : 10;
            var plan = Assert.IsType<TravelService.Plan>(TravelService.PlanTo(ws, (targetX,1)));
            string report = TravelService.Go(ws, plan, TravelService.Pace.Normal, new PeacefulEncounterRandom());
            Assert.Equal(atDestination ? "Мельница" : null, ws.World.Place);
            // The runtime world above is only a route fixture; the response needs no generated world files.
            ws.World = new() { Place = ws.World.Place ?? "Дорога между деревнями" };
            output.WriteLine(report);
            var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
                "lang:ru\nspeechEnabled:false\n" + new AiContextBuilder(ws, []).MinimalState(report));
            output.WriteLine(reply.RawResponse);
            var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse);
            Assert.NotEmpty(parsed.HistoryTexts);
            Assert.Equal(2, ws.Time.Day);
            Assert.Equal(PartsOfDay.Evening, ws.Time.PartOfDay);
            Assert.Equal("5/20", ws.Hero.Hp);
            Assert.Contains(ws.Hero.Effects, e => e.Deleted != true && e.Name == "Усталые ноги");
            Assert.Equal(2, ws.Hero.Inventory.Single(i => i.Deleted != true && i.Name == "Дорожный паёк").Quantity);
            Assert.Empty(ws.Narrative.CurrentArc);
            Assert.False(ws.Combat?.Active == true);
            Assert.Equal(atDestination ? "Мельница" : "Дорога между деревнями", ws.World.Place);
        }
        finally { worlds.Remove(id); }
    }

    private sealed class PeacefulEncounterRandom : Random
    {
        private int calls;
        public override double NextDouble() => calls++ == 0 ? 0 : 0.4;
    }
}
