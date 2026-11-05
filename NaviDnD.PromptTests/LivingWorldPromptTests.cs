using NaviDnD.Clients;
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
        ws.Map = new() { Chunks = [new() { Theme = "forest", Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }], Entities = [], Objects = [], Rooms = [], Doors = [] };
        ws.Hero!.Position = [10, 8];
        ws.Hero.VisionFt = null;
        ws.Combat = null;
        ws.History = [];
        storage.ApplyUpdateWorldState("""
        {"time":{"day":1,"partOfDay":"morning","totalRounds":1},"narrative":{"plotThreads":[
          {"name":"Пропавший груз","status":"rumor","description":"В деревне пропал груз. Герой не соглашался помогать.","dmNotes":"Груз остался в деревне; лес с ним не связан."},
          {"name":"Лесной обход","status":"hidden","hidden":true,"dmNotes":"Место: лесная тропа. Лесничий сейчас идёт проверять повреждённый мостик у ручья. Через полчаса он дойдёт и начнёт ремонт независимо от героя; героя не ищет."}
        ]}}
        """);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            new AiContextBuilder(ws).MinimalState(action));
        output.WriteLine($"Scenario={scenario}, seconds={watch.Elapsed.TotalSeconds:F1}\n{reply.RawResponse}");
        var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse);
        Assert.NotEmpty(parsed.HistoryTexts);
        Assert.NotEqual(true, ws.Hero.Dead);
        Assert.False(ws.Combat?.Active == true);
        output.WriteLine("State: " + System.Text.Json.JsonSerializer.Serialize(new { ws.Time, ws.Map.Entities, ws.Narrative }));
    }
}
