using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// REAL calls Claude - see class SendActionPromptTests for general rules (paid, slow, run rarely and
// deliberately). Checks § Time/Rest systemPrompt.md: The AI itself resets time.totalRounds to 1 and moves
// time.day when changing partOfDay (the engine does not track this - see Storage.ApplyTimeUpdate, there is just
// an assignment without any automation), and also denies rest while periodic damage is active.
public class TimeOfDayPromptTests
{
    private readonly ITestOutputHelper _output;

    public TimeOfDayPromptTests(ITestOutputHelper output) => _output = output;

    // Long rest after midnight (Evening + 2 steps = Night → Morning) - the cycle goes through "Night", which
    // means day should increase by 1, and totalRounds should reset by 1, regardless of how large the value was
    // before the rest (519 is a realistic "old" value, as in the production log analysis, which is why this
    // rule appeared).
    [Fact]
    public async Task LongRest_ResetsTotalRounds_AndIncrementsDay_WhenCycleCrossesNight()
    {
        var storage = PromptScenario.LoadUiTestFixture("burning_effect_game.json");
        // id:0 - predefined in the “Burning” fixture, this scenario is not needed (the AI will reasonably not
        // allow the burning hero to sleep peacefully, see a similar note in EffectsPromptTests).
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
        Assert.Equal("morning", storage.WorldState.Time.PartOfDay);
        Assert.Equal(1, storage.WorldState.Time.TotalRounds);
        Assert.Equal(4, storage.WorldState.Time.Day);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "LongRestRoundReset", action: "SendAction");
    }

    // Short rest (Morning + 1 step = Day, “Night” does not intersect - day does not change). At the same time,
    // it checks the asymmetry of finite/untilLongRest effects during a short rest (see § Rest): “Bruise” with
    // expiresAtRound should be removed (rest duration covers 2 rounds), “Exhaustion” untilLongRest - only on a
    // long one, a short one does not affect it.
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
        Assert.Equal("day", storage.WorldState.Time.PartOfDay);
        Assert.Equal(1, storage.WorldState.Time.TotalRounds);
        Assert.Equal(1, storage.WorldState.Time.Day);

        var stillActive = storage.WorldState.Hero!.Effects?.Where(e => e.Deleted != true).Select(e => e.Name).ToList() ?? [];
        Assert.DoesNotContain("Ушиб", stillActive);
        Assert.Contains("Истощение", stillActive);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "ShortRest", action: "SendAction");
    }

    // Active periodic damage ("Poisoned", without expiresAtRound - indefinite, worst case) - according to an
    // obvious design decision, rest should be PROHIBITED entirely, and not allowed with "rethinking" the damage
    // for the missed hours: partOfDay/totalRounds should not move, but the effect itself should remain.
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
        Assert.Equal("evening", storage.WorldState.Time.PartOfDay);
        Assert.Equal(50, storage.WorldState.Time.TotalRounds);

        var stillActive = storage.WorldState.Hero!.Effects?.Where(e => e.Deleted != true).Select(e => e.Name).ToList() ?? [];
        Assert.Contains("Отравлен", stillActive);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "RestBlockedByPoison", action: "SendAction");
    }
}
