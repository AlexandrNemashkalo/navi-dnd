using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// РЕАЛЬНЫЕ вызовы Claude — см. класс SendActionPromptTests для общих правил (платно, медленно,
// запускать редко и осознанно).
public class InventoryPromptTests
{
    private readonly ITestOutputHelper _output;

    public InventoryPromptTests(ITestOutputHelper output) => _output = output;

    // Тот же сценарий, что и NaviDnD.UiTests.InventoryScenarioTests — герой убирает лук и берёт меч
    // в руку. Обычное (не триггерное) действие — MinimalState, как это реально шлёт игра.
    [Fact]
    public async Task EquipSword_UnequipsBow_AndEquipsSwordInHand()
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        var bow = storage.WorldState.Hero!.Inventory!.First(i => i.Name == "Длинный лук");
        var sword = storage.WorldState.Hero!.Inventory!.First(i => i.Name == "Длинный меч");
        Assert.True(bow.EquipmentedSlot is { Count: > 0 }, "Предусловие сценария: лук должен быть изначально экипирован.");
        Assert.True(sword.EquipmentedSlot is not { Count: > 0 }, "Предусловие сценария: меч должен быть изначально НЕ экипирован.");

        var ctx = new AiContextBuilder(storage.WorldState);
        string userMessage = ctx.MinimalState("убираю лук за спину, беру длинный меч в руку");

        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, userMessage);
        _output.WriteLine("Ответ модели:\n" + result.RawResponse);

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Смена экипировки должна быть описана в history.");

        var bowAfter = storage.WorldState.Hero!.Inventory!.First(i => i.Name == "Длинный лук");
        var swordAfter = storage.WorldState.Hero!.Inventory!.First(i => i.Name == "Длинный меч");

        Assert.True(bowAfter.EquipmentedSlot is not { Count: > 0 }, "Лук должен быть снят (equipmentedSlot пуст).");
        Assert.True(swordAfter.EquipmentedSlot is { Count: > 0 }, "Меч должен быть экипирован в руку (equipmentedSlot не пуст).");

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "Inventory", action: "SendAction");
    }
}
