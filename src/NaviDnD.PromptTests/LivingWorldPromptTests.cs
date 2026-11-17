using NaviDnD.Clients;
using NaviDnD.Data.Models;
using System.Text.Encodings.Web;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class LivingWorldPromptTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("wait", "Сижу тихо у лесной тропы и жду сколько потребуется, пока не увижу животное.")]
    [InlineData("independence", "Не хочу заниматься пропавшим грузом. Сажусь у ручья, умываюсь и слушаю лес.")]
    [InlineData("initiative", "Отдыхаю у тропы час, ни с кем не заговариваю.")]
    public async Task QuietScene_ProducesApplicableNarrative(string scenario, string action)
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        var ws = storage.WorldState;
        ws.Map = new() { Chunks = [new() { Theme = "forest", Purpose = "A quiet forest path beside a stream", Terrain = Enumerable.Repeat("..........~.........", 15).ToList(), Populated = true }], Entities = [], Objects = [], Rooms = [], Doors = [] };
        ws.World = new() { Place = "Лесная тропа" };
        ws.Narrative = new() { World = "Лесная окраина деревни; обычная жизнь продолжается независимо от путника." };
        ws.Hero!.Position = [10, 8];
        ws.Hero.VisionFt = null;
        ws.Combat = null;
        ws.History = [];
        storage.ApplyUpdateWorldState("""
        {"time":{"day":1,"partOfDay":"morning","totalRounds":1},"narrative":{"plotThreads":[
          {"name":"Пропавший груз","status":"слух","description":"В деревне пропал груз. Герой не соглашался помогать.","dmNotes":"Груз остался в деревне; лес с ним не связан."},
          {"name":"Лесной обход","status":"неизвестно","hidden":true,"dmNotes":"Лесная тропа: лесничий придёт проверить ручей в раунде 301; начнёт расчистку веток независимо от героя, помощи не просит."}
        ]}}
        """);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            "lang:ru\n" + new AiContextBuilder(ws, Enumerable.Range(1, 20).SelectMany(c => Enumerable.Range(1, 15).Select(r => (c, r))).ToHashSet()).MinimalState(action));
        output.WriteLine($"Scenario={scenario}, seconds={watch.Elapsed.TotalSeconds:F1}\n{reply.RawResponse}");
        var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse, retainHistory: true);
        Assert.NotEmpty(parsed.HistoryTexts);
        Assert.NotEqual(true, ws.Hero.Dead);
        Assert.False(ws.Combat?.Active == true);
        Assert.Empty(ws.Narrative!.CurrentArc);
        if (scenario == "independence")
            Assert.Contains(ws.Narrative.PlotThreads[0].Status, new[] { PlotThreadStatuses.Rumor, PlotThreadStatuses.Declined });
        else Assert.Equal(PlotThreadStatuses.Rumor, ws.Narrative.PlotThreads[0].Status);
        Assert.DoesNotContain(ws.Map.Entities ?? [], e => e.Triggers?.Spotted != null);
        if (scenario == "wait")
            Assert.Contains(ws.Map.Entities ?? [], e => e.Deleted != true && e.Hidden != true
                && e.Position is { Count: >= 2 } p && ws.Map.TerrainAt(p[0], p[1]) is { BlocksMove: false }
                && !p.SequenceEqual(ws.Hero.Position!));
        if (scenario == "initiative")
        {
            // The GM may pause at the planned arrival so the player can react instead of skipping it.
            Assert.True(ws.Time.TotalRounds >= 301 || ws.Time.PartOfDay != "morning" || ws.Time.Day > 1);
            Assert.NotEqual("Лесная тропа: лесничий придёт проверить ручей в раунде 301; начнёт расчистку веток независимо от героя, помощи не просит.", ws.Narrative.PlotThreads[1].DmNotes);
            if (ws.Map.Entities?.Any(e => e.Deleted != true) == true)
            {
                var followup = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
                    "lang:ru\n" + new AiContextBuilder(ws, Enumerable.Range(1, 20).SelectMany(c => Enumerable.Range(1, 15).Select(r => (c, r))).ToHashSet())
                        .MinimalState("Киваю лесничему: «Как тебя зовут? Часто приходится расчищать ручей?»"));
                output.WriteLine("Follow-up: " + followup.RawResponse);
                var conversation = PromptScenario.ApplyAndParse(storage, followup.RawResponse, retainHistory: true);
                Assert.NotEmpty(conversation.HistoryTexts);
                Assert.Contains(ws.Narrative.Npcs, npc => npc.Met == true && npc.Memory?.Count > 0);
                Assert.Contains(ws.Narrative.Npcs, npc => npc.Met == true && ws.Map.Entities.Any(e => e.Deleted != true && e.Name == npc.Name));
                Assert.Empty(ws.Narrative.CurrentArc);
                Assert.False(ws.Combat?.Active == true);
            }
        }
        output.WriteLine("State: " + System.Text.Json.JsonSerializer.Serialize(new { ws.Time, ws.Map.Entities, ws.Narrative }, new System.Text.Json.JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
