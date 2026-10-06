using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// РЕАЛЬНЫЕ вызовы Claude — платно и медленно (секунды на тест), поэтому проект НЕ входит в общий
// прогон решения. Запускать осознанно и РЕДКО — только когда меняешь конкретный systemPrompt.md и
// хочешь проверить/обновить мок под изменение, не как часть обычной работы:
//   dotnet test NaviDnD.PromptTests
// Чтобы обновить канонический мок для NaviDnD.UiTests из этого же прогона:
//   NAVIDND_RECORD_PROMPTS=1 dotnet test NaviDnD.PromptTests --filter OnStepTrap
public class SendActionPromptTests
{
    private readonly ITestOutputHelper _output;

    public SendActionPromptTests(ITestOutputHelper output) => _output = output;

    // Тот же сценарий, что и NaviDnD.UiTests.TriggersScenarioTests.OnStepTrap_MovingOntoIt_FiresAiTrigger —
    // герой наступает на "Капкан" (triggers.onStep, once:true).
    [Fact]
    public async Task OnStepTrap_DealsDamageAndRemovesTrap()
    {
        var storage = PromptScenario.LoadUiTestFixture("trap_game.json");
        var trap = storage.WorldState.Map.Objects![0]; // "Капкан" — единственный объект в фикстуре
        int initialHp = ParseHp(storage.WorldState.Hero!.Hp);

        var ctx = new AiContextBuilder(storage.WorldState);
        string userMessage = ctx.TriggerState([(trap, "onStep")], [], []);

        string response = await PromptScenario.CallRealAiAsync("Dnd5e", "SendAction", storage, userMessage);
        _output.WriteLine("Ответ модели:\n" + response);

        var result = PromptScenario.ApplyAndParse(storage, response);

        Assert.True(result.HistoryTexts.Count > 0, "Ответ не содержит ни одной записи history.");
        Assert.Contains(result.HistoryTexts, t => t.Contains('[') && t.Contains(']'));

        int finalHp = ParseHp(storage.WorldState.Hero!.Hp);
        Assert.True(finalHp < initialHp, $"HP героя должно было уменьшиться от ловушки: было {initialHp}, стало {finalHp}.");

        Assert.DoesNotContain(storage.WorldState.Map.Objects ?? [], o => o.Name == "Капкан" && o.Deleted != true);

        PromptScenario.RecordAsMock(response, mockResponsesFolder: "Triggers", action: "SendAction");
    }

    // Тот же сценарий, что и NaviDnD.UiTests.TriggersScenarioTests.HiddenObjectOnVisible... — герой
    // приближается на дистанцию видимости к скрытому объекту (triggers.onVisible, once:true).
    [Fact]
    public async Task HiddenObjectOnVisible_RevealsTheObject()
    {
        var storage = PromptScenario.LoadUiTestFixture("hidden_object_game.json");
        var hiddenObject = storage.WorldState.Map.Objects![0]; // "Тайный символ"

        var ctx = new AiContextBuilder(storage.WorldState);
        string userMessage = ctx.TriggerState([(hiddenObject, "onVisible")], [], []);

        string response = await PromptScenario.CallRealAiAsync("Dnd5e", "SendAction", storage, userMessage);
        _output.WriteLine("Ответ модели:\n" + response);

        var result = PromptScenario.ApplyAndParse(storage, response);

        Assert.True(result.HistoryTexts.Count > 0, "Ответ не содержит ни одной записи history — обнаружение скрытого объекта должно быть описано.");

        // Объект либо помечен видимым (hidden:false), либо удалён из списка целиком — оба варианта
        // валидны согласно промпту ("hidden: снять — {id,hidden:false}"), лишь бы объект больше не
        // оставался Hidden:true (иначе триггер по факту не сработал с точки зрения игрока).
        var stillHidden = storage.WorldState.Map.Objects?.Any(o => o.Name == "Тайный символ" && o.Hidden == true) ?? false;
        Assert.False(stillHidden, "Скрытый объект должен был раскрыться (hidden:false) или быть удалён — но остался Hidden:true.");

        PromptScenario.RecordAsMock(response, mockResponsesFolder: "HiddenTrigger", action: "SendAction");
    }

    private static int ParseHp(string hp) => int.Parse(hp.Split('/')[0]);
}
