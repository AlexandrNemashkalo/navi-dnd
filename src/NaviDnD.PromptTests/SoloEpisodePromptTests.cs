using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class SoloEpisodePromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task NegotiatedPassageRemainsResolvedAfterHistoryIsGone()
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        var ws = storage.WorldState;
        ws.World = new() { Place = "Дорожная застава" };
        ws.Map = new()
        {
            Chunks = [new() { Theme = "plains", Populated = true, Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }],
            Rooms = [], Objects = [], Furniture = [],
            Doors = [new() { From = [10, 8], To = [11, 8], IsDoor = true, IsDoorOpen = false,
                AiInfo = ["Дорожный шлагбаум. Рада поднимет его после мирной договорённости; действует её решение, повторной платы нет."] }],
            Entities = [new() { Name = "Рада", Symbol = "RAD", Position = [10, 8], Hp = "12/12", AiInfo = ["Нейтральная смотрительница заставы, стоит у шлагбаума, не нападает. Полные мотивы в записи NPC."] }]
        };
        ws.Hero!.Position = [9, 8];
        ws.Hero.Inventory = [new() { Name = "Дорожный паёк", Quantity = 3 }];
        ws.Combat = null;
        ws.History = [];
        ws.Narrative = new()
        {
            Style = "Спокойное приключение; внимание к людям и последствиям, без навязанных сражений.",
            Npcs = [new() { Name = "Рада", Met = true, Attitude = "neutral", Notes = "Смотрительница заставы.",
                DmNotes = "Рада задерживает путников из-за нехватки еды на вечернее дежурство. Открыто расскажет причину на вопрос. Добровольная передача одного пайка решит её проблему: она откроет шлагбаум и обещает этому герою свободный проход впредь. Не требует броска за передачу еды и не скрывает дополнительного требования." }],
            PlotThreads = [new() { Name = "Закрытая застава", Status = "слух", Description = "Проход через заставу закрыт; причина неизвестна.",
                DmNotes = "Дорожная застава: нехватка одного пайка у Рады. Мирная договорённость открывает проход; после решения повторная плата не нужна." }]
        };

        await Act(storage, "Спрашиваю Раду: почему закрыт проход и можно ли решить дело без драки?");
        Assert.Empty(ws.Narrative.CurrentArc);
        Assert.False(ws.Combat?.Active == true);
        Assert.Equal(3, ws.Hero.Inventory.Single(i => i.Deleted != true).Quantity);
        Assert.NotEmpty(ws.Narrative.Npcs[0].Memory!);

        await Act(storage, "Хочу договориться о свободном проходе. Добровольно отдаю Раде один дорожный паёк: «Вот еда на дежурство. Тогда мы договорились, и впредь я прохожу свободно?»");
        Assert.False(ws.Combat?.Active == true);
        Assert.Equal(2, ws.Hero.Inventory.Single(i => i.Deleted != true).Quantity);
        Assert.True(ws.Map.Doors[0].IsDoorOpen);
        Assert.Empty(ws.Narrative.CurrentArc);
        Assert.NotEqual("слух", ws.Narrative.PlotThreads[0].Status);
        Assert.NotEmpty(ws.Narrative.PlotThreads[0].Steps!);
        string resolvedStatus = ws.Narrative.PlotThreads[0].Status!;

        // The next visit has no conversation transcript: only the actual saved consequences survive.
        ws.History = [];
        string saved = JsonSerializer.Serialize(ws, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var reloaded = new Storage();
        reloaded.ApplySavedState(saved);
        reloaded.WorldState.Time.Day++;
        await Act(reloaded, "На следующий день снова здороваюсь с Радой и спрашиваю, действует ли наша договорённость о проходе.");
        var after = reloaded.WorldState;
        Assert.True(after.Map.Doors![0].IsDoorOpen);
        Assert.Equal(2, after.Hero!.Inventory.Single(i => i.Deleted != true).Quantity);
        Assert.Equal(resolvedStatus, after.Narrative!.PlotThreads[0].Status);
        Assert.Single(after.Narrative.PlotThreads);
        Assert.Empty(after.Narrative.CurrentArc);
        Assert.False(after.Combat?.Active == true);
    }

    private async Task Act(Storage storage, string action)
    {
        var visible = Enumerable.Range(1, 20).SelectMany(c => Enumerable.Range(1, 15).Select(r => (c, r))).ToHashSet();
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            "lang:ru\nspeechEnabled:false\n" + new AiContextBuilder(storage.WorldState, visible).MinimalState(action));
        output.WriteLine(action + "\n" + reply.RawResponse);
        PromptScenario.ApplyAndParse(storage, reply.RawResponse, retainHistory: true);
    }
}
