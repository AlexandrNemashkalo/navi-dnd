using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// РЕАЛЬНЫЕ вызовы Claude — см. класс SendActionPromptTests для общих правил (платно, медленно,
// запускать редко и осознанно). Проверяет фикс реального бага из продакшен-лога: ИИ описал «подхожу
// и открываю дверь», хотя герой не двигался и был в двух клетках от двери — движок это никак не
// проверял, промпт полагался на то, что ИИ сам не будет придумывать несуществующее сближение.
// Теперь § КАРТА И ОБЪЕКТЫ учит вызывать open_door(id)/close_door(id) — тул сам меряет дистанцию
// (Chebyshev ≤1 до from/to) и либо возвращает патч, либо причину отказа.
public class DoorInteractionPromptTests
{
    private readonly ITestOutputHelper _output;

    public DoorInteractionPromptTests(ITestOutputHelper output) => _output = output;

    // Герой на [1,1], дверь между [3,3]/[4,3] — далеко (Chebyshev 2/3). open_door должен отказать,
    // и ИИ не должен резолвить открытие сам поверх отказа тула.
    [Fact]
    public async Task OpenDoor_WhenHeroIsFar_DoesNotFabricateSuccess()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var doorBefore = storage.WorldState.Map.Doors!.Single();
        Assert.False(doorBefore.IsDoorOpen, "Предусловие: дверь изначально закрыта.");

        var ctx = new AiContextBuilder(storage.WorldState);
        string message = ctx.MinimalState("подхожу и открываю дверь");
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, message);
        _output.WriteLine("Ответ модели (дверь далеко):\n" + result.RawResponse);

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Отказ/уточнение должно быть описано в history.");

        var doorAfter = storage.WorldState.Map.Doors!.Single();
        Assert.NotEqual(true, doorAfter.IsDoorOpen);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "DoorTooFar", action: "SendAction");
    }

    // Герой стоит вплотную к двери (id:0, from:[3,3]) — open_door должен вернуть патч, ИИ должен
    // его применить.
    [Fact]
    public async Task OpenDoor_WhenHeroIsAdjacent_OpensIt()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        storage.ApplyUpdateWorldState("""{"hero":{"position":[3,3]}}""");

        var ctx = new AiContextBuilder(storage.WorldState);
        string message = ctx.MinimalState("открываю дверь передо мной");
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, message);
        _output.WriteLine("Ответ модели (дверь рядом):\n" + result.RawResponse);

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Открытие двери должно быть описано в history.");

        var doorAfter = storage.WorldState.Map.Doors!.Single();
        Assert.True(doorAfter.IsDoorOpen, "Дверь рядом с героем должна быть открыта.");

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "DoorAdjacent", action: "SendAction");
    }
}
