using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// РЕАЛЬНЫЕ вызовы Claude — см. класс SendActionPromptTests для общих правил (платно, медленно,
// запускать редко и осознанно). Проверяет § Время/Отдых systemPrompt.md: ИИ сам сбрасывает
// time.totalRounds на 1 и двигает time.day при смене partOfDay (движок это не отслеживает —
// см. Storage.ApplyTimeUpdate, там просто присваивание без какой-либо автоматики), а также
// отказывает в отдыхе, пока активен периодический урон.
public class TimeOfDayPromptTests
{
    private readonly ITestOutputHelper _output;

    public TimeOfDayPromptTests(ITestOutputHelper output) => _output = output;

    // Долгий отдых через полночь (Вечер + 2 шага = Ночь → Утро) — цикл проходит через "Ночь", значит
    // day должен вырасти на 1, а totalRounds сброситься на 1 независимо от того, каким большим было
    // значение до отдыха (519 — реалистичное "старое" значение, как в разборе продакшен-лога, из-за
    // которого и появилось это правило).
    [Fact]
    public async Task LongRest_ResetsTotalRounds_AndIncrementsDay_WhenCycleCrossesNight()
    {
        var storage = PromptScenario.LoadUiTestFixture("burning_effect_game.json");
        // id:0 — предзаданный в фикстуре "Горящий", этому сценарию не нужен (горящего героя ИИ
        // обоснованно не даст спокойно уснуть, см. аналогичную заметку в EffectsPromptTests).
        storage.ApplyUpdateWorldState("""
            {"hero":{"hp":"4/10","effects":[{"id":0,"deleted":true}]},
             "time":{"totalRounds":519,"partOfDay":"Вечер","day":3}}
            """);

        var ctx = new AiContextBuilder(storage.WorldState);
        string restMessage = ctx.MinimalState("останавливаюсь на долгий привал, разбиваю лагерь и отдыхаю до утра");
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, restMessage);
        _output.WriteLine("Ответ модели (долгий отдых через полночь):\n" + result.RawResponse);

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Долгий отдых должен быть описан в history.");

        _output.WriteLine($"После отдыха: totalRounds={storage.WorldState.Time.TotalRounds} day={storage.WorldState.Time.Day} partOfDay={storage.WorldState.Time.PartOfDay}");
        Assert.Equal("Утро", storage.WorldState.Time.PartOfDay);
        Assert.Equal(1, storage.WorldState.Time.TotalRounds);
        Assert.Equal(4, storage.WorldState.Time.Day);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "LongRestRoundReset", action: "SendAction");
    }

    // Короткий отдых (Утро + 1 шаг = День, "Ночь" не пересекается — day не меняется). Заодно
    // проверяет асимметрию finite/untilLongRest эффектов при коротком отдыхе (см. § Отдых): "Ушиб"
    // с expiresAtRound должен сняться (отдых по длительности перекрывает 2 раунда), "Истощение"
    // untilLongRest — только на долгом, короткий его не трогает.
    [Fact]
    public async Task ShortRest_AdvancesOneStep_ResetsRound_KeepsUntilLongRestEffect_RemovesFiniteEffect()
    {
        var storage = PromptScenario.LoadUiTestFixture("burning_effect_game.json");
        storage.ApplyUpdateWorldState("""
            {"hero":{"hp":"8/10","effects":[{"id":0,"deleted":true},
              {"name":"Ушиб","description":"Побаливает от удара.","color":[160,160,160],"expiresAtRound":3},
              {"name":"Истощение","description":"Ты вымотан долгим переходом.","color":[160,160,160],"untilLongRest":true}]},
             "time":{"totalRounds":45,"partOfDay":"Утро","day":1}}
            """);

        var ctx = new AiContextBuilder(storage.WorldState);
        string restMessage = ctx.MinimalState("устраиваю короткий привал на час, отдыхаю у стены");
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, restMessage);
        _output.WriteLine("Ответ модели (короткий отдых):\n" + result.RawResponse);

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Короткий отдых должен быть описан в history.");

        _output.WriteLine($"После отдыха: totalRounds={storage.WorldState.Time.TotalRounds} day={storage.WorldState.Time.Day} partOfDay={storage.WorldState.Time.PartOfDay}");
        Assert.Equal("День", storage.WorldState.Time.PartOfDay);
        Assert.Equal(1, storage.WorldState.Time.TotalRounds);
        Assert.Equal(1, storage.WorldState.Time.Day);

        var stillActive = storage.WorldState.Hero!.Effects?.Where(e => e.Deleted != true).Select(e => e.Name).ToList() ?? [];
        Assert.DoesNotContain("Ушиб", stillActive);
        Assert.Contains("Истощение", stillActive);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "ShortRest", action: "SendAction");
    }

    // Активный периодический урон ("Отравлен", без expiresAtRound — бессрочный, худший случай) — по
    // явному дизайн-решению отдых должен быть ЗАПРЕЩЁН целиком, а не разрешён с "додумыванием" урона
    // за пропущенные часы: partOfDay/totalRounds не должны сдвинуться, а сам эффект — остаться.
    [Fact]
    public async Task Rest_BlockedByActiveDamagingPeriodicEffect_RefusesRest()
    {
        var storage = PromptScenario.LoadUiTestFixture("burning_effect_game.json");
        storage.ApplyUpdateWorldState("""
            {"hero":{"hp":"8/10","effects":[{"id":0,"deleted":true},
              {"name":"Отравлен","description":"Яд в крови жжёт вены.","color":[80,180,80],
               "onRound":{"effect":"1 poison damage to hero"}}]},
             "time":{"totalRounds":50,"partOfDay":"Вечер","day":2}}
            """);

        var ctx = new AiContextBuilder(storage.WorldState);
        string restMessage = ctx.MinimalState("останавливаюсь на долгий привал, разбиваю лагерь и отдыхаю до утра");
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, restMessage);
        _output.WriteLine("Ответ модели (отдых при активном яде — ожидаем отказ):\n" + result.RawResponse);

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Отказ в отдыхе должен быть объяснён в history.");

        _output.WriteLine($"После попытки: totalRounds={storage.WorldState.Time.TotalRounds} partOfDay={storage.WorldState.Time.PartOfDay}");
        Assert.Equal("Вечер", storage.WorldState.Time.PartOfDay);
        Assert.Equal(50, storage.WorldState.Time.TotalRounds);

        var stillActive = storage.WorldState.Hero!.Effects?.Where(e => e.Deleted != true).Select(e => e.Name).ToList() ?? [];
        Assert.Contains("Отравлен", stillActive);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "RestBlockedByPoison", action: "SendAction");
    }
}
