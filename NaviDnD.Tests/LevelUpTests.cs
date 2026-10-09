using NaviDnD.Data.Models;
using NaviDnD.Helpers;

namespace NaviDnD.Tests;

// Опыт и уровни: движок ведёт hero.level/xp (мастер — только xpAward), окно «Новый уровень» собирает героя из выбора
// игрока и патча мастера (LevelUp) — без нейронки.
public class LevelUpTests
{
    private static Hero Fighter(int level = 3, int xp = 2700) => new()
    {
        Symbol = "HRO",
        Name = "Кадрим",
        Hp = "20/28",
        Level = level,
        Xp = xp,
        Stats =
        [
            new() { Name = "Сила", Value = "+3 (16)" },
            new() { Name = "Телосложение", Value = "+2 (15)" },
            new() { Name = "Бонус Мастерства", Value = "+2" },
            new() { Name = "Класс", Value = "Воин", Key = StatKeys.Class },
        ],
        Skills = [new() { Name = "Атлетика", Value = "+5" }],
        Inventory = [new() { Name = "Меч", Description = "Длинный меч" }],
        Abilities = [new() { Name = "Второе дыхание", Description = "1к10+уровень" }],
        Resources = [],
        EquipmentSlots = [],
    };

    private static Storage WithHero(Hero hero)
    {
        var storage = new Storage();
        storage.WorldState.Hero = hero;
        return storage;
    }

    [Theory]
    [InlineData("Воин 1 ур", "Воин", 1)]
    [InlineData("Воин (Чемпион) 3 ур.", "Воин (Чемпион)", 3)]
    [InlineData("Wizard (level 2)", "Wizard", 2)]
    [InlineData("Druid 1 lvl", "Druid", 1)]
    [InlineData("Fighter 12", "Fighter", 12)]
    public void ClassLevelSplitsLevelOff(string value, string cls, int level)
    {
        Assert.Equal((cls, (int?)level), ClassLevel.Split(value));
    }

    [Fact]
    public void ClassWithoutLevelStaysAsIs()
    {
        Assert.Equal(("Воин (Чемпион)", (int?)null), ClassLevel.Split("Воин (Чемпион)"));
    }

    [Fact]
    public void MasterCannotPatchLevelOrXp()
    {
        var storage = WithHero(Fighter());
        storage.ApplyUpdateWorldState("""{"hero":{"level":10,"xp":99999}}""");
        Assert.Equal(3, storage.WorldState.Hero!.Level);
        Assert.Equal(2700, storage.WorldState.Hero.Xp);
    }

    [Fact]
    public void SavedStateKeepsLevelAndXp()
    {
        var storage = new Storage();
        storage.ApplySavedState("""{"hero":{"name":"Кадрим","level":5,"xp":7000}}""");
        Assert.Equal(5, storage.WorldState.Hero!.Level);
        Assert.Equal(7000, storage.WorldState.Hero.Xp);
    }

    [Fact]
    public void XpAwardCountsMonstersByChallengeRating()
    {
        var storage = WithHero(Fighter(xp: 2600));
        storage.ApplyUpdateWorldState("""{"hero":{"xpAward":{"monsters":["Гоблин","Goblin","Неведомая тварь"],"xp":25,"reason":"засада у моста"}}}""");
        Assert.Equal(2600 + 50 + 50 + 25, storage.WorldState.Hero!.Xp);
        var notices = storage.TakeNotices();
        Assert.Equal(L.WF("[+{0} опыта: {1}]", 125, "засада у моста"), notices[0]);
        Assert.Equal(2, notices.Count);   // порог 4-го уровня (2700) пройден
        Assert.Empty(storage.TakeNotices());
    }

    [Fact]
    public void ZeroXpAwardAddsNothing()
    {
        var storage = WithHero(Fighter());
        storage.ApplyUpdateWorldState("""{"hero":{"xpAward":{"monsters":["Неведомая тварь"]}}}""");
        Assert.Equal(2700, storage.WorldState.Hero!.Xp);
        Assert.Empty(storage.TakeNotices());
    }

    [Fact]
    public void NewHeroTakesLevelFromClassStat()
    {
        var storage = new Storage();
        storage.ApplyUpdateWorldState("""{"hero":{"name":"Лира","stats":[{"name":"Класс","value":"Волшебник 2 ур"}]}}""");
        var hero = storage.WorldState.Hero!;
        Assert.Equal(2, hero.Level);
        Assert.Equal(300, hero.Xp);
        Assert.Equal("Волшебник", hero.Stat(StatKeys.Class)!.Value);
    }

    [Fact]
    public void ClassStatWithLevelDoesNotRaiseLevel()
    {
        var storage = WithHero(Fighter());
        storage.ApplyUpdateWorldState("""{"hero":{"stats":[{"id":3,"value":"Воин 9 ур"}]}}""");
        Assert.Equal(3, storage.WorldState.Hero!.Level);
        Assert.Equal("Воин", storage.WorldState.Hero.Stat(StatKeys.Class)!.Value);
    }

    [Fact]
    public void RulesFollowTheTable()
    {
        var rules = LevelRules.Current;
        Assert.Equal(20, rules.MaxLevel);
        Assert.Equal(2700, rules.NextXp(3));
        Assert.True(rules.CanLevelUp(Fighter(xp: 2700)));
        Assert.False(rules.CanLevelUp(Fighter(xp: 2699)));
        Assert.Equal(10, rules.HitDie(Fighter()));
        Assert.True(rules.IsAsiLevel(Fighter(), 6));         // у воина — лишнее улучшение на 6-м
        Assert.Equal(3, rules.ProficiencyAt(5));
    }

    [Fact]
    public void NotAvailableInCombat()
    {
        var ws = new WorldState { Hero = Fighter(), Combat = new CombatState { Active = true } };
        Assert.False(LevelUp.Available(ws));
        ws.Combat = null;
        Assert.True(LevelUp.Available(ws));
    }

    [Fact]
    public void ReadyOnlyWhenEverythingChosen()
    {
        var up = new LevelUp(Fighter(), LevelRules.Current);
        Assert.True(up.IsAsi);   // 3 → 4
        Assert.True(up.LoadPlan("""
            {"gains":[{"name":"Улучшение приёма","description":"…"}],
             "choices":[{"id":"style","title":"Стиль","count":1,"options":[{"name":"Оборона","description":"+1 КД"},{"name":"Дуэлянт","description":"+2 урон"}]},
                        {"id":"feat","title":"Черта","count":1,"options":[{"name":"Стойкий","description":"…"}]}]}
            """));
        Assert.False(up.Ready);
        up.ChooseAverage();
        up.Choices[0].Toggle(1);
        up.SetAsi(LevelUp.AsiMode.Plus1x2);
        up.ToggleAsi(AbilityNames.Str);
        Assert.False(up.Ready);                              // нужно две характеристики
        up.ToggleAsi(AbilityNames.Con);
        Assert.True(up.Ready);                               // черта скрыта — не нужна
        Assert.DoesNotContain(up.VisibleChoices, c => c.Id == LevelUp.FeatId);
        up.SetAsi(LevelUp.AsiMode.Feat);
        Assert.False(up.Ready);
        up.Choices.Single(c => c.Id == LevelUp.FeatId).Toggle(0);
        Assert.True(up.Ready);
    }

    [Fact]
    public void SingleChoiceReplacesPick()
    {
        var c = new LevelUp.Choice { Count = 1, Options = [new() { Name = "А" }, new() { Name = "Б" }] };
        c.Toggle(0);
        c.Toggle(1);
        Assert.Equal([1], c.Picked);
    }

    [Fact]
    public void HitPointsRollOnce()
    {
        var up = new LevelUp(Fighter(), LevelRules.Current);
        Assert.Equal(10 / 2 + 1 + 2, up.HpAverage);
        int first = up.RollHp(new Random(1));
        Assert.Equal(first, up.RollHp(new Random(2)));       // повтора нет
        up.ChooseAverage();
        Assert.True(up.HpRoll);                              // после броска на среднее не переключить
    }

    [Fact]
    public void ApplyKeepsOnlySheetChangesAndSetsEngineNumbers()
    {
        var hero = Fighter();
        var up = new LevelUp(hero, LevelRules.Current);
        up.LoadPlan("""{"gains":[],"choices":[]}""");
        up.ChooseAverage();
        up.SetAsi(LevelUp.AsiMode.Plus1x2);
        up.ToggleAsi(AbilityNames.Str);
        up.ToggleAsi(AbilityNames.Con);                       // 15 → 16: модификатор +2 → +3

        var after = up.Apply("""
            {"hero":{"abilities":[{"name":"Стойкость","description":"…"}],"skills":[{"id":0,"value":"+6"}],
                     "inventory":[{"id":0,"deleted":true}],"hp":"999/999","level":20,"xp":1}}
            """);

        Assert.Equal(4, after.Level);
        Assert.Equal(2700, after.Xp);
        // Среднее d10 с прежним Тел: 6+2; модификатор Тел вырос на 1 — ещё +1 за каждый из 4 уровней.
        Assert.Equal(8 + 4, up.HpGain);
        Assert.Equal("32/40", after.Hp);
        Assert.Equal("+3 (17)", AbilityNames.Find(after, AbilityNames.Str)!.Value);
        Assert.Equal("+3 (16)", AbilityNames.Find(after, AbilityNames.Con)!.Value);
        Assert.Equal("+6", after.Skills[0].Value);
        Assert.Contains(after.Abilities, a => a.Name == "Стойкость" && a.Color is [220, 220, 220]);   // без цвета от мастера — утилитарный
        Assert.Single(after.Inventory);                       // инвентарь мастер тут не трогает
        // Исходный герой не изменился до «Принять».
        Assert.Equal(3, hero.Level);
        Assert.Equal("+3 (16)", hero.Stats[0].Value);
        Assert.Single(hero.Abilities);
    }

    [Fact]
    public void ApplyRequestListsChosenOptions()
    {
        var up = new LevelUp(Fighter(level: 2, xp: 900), LevelRules.Current);
        up.LoadPlan("""{"gains":[],"choices":[{"id":"subclass","title":"Архетип","count":1,"options":[{"name":"Чемпион","description":"Крит 19–20"},{"name":"Мастер боевых искусств","description":"Приёмы"}]}]}""");
        up.Choices[0].Toggle(0);
        string request = up.ApplyRequest();
        Assert.Contains("Воин 2 → 3", request);
        Assert.Contains("Чемпион — Крит 19–20", request);
        Assert.DoesNotContain("Мастер боевых искусств", request);
        Assert.False(up.IsAsi);
    }

    [Fact]
    public void KnownAbilityIsNotAGainAndResourceGrowthShowsInPreview()
    {
        var hero = Fighter();
        hero.Resources = [new() { Name = "Ячейки 1 круга", Value = "2/2", Category = ResourceCategories.Spells }];
        var up = new LevelUp(hero, LevelRules.Current);
        up.LoadPlan("""
            {"gains":[{"name":"Второе дыхание","description":"уже есть"},
                      {"name":"Ячейки 1 круга","description":"3 вместо 2","resource":"Ячейки 1 круга","value":"3/3"}]}
            """);
        Assert.Equal(["Ячейки 1 круга"], up.Gains.Select(g => g.Name));
        var preview = up.Preview();
        Assert.Equal("3/3", preview.Resources.Single().Value);
        Assert.DoesNotContain(preview.Abilities, a => a.Name == "Ячейки 1 круга");
        Assert.Equal("2/2", hero.Resources.Single().Value);
    }

    [Fact]
    public void DraftSurvivesSaveAndReload()
    {
        var hero = Fighter(level: 2, xp: 900);
        var up = new LevelUp(hero, LevelRules.Current);
        up.LoadPlan("""{"choices":[{"id":"subclass","title":"Архетип","count":1,"options":[{"name":"Чемпион"}]}]}""");
        up.MergeCustom("""{"choices":[{"id":"subclass","title":"Архетип","count":1,"options":[{"name":"Мистический рыцарь"}]}]}""");
        int die = up.RollHp(new Random(3));
        up.SaveDraft();

        // Сохранение → загрузка: черновик на месте, мастер своим патчем его не меняет.
        var saved = System.Text.Json.JsonSerializer.Serialize(new { hero },
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        var storage = new Storage();
        storage.ApplySavedState(saved);
        storage.ApplyUpdateWorldState("""{"hero":{"levelUpDraft":null}}""");
        var again = new LevelUp(storage.WorldState.Hero!, LevelRules.Current);

        Assert.True(again.PlanLoaded);
        Assert.Contains(again.Choices[0].Options, o => o.Name == "Мистический рыцарь" && o.Custom);
        Assert.Equal(die, again.HpDie);
        Assert.True(again.HpRoll);
        Assert.Null(again.Apply(null).LevelUpDraft);   // уровень получен — черновик не нужен
    }

    [Fact]
    public void NewSpellsDoNotExceedPreparedMax()
    {
        var hero = Fighter();
        hero.Stats.Add(new() { Name = "Подготовлено заклинаний", Value = "2", Key = StatKeys.SpellsPrepared });
        hero.Spells = [new() { Name = "Щит", Level = 1, Prepared = true }, new() { Name = "Сон", Level = 1, Prepared = true }];
        var up = new LevelUp(hero, LevelRules.Current);
        up.ChooseAverage();
        var after = up.Apply("""{"hero":{"spells":[{"name":"Маскировка","level":1,"prepared":true}]}}""");
        Assert.Equal(2, after.Spells!.Count(sp => sp.Prepared == true));
        Assert.False(after.Spells!.Single(sp => sp.Name == "Маскировка").Prepared);
    }

    [Fact]
    public void StatGrowthShowsInPreview()
    {
        var hero = Fighter();
        hero.Stats.Add(new() { Name = "Подготовлено заклинаний", Value = "4", Key = StatKeys.SpellsPrepared });
        var up = new LevelUp(hero, LevelRules.Current);
        up.LoadPlan("""{"gains":[{"name":"Подготовка заклинаний","description":"5 вместо 4","stat":"Подготовлено заклинаний","value":"5"}]}""");
        var preview = up.Preview();
        Assert.Equal("5", preview.Stat(StatKeys.SpellsPrepared)!.Value);
        Assert.DoesNotContain(preview.Abilities, a => a.Name == "Подготовка заклинаний");
        Assert.Equal("4", hero.Stat(StatKeys.SpellsPrepared)!.Value);
    }

    [Fact]
    public void CustomOptionJoinsItsGroupOrRefuses()
    {
        var up = new LevelUp(Fighter(level: 2, xp: 900), LevelRules.Current);
        up.LoadPlan("""{"choices":[{"id":"subclass","title":"Архетип","count":1,"options":[{"name":"Чемпион"}]}]}""");
        Assert.Null(up.MergeCustom("""{"choices":[{"id":"subclass","title":"Архетип","count":1,"options":[{"name":"Мистический рыцарь"}]}]}"""));
        Assert.Contains(up.Choices[0].Options, o => o.Name == "Мистический рыцарь" && o.Custom);
        Assert.Equal("Только по правилам", up.MergeCustom("""{"refusal":"Только по правилам"}"""));
    }

    [Fact]
    public void FeatsDroppedOffAsiLevels()
    {
        var up = new LevelUp(Fighter(level: 1, xp: 300), LevelRules.Current);
        up.LoadPlan("""{"choices":[{"id":"feat","title":"Черта","count":1,"options":[{"name":"Стойкий"}]}]}""");
        Assert.Empty(up.Choices);
    }

    [Fact]
    public void ApplyAddsSpellsAndAverageHp()
    {
        var up = new LevelUp(Fighter(), LevelRules.Current);
        up.ChooseAverage();
        var after = up.Apply("""{"hero":{"spells":[{"name":"Щит","level":1,"prepared":true}]}}""");
        Assert.Contains(after.Spells!, sp => sp.Name == "Щит");
        Assert.Equal("28/36", after.Hp);
    }
}
