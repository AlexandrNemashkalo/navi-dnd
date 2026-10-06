using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// РЕАЛЬНЫЕ вызовы Claude — см. класс SendActionPromptTests для общих правил (платно, медленно,
// запускать редко и осознанно). Проверяет весь жизненный цикл статус-эффектов (§ СОСТОЯНИЯ
// systemPrompt.md): ИИ реально добавляет hero.effects по свободному действию игрока (не только по
// триггеру/бою, как остальные PromptTests), затем корректно снимает эффект — вручную (Prone) или
// по истечении раунда (round-transition).
public class EffectsPromptTests
{
    private readonly ITestOutputHelper _output;

    public EffectsPromptTests(ITestOutputHelper output) => _output = output;

    // Лежит ничком (Prone) — эффект без expiresAtRound/untilLongRest (снимается только явным
    // действием игрока, см. § СОСТОЯНИЯ "Бессрочно/до ручного снятия"). Два реальных вызова подряд
    // на ОДНОМ storage: сначала герой ложится ничком, потом встаёт — эффект должен появиться и
    // пропасть в hero.effects, а не просто упоминаться в нарративе.
    [Fact]
    public async Task Prone_AddedWhenHeroGoesProne_AndRemovedWhenHeroStandsUp()
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        Assert.True(storage.WorldState.Hero!.Effects is not { Count: > 0 },
            "Предусловие сценария: у героя изначально не должно быть активных эффектов.");

        var ctx = new AiContextBuilder(storage.WorldState);

        // Шаг 1: герой сознательно падает ничком, чтобы укрыться от стрел.
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

        // Шаг 2: герой встаёт — эффект должен быть снят.
        string standMessage = ctx.MinimalState("встаю на ноги");
        var standResult = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, standMessage);
        _output.WriteLine("Ответ модели (встаю):\n" + standResult.RawResponse);

        var standParsed = PromptScenario.ApplyAndParse(storage, standResult.RawResponse);
        Assert.True(standParsed.HistoryTexts.Count > 0, "Вставание на ноги должно быть описано в history.");

        var stillActive = storage.WorldState.Hero!.Effects?.Where(e => e.Deleted != true).ToList() ?? [];
        Assert.DoesNotContain(stillActive, e => e.Name == prone.Name);

        PromptScenario.RecordAsMock(fallResult.RawResponse, mockResponsesFolder: "ProneEffect", action: "SendAction");
    }

    // Кратковременный бафф (несколько раундов) с onExpire — проверяем именно ту часть жизненного
    // цикла, которую Prone-тест не задевает: движок обнаруживает истечение раунда и присылает
    // отдельный round-transition вызов, реальный ИИ обязан снять эффект по нему.
    //
    // Эффект добавлен напрямую патчем (не через свободное действие) — сознательно: две попытки
    // получить его через нарратив ("боевой клич" / "союзник-жрец читает молитву") ИИ ПРАВИЛЬНО
    // отклонил, потому что герой не владеет такой способностью, а на карте нет союзника — это не
    // баг, а работающий § АБСОЛЮТНЫЕ ЗАПРЕТЫ ("не додумывать за игрока/сцену"). Здесь же цель —
    // проверить не "добавление", а именно снятие по истечении раунда, так что источник эффекта для
    // теста не важен.
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

        // Перематываем время за пределы длительности эффекта и прогоняем движковую проверку раунда.
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

    // Эффект на МОНСТРЕ, не только на герое — Storage.CollectEntityEffects с самого начала одинаково
    // собирает map.entities[i].effects, но реальным ИИ это раньше не проверялось (все прочие тесты
    // этого файла — только hero.effects). Заодно проверяем батчинг: если в ОДНОМ раунде тикают
    // эффекты сразу у героя И у монстра, Storage.TakeRoundBundle() собирает их в ОДИН RoundBundle, а
    // AiContextBuilder.RoundTransitionState формирует ОДНО сообщение на оба — значит и реальному ИИ
    // достаточно ОДНОГО вызова, а не по одному на владельца эффекта (важно для количества/стоимости
    // запросов — она не должна расти линейно с числом активных периодических эффектов).
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

        // Первый же вызов TakeRoundBundle на свежем Storage — дедуп пуст, оба onRound-эффекта без
        // expiresAtRound тикают сразу же (см. Storage.CollectEntityEffects) — раунд специально не
        // перематываем, это не нужно для проверки батчинга.
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

        // ОДИН вызов ИИ — оба эффекта должны разрешиться в этом же ответе.
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

    // Схвачен (Grabbed/Restrained) — по промпту (§ hero.speedMax) скорость обнуляется на весь срок
    // действия эффекта через ОБА поля (speedMax И speedLeft), не только разово speedLeft — та же
    // ошибка, что чинили для Ничком (см. Prone-тест выше), не должна повториться и здесь.
    // Освобождение обычно требует броска (Атлетика/Акробатика) — по промпту ЛЮБАЯ проверка идёт
    // через roll_dice, так что rollValue:20 (авто-нат.20) гарантирует успех и тест не зависит от
    // удачи модели.
    [Fact]
    public async Task Grabbed_SetsSpeedToZero_AndRestoresSpeedWhenHeroBreaksFree()
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        int originalSpeedMax = storage.WorldState.Hero!.SpeedMax ?? 30;
        Assert.True(storage.WorldState.Hero!.Effects is not { Count: > 0 },
            "Предусловие сценария: у героя изначально не должно быть активных эффектов.");

        var ctx = new AiContextBuilder(storage.WorldState);

        // Угроза вводится самим окружением (DM волен вводить такую опасность сам) — не требует уже
        // существующей на карте сущности или способности героя, в отличие от провалившихся попыток
        // из TemporaryBuff-теста выше. Захват описан как УЖЕ СОСТОЯВШИЙСЯ факт (не "пытается
        // схватить") — первая формулировка ("неожиданно вырывается и обвивает") оставляла ИИ
        // пространство для спасброска, и он им воспользовался (успешный бросок Силы БЕЗ вызова
        // roll_dice, вопреки промпту), избежав захвата вообще — эффект не добавлялся, тест падал не
        // из-за бага, а из-за неоднозначной формулировки сценария.
        string grabMessage = ctx.MinimalState(
            "склизкое щупальце из трещины в стене уже туго обвило мою ногу и крепко держит — вырваться с ходу не вышло, я скован на месте");
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

    // Долгий отдых — ХП восстанавливается, untilLongRest-эффекты снимаются, время суток сдвигается
    // на 2 шага по циклу Ночь→Утро→День→Вечер→Ночь (см. § Время систем-промпта). Движок отдых не
    // отслеживает — целиком на ответственности ИИ через обычный SendAction, без отдельного игрового
    // действия «отдохнуть». Действие сформулировано БЕЗ конкретного времени назначения ("до утра" и
    // т.п.), чтобы не создавать конфликт между нарративной репликой игрока и механическим правилом
    // "+2 шага" — тестируем именно правило, а не то, умеет ли ИИ считать часы до рассвета.
    //
    // Fixture — burning_effect_game.json (пустой map.entities), а не full_game.json: первая попытка
    // на full_game.json ИИ ПРАВИЛЬНО отказал в отдыхе — рядом враждебный культист и гоблины,
    // безопасного лагеря в разгар подземелья, кишащего врагами, по сюжету быть не может (тот же
    // класс «отказа по § АБСОЛЮТНЫЕ ЗАПРЕТЫ», что уже дважды ловили выше). Тут проверяем именно
    // механику отдыха, а не то, разрешит ли ИИ спать посреди боя.
    [Fact]
    public async Task LongRest_RestoresHp_RemovesUntilLongRestEffects_AndAdvancesTimeOfDayByTwoSteps()
    {
        var storage = PromptScenario.LoadUiTestFixture("burning_effect_game.json");
        // id:0 — заранее заданный в фикстуре "Горящий" (не нужен этому сценарию — горящего героя
        // ИИ обоснованно не даст спокойно уснуть) — убираем тем же патчем, что добавляет "Истощение".
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
        Assert.Equal("Утро", storage.WorldState.Time.PartOfDay);

        PromptScenario.RecordAsMock(result.RawResponse, mockResponsesFolder: "LongRest", action: "SendAction");
    }

    private static int ParseHp(string hp) => int.Parse(hp.Split('/')[0]);
}
