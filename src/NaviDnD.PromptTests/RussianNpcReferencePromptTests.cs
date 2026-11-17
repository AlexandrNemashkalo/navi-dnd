using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class RussianNpcReferencePromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task InflectedNpcReferenceRetrievesOfferDetailsWithoutAcceptingErrand()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        state.World = new() { Place = "Деревенская площадь" };
        state.History = [];
        state.Combat = null;
        state.Hero!.Position = [9, 8];
        state.Hero.Inventory = [];
        string hp = state.Hero.Hp;
        state.Narrative = new()
        {
            PlotThreads = [new() { Name = "Письмо Ивана", Status = PlotThreadStatuses.Offered,
                Description = "Иван предлагает доставку письма." }]
        };
        state.Map = new()
        {
            Chunks = [new() { Theme = "plains", Populated = true,
                Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }],
            Rooms = [], Doors = [], Furniture = [], Objects = [],
            Entities = [new() { Name = "Иван", Symbol = "IVA", Position = [10, 8], Hp = "10/10",
                AiInfo = ["Предлагает доставить запечатанное письмо травнице Агафье в соседнюю деревню Берестье, плата после доставки 2 серебряных; герой ещё не согласился."] }]
        };
        const string action = "Спрашиваю Ивана, кому и куда нужно доставить письмо и когда заплатят. Пока только уточняю условия, не принимаю поручение.";
        var visible = Enumerable.Range(1, 20).SelectMany(c => Enumerable.Range(1, 15).Select(r => (c, r))).ToHashSet();
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            "lang:ru\nspeechEnabled:false\n" + new AiContextBuilder(state, visible).MinimalState(action));
        output.WriteLine(reply.ToolTrace);
        output.WriteLine(reply.RawResponse);
        Assert.False(reply.RollDiceTriggered);
        var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse, retainHistory: true);
        Assert.NotEmpty(parsed.HistoryTexts);
        string publicKnowledge = string.Join(" ", state.History.Select(h => h.Text)) + " "
            + string.Join(" ", state.Narrative.PlotThreads.Where(t => t.Deleted != true && t.Hidden != true)
                .Select(t => (t.Description ?? "") + " " + string.Join(" ", t.Steps ?? [])));
        Assert.Matches(@"(?i)\bБересть[еяю]\b", publicKnowledge);
        Assert.Matches(@"(?i)\bАгафь[яеию]\b", publicKnowledge);
        Assert.Matches(@"(?i)после\s+(?:успешной\s+)?доставки|по\s+(?:факту\s+)?доставке|когда.{0,60}достав", publicKnowledge);
        Assert.Empty(state.Narrative.CurrentArc);
        Assert.DoesNotContain(state.Narrative.PlotThreads, t => t.Deleted != true && t.Status == PlotThreadStatuses.Active);
        Assert.Equal(PlotThreadStatuses.Offered, Assert.Single(state.Narrative.PlotThreads,
            t => t.Name == "Письмо Ивана" && t.Deleted != true).Status);
        Assert.Empty(state.Hero.Inventory);
        Assert.Equal(new[] { 9, 8 }, state.Hero.Position);
        Assert.Equal(hp, state.Hero.Hp);
        Assert.False(state.Combat?.Active == true);
    }
}
