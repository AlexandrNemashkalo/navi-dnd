using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// REAL calls Claude - see class SendActionPromptTests for general rules (paid, slow, run rarely and
// deliberately). Checks the entire life cycle of status effects (§ CONDITIONS systemPrompt.md): AI actually
// adds hero.effects by the player’s free action (not only by trigger/combat, like the rest of PromptTests)),
// then correctly removes the effect - manually (Prone) or at the end of the round (round-transition).)
public class EffectsPromptTests
{
    private readonly ITestOutputHelper _output;

    public EffectsPromptTests(ITestOutputHelper output) => _output = output;

    // Lying prone (Prone) - effect without expiresAtRound/untilLongRest (can only be removed by an explicit
    // action of the player, see § CONDITIONS "Indefinitely/until manual removal"). Two real calls in a row on
    // ONE storage:, first the hero lies prone, then gets up - the effect should appear and disappear in
    // hero.effects, and not just be mentioned in the narrative.
    [Fact]
    public async Task Prone_AddedWhenHeroGoesProne_AndRemovedWhenHeroStandsUp()
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        Assert.True(storage.WorldState.Hero!.Effects is not { Count: > 0 },
            "Предусловие сценария: у героя изначально не должно быть активных эффектов.");

        var ctx = new AiContextBuilder(storage.WorldState);

        // Step 1: the hero deliberately falls on his face to protect himself from arrows.
        string fallMessage = ctx.MinimalState("бросаюсь на землю и лежу ничком, чтобы укрыться от летящих стрел");
        var fallResult = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, fallMessage);
        _output.WriteLine("Ответ модели (падаю ничком):\n" + fallResult.RawResponse);

        var fallParsed = PromptScenario.ApplyAndParse(storage, fallResult.RawResponse);
        Assert.True(fallParsed.HistoryTexts.Count > 0, "Падение ничком должно быть описано в history.");

        var activeEffects = storage.WorldState.Hero!.Effects?.Where(e => e.Deleted != true).ToList() ?? [];
        Assert.True(activeEffects.Count > 0,
            "После намеренного падения ничком у героя должен появиться активный эффект в hero.effects (не только текст в history).");
        var prone = activeEffects[0];
        _output.WriteLine($"Добавленный эффект: \"{prone.Name}\" color={(prone.Color != null ? string.Join(",", prone.Color) : "null")} " +
                           $"expiresAtRound={prone.ExpiresAtRound?.ToString() ?? "null"} untilLongRest={prone.UntilLongRest}");
        Assert.False(string.IsNullOrWhiteSpace(prone.Name), "У добавленного эффекта должно быть название.");
        Assert.True(prone.Color is { Count: 3 }, "У добавленного эффекта должен быть color:[R,G,B] (обязателен по промпту).");

        // Step 2: the hero stands up - the effect must be removed.
        string standMessage = ctx.MinimalState("встаю на ноги");
        var standResult = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, standMessage);
        _output.WriteLine("Ответ модели (встаю):\n" + standResult.RawResponse);

        var standParsed = PromptScenario.ApplyAndParse(storage, standResult.RawResponse);
        Assert.True(standParsed.HistoryTexts.Count > 0, "Вставание на ноги должно быть описано в history.");

        var stillActive = storage.WorldState.Hero!.Effects?.Where(e => e.Deleted != true).ToList() ?? [];
        Assert.DoesNotContain(stillActive, e => e.Name == prone.Name);

        PromptScenario.RecordAsMock(fallResult.RawResponse, mockResponsesFolder: "ProneEffect", action: "SendAction");
    }

    // Short-term buff (several rounds) with onExpire - we check exactly that part of the life cycle that the
    // Prone-test does not affect: the engine detects the expiration of the round and sends a separate
    // round-transition call, the real AI is obliged to remove the effect on it.  The effect was added directly
    // by a patch (not through a free action) - deliberately: two attempts to get it through the narrative
    // ("battle cry" / "priest ally reads a prayer") were CORRECTLY rejected by the AI, because the hero does
    // not have such an ability, and there is no ally on the map - this is not a bug, but a working one §
    // ABSOLUTE PROHIBITIONS ("do not think for the player/scene"). Here the goal is not to check “adding”, but
    // rather withdrawal after the round, so the source of the effect is not important for the test.
    [Fact]
    public async Task TemporaryBuff_WithRoundLimit_IsRemovedAfterItExpires()
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        int currentRound = storage.WorldState.Time.TotalRounds;
        int expiresAtRound = currentRound + 2;
        string effectPatch = """
            {"hero":{"effects":[{"name":"Благословение","description":"Чувствуешь прилив уверенности.","color":[80,150,220],
              "expiresAtRound":EXPIRES_AT_ROUND,"onExpire":{"effect":"Благословение рассеивается — опиши коротко, удали эффект."}}]}}
            """.Replace("EXPIRES_AT_ROUND", expiresAtRound.ToString());
        storage.ApplyUpdateWorldState(effectPatch);

        var buff = storage.WorldState.Hero!.Effects!.Single(e => e.Deleted != true);
        _output.WriteLine($"Эффект посеян напрямую: \"{buff.Name}\" expiresAtRound={buff.ExpiresAtRound}.");

        // Rewind time beyond the duration of the effect and run the engine check for the round.
        storage.WorldState.Time.TotalRounds = expiresAtRound + 1;
        var bundle = storage.TakeRoundBundle();
        Assert.NotNull(bundle);
        Assert.Contains(bundle.ExpiredEffects, e => e.Effect.Name == "Благословение");

        var ctx = new AiContextBuilder(storage.WorldState);
        string roundMessage = ctx.RoundTransitionState(bundle);
        var expireResult = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, roundMessage);
        _output.WriteLine("Ответ модели (истечение эффекта):\n" + expireResult.RawResponse);

        var expireParsed = PromptScenario.ApplyAndParse(storage, expireResult.RawResponse);
        Assert.True(expireParsed.HistoryTexts.Count > 0, "Истечение эффекта должно быть описано в history.");

        var stillActive = storage.WorldState.Hero!.Effects?.Where(e => e.Deleted != true).ToList() ?? [];
        Assert.DoesNotContain(stillActive, e => e.Name == "Благословение");

        PromptScenario.RecordAsMock(expireResult.RawResponse, mockResponsesFolder: "TemporaryBuff", action: "SendAction");
    }

    // Effect on a MONSTER, not only on the hero - Storage.CollectEntityEffects collects map.entities[i].effects
    // in the same way from the very beginning, but this has not been tested by real AI before (all other tests
    // in this file - only hero.effects). At the same time, we check the batching: if in ONE round the effects
    // of both the hero AND the monster are ticking, Storage.TakeRoundBundle() collects them into ONE
    // RoundBundle, and AiContextBuilder.RoundTransitionState generates ONE message for both - which means that
    // the real AI only needs ONE call, and not one per effect owner (important for the number/cost of requests
    // - it should not grow linearly with the number of active periodic effects).
    [Fact]
    public async Task SimultaneousEffects_OnHeroAndMonster_AreCollectedAndResolvedInOneAiCall()
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        var cultist = storage.WorldState.Map.Entities!.Single(e => e.Name == "Культист Мораг"); // id 3, hp "23/33"
        int heroHpBefore = ParseHp(storage.WorldState.Hero!.Hp);
        int cultistHpBefore = ParseHp(cultist.Hp);

        storage.ApplyUpdateWorldState("""
            {"hero":{"effects":[{"name":"Горящий","description":"Пламя лижет кожу.","color":[220,120,40],
              "onRound":{"effect":"1 fire damage to hero"}}]},
             "map":{"entities":[{"id":3,"effects":[{"name":"Отравлен","description":"Яд в крови.","color":[80,180,80],
              "onRound":{"effect":"1 poison damage to this entity"}}]}]}}
            """);

        // The very first call of TakeRoundBundle on a fresh Storage - the grandfather is empty, both onRound-
        // effects without expiresAtRound tick immediately (see Storage.CollectEntityEffects) - we do not rewind
        // the round on purpose, this is not necessary to check batching.
        var bundle = storage.TakeRoundBundle();
        Assert.NotNull(bundle);
        Assert.Equal(2, bundle.OnRoundEffects.Count);
        Assert.Contains(bundle.OnRoundEffects, e => e.OwnerPath == "hero" && e.Effect.Name == "Горящий");
        Assert.Contains(bundle.OnRoundEffects, e => e.OwnerPath == "map.entities" && e.Effect.Name == "Отравлен");

        var ctx = new AiContextBuilder(storage.WorldState);
        string roundMessage = ctx.RoundTransitionState(bundle);
        Assert.Contains("Горящий", roundMessage);
        Assert.Contains("Отравлен", roundMessage);
        _output.WriteLine("Совмещённый round-transition запрос (оба эффекта одним сообщением):\n" + roundMessage);

        // ONE AI call - both effects must resolve in the same response.
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, roundMessage);
        _output.WriteLine("Ответ модели (оба эффекта одним вызовом):\n" + result.RawResponse);

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Тик обоих эффектов должен быть описан в history.");

        int heroHpAfter = ParseHp(storage.WorldState.Hero!.Hp);
        int cultistHpAfter = ParseHp(storage.WorldState.Map.Entities!.Single(e => e.Name == "Культист Мораг" && e.Deleted != true).Hp);

        Assert.True(heroHpAfter < heroHpBefore, $"HP героя должно уменьшиться от «Горящий»: было {heroHpBefore}, стало {heroHpAfter}.");
        Assert.True(cultistHpAfter < cultistHpBefore, $"HP культиста должно уменьшиться от «Отравлен»: было {cultistHpBefore}, стало {cultistHpAfter}.");

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "MultiEffectRound", action: "SendAction");
    }

    // Captured (Grabbed/Restrained) - by prompt (§ hero.speedMax) speed is reset to zero for the entire
    // duration of the effect through BOTH fields (speedMax AND speedLeft), not only one-time speedLeft - the
    // same error that was corrected for Prone (see Prone-test above), should not be repeated here. Freeing
    // usually requires a roll (Athletics/Acrobatics) - by default, ANY check goes through roll_dice, so
    // rollValue:20 (auto-nat.20) guarantees success and the test does not depend on the luck of the model.
    [Fact]
    public async Task Grabbed_SetsSpeedToZero_AndRestoresSpeedWhenHeroBreaksFree()
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        int originalSpeedMax = storage.WorldState.Hero!.SpeedMax ?? 30;
        Assert.True(storage.WorldState.Hero!.Effects is not { Count: > 0 },
            "Предусловие сценария: у героя изначально не должно быть активных эффектов.");

        var ctx = new AiContextBuilder(storage.WorldState);

        // The threat is introduced by the scene, not the player: the player’s words “I was grabbed by a
        // tentacle” without a tentacle on the map are rejected by the GM according to the rules (the world is
        // only what is in the scene). The tentacle is an object on the hero's square with onStep, like a trap:
        // the capture comes from playing a trigger, and its outcome is unique (without a saving throw that the
        // AI could win).
        var heroPos = storage.WorldState.Hero!.Position!;
        storage.ApplyUpdateWorldState($$"""
            {"map":{"objects":[{"symbol":"(T)","position":[{{heroPos[0]}},{{heroPos[1]}}],"name":"Щупальце из трещины","color":[90,120,90],
              "triggers":{"onStep":{"effect":"Склизкое щупальце из трещины в стене обвивает ногу героя и держит: состояние «Схвачен» (скорость 0), без спасброска; вырваться — проверка Атлетики или Акробатики СЛ 12","once":true} } }]} }
            """);
        var tentacle = storage.WorldState.Map.Objects!.Last();
        string grabMessage = ctx.TriggerState([(tentacle, "onStep")], [], []);
        var grabResult = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, grabMessage);
        _output.WriteLine("Ответ модели (хватает щупальце):\n" + grabResult.RawResponse);

        var grabParsed = PromptScenario.ApplyAndParse(storage, grabResult.RawResponse);
        Assert.True(grabParsed.HistoryTexts.Count > 0, "Захват щупальцем должен быть описан в history.");

        var activeEffects = storage.WorldState.Hero!.Effects?.Where(e => e.Deleted != true).ToList() ?? [];
        Assert.True(activeEffects.Count > 0,
            "После захвата у героя должен появиться активный эффект в hero.effects (не только текст в history).");
        var grabbed = activeEffects[0];
        _output.WriteLine($"Добавленный эффект: \"{grabbed.Name}\" speedMax={storage.WorldState.Hero.SpeedMax} speedLeft={storage.WorldState.Hero.SpeedLeft}");
        Assert.False(string.IsNullOrWhiteSpace(grabbed.Name), "У добавленного эффекта должно быть название.");
        Assert.True(grabbed.Color is { Count: 3 }, "У добавленного эффекта должен быть color:[R,G,B] (обязателен по промпту).");

        Assert.Equal(0, storage.WorldState.Hero.SpeedMax);
        Assert.Equal(0, storage.WorldState.Hero.SpeedLeft);

        string breakFreeMessage = ctx.MinimalState("пытаюсь вырваться из хватки щупальца, рвусь изо всех сил");
        var breakResult = await PromptScenario.CallRealAiWithAutoAnswersAsync(
            "Dnd5e", "SendAction", storage, breakFreeMessage, rollValue: 20);
        _output.WriteLine("Ответ модели (вырываюсь):\n" + breakResult.RawResponse);
        Assert.True(breakResult.RollDiceTriggered, "Попытка вырваться — это проверка (Атлетика/Акробатика), должна идти через roll_dice.");

        var breakParsed = PromptScenario.ApplyAndParse(storage, breakResult.RawResponse);
        Assert.True(breakParsed.HistoryTexts.Count > 0, "Попытка вырваться должна быть описана в history.");

        var stillActive = storage.WorldState.Hero!.Effects?.Where(e => e.Deleted != true).ToList() ?? [];
        Assert.DoesNotContain(stillActive, e => e.Name == grabbed.Name);
        Assert.Equal(originalSpeedMax, storage.WorldState.Hero.SpeedMax);

        PromptScenario.RecordAsMock(grabResult.RawResponse, mockResponsesFolder: "GrabbedEffect", action: "SendAction");
    }

    // Long rest - HP is restored, untilLongRest- effects are removed, the time of day is shifted by 2 steps
    // according to the cycle Night→Morning→Day→Evening→Night (see § Prompt system time). The engine does not
    // track rest - it is entirely the responsibility of the AI through the usual SendAction, without a separate
    // game action “rest”. The action is formulated WITHOUT a specific destination time (“until morning”, etc.)
    // so as not to create a conflict between the player’s narrative response and the mechanical rule “+2 steps”
    // - we are testing the rule, and not whether the AI can count the hours until dawn.  Fixture -
    // burning_effect_game.json (empty map.entities), not full_game.json: first attempt on full_game.json The AI
    // CORRECTLY refused to rest - there is a hostile cultist and goblins nearby, there cannot be a safe camp in
    // the midst of a dungeon teeming with enemies, according to the plot (the same class of “refusal according
    // to § ABSOLUTE PROHIBITIONS”, which we have already caught twice above). Here we are checking exactly the
    // mechanics of rest, and not whether the AI will allow you to sleep in the middle of a battle.
    [Fact]
    public async Task LongRest_RestoresHp_RemovesUntilLongRestEffects_AndAdvancesTimeOfDayByTwoSteps()
    {
        var storage = PromptScenario.LoadUiTestFixture("burning_effect_game.json");
        // id:0 - the “Burning” preset in the fixture (not needed for this scenario - the AI will reasonably
        // not allow the burning hero to sleep peacefully) - we remove it with the same patch that adds
        // “Exhaustion”.
        storage.ApplyUpdateWorldState("""
            {"hero":{"hp":"3/10","effects":[{"id":0,"deleted":true},
              {"name":"Истощение","description":"Ты вымотан долгим переходом.","color":[160,160,160],"untilLongRest":true}]},
             "time":{"partOfDay":"Вечер"}}
            """);

        var effectBefore = storage.WorldState.Hero!.Effects!.Single(e => e.Deleted != true);
        Assert.Equal("Истощение", effectBefore.Name);
        Assert.True(effectBefore.UntilLongRest, "Предусловие: эффект должен быть помечен untilLongRest:true.");

        var ctx = new AiContextBuilder(storage.WorldState);
        string restMessage = ctx.MinimalState("останавливаюсь на долгий привал, разбиваю лагерь и отдыхаю");
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, restMessage);
        _output.WriteLine("Ответ модели (долгий отдых):\n" + result.RawResponse);

        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.True(parsed.HistoryTexts.Count > 0, "Долгий отдых должен быть описан в history.");

        int hpAfter = ParseHp(storage.WorldState.Hero!.Hp);
        Assert.True(hpAfter > 4, $"HP должно восстановиться после долгого отдыха: было 4, стало {hpAfter}.");

        var stillActive = storage.WorldState.Hero!.Effects?.Where(e => e.Deleted != true).ToList() ?? [];
        Assert.DoesNotContain(stillActive, e => e.Name == "Истощение");

        _output.WriteLine($"partOfDay после отдыха: {storage.WorldState.Time.PartOfDay} (было «Вечер», ожидаем +2 шага = «Утро»)");
        Assert.Equal("morning", storage.WorldState.Time.PartOfDay);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "LongRest", action: "SendAction");
    }

    private static int ParseHp(string hp) => int.Parse(hp.Split('/')[0]);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestKeepsCurseAndDiseaseAndRemovesOnlyEligibleEffects(bool isLong)
    {
        var storage = PromptScenario.LoadUiTestFixture("burning_effect_game.json");
        storage.WorldState.Hero!.Effects = [
            new() { Name = "Щит", Description = "Временная магическая защита", ExpiresAtRound = storage.WorldState.Time.TotalRounds + 1 },
            new() { Name = "Усталость", Description = "Проходит после долгого отдыха", UntilLongRest = true },
            new() { Name = "Проклятие", Description = "Проклятие чёрного алтаря. Только Снятие проклятия; отдых не помогает." },
            new() { Name = "Болезнь", Description = "Гниющая лихорадка. До магического исцеления; отдых не лечит." }];
        string action = isLong ? "разбиваю безопасный лагерь и отдыхаю восемь часов" : "сижу в безопасном лагере и отдыхаю один час";
        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            new AiContextBuilder(storage.WorldState).MinimalState(action));
        _output.WriteLine(result.RawResponse);
        PromptScenario.ApplyAndParse(storage, result.RawResponse);
        var effects = storage.WorldState.Hero.Effects ?? [];
        Assert.DoesNotContain(effects, e => e.Deleted != true && e.Name == "Щит");
        Assert.Equal(!isLong, effects.Any(e => e.Deleted != true && e.Name == "Усталость"));
        Assert.Contains(effects, e => e.Deleted != true && e.Name == "Проклятие");
        Assert.Contains(effects, e => e.Deleted != true && e.Name == "Болезнь");
        Assert.Contains(isLong ? "long_rest →" : "short_rest →", result.ToolTrace);
    }

}
