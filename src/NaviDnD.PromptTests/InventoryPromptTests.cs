using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// REAL calls Claude - see class SendActionPromptTests for general rules (paid, slow, run rarely and
// deliberately).
public class InventoryPromptTests
{
    private readonly ITestOutputHelper _output;

    public InventoryPromptTests(ITestOutputHelper output) => _output = output;

    // The same scenario as NaviDnD.UiTests.InventoryScenarioTests - the hero puts away the bow and takes the
    // sword in his hand. The normal (non-trigger) action is MinimalState, as the game actually sends it.
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
    [Fact]
    public async Task NearbyObjectInspectionUsesProvidedNotesWithoutFetchingWorldState()
    {
        var storage = PromptScenario.LoadUiTestFixture("opportunity_attack_game.json");
        storage.WorldState.Combat = null;
        storage.WorldState.Map.Entities = [];
        storage.WorldState.Map.Objects = [new() { Name = "Каменная чаша", Symbol = "(C)", Position = [4,3],
            AiInfo = ["На дне чаши выгравирована звезда с семью лучами. Чаша сухая, обычный камень, магии нет."] }];
        var ctx = new NaviDnD.Clients.AiContextBuilder(storage.WorldState, [(3,3), (4,3)]);
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            ctx.MinimalState("осматриваю чашу рядом: что на её дне?"));
        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.DoesNotContain("get_world_state →", result.ToolTrace);
        Assert.Contains(parsed.HistoryTexts, t => t.Contains("звезд", StringComparison.OrdinalIgnoreCase)
            || t.Contains("звёзд", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("10/10", storage.WorldState.Hero!.Hp);
    }

}
