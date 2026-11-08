using NaviDnD.Data.Models;
using NaviDnD.Helpers;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class LevelUpPromptTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Воин", 8)]
    [InlineData("Волшебник", 6)]
    public async Task OptionsThenApplyAddsSecondLevelFeatures(string cls, int hpGain)
    {
        var storage = new Storage();
        storage.WorldState.Hero = new Hero { Symbol = "HRO", Name = "Тестер", Level = 1, Xp = 300, Hp = "8/10",
            Stats = [new() { Name = "Класс", Key = StatKeys.Class, Value = cls },
                     new() { Name = "Сила", Value = "+2 (14)" }, new() { Name = "Ловкость", Value = "+2 (14)" },
                     new() { Name = "Телосложение", Value = "+2 (14)" }, new() { Name = "Интеллект", Value = "+3 (16)" },
                     new() { Name = "Мудрость", Value = "+0 (10)" }, new() { Name = "Харизма", Value = "+0 (10)" },
                     new() { Name = "Бонус Мастерства", Value = "+2" }],
            Abilities = cls == "Воин" ? [new() { Name = "Второе дыхание", Description = "1d10+уровень за бонусное действие, раз за отдых" }]
                : [new() { Name = "Магическое восстановление", Description = "Восстановление ячеек на коротком отдыхе" }],
            Resources = cls == "Волшебник" ? [new() { Name = "Ячейки 1 круга", Value = "2/2", Category = ResourceCategories.Spells }] : [] };
        var up = new LevelUp(storage.WorldState.Hero, LevelRules.Current);
        string options = await PromptScenario.CallRealAiAsync("Dnd5e", "LevelUpOptions", storage, "lang:ru\n" + up.OptionsRequest());
        output.WriteLine(options);
        Assert.True(up.LoadPlan(options));
        up.ChooseAverage();
        foreach (var choice in up.VisibleChoices)
            for (int i = 0; i < choice.Needed; i++) choice.Toggle(i);
        Assert.True(up.Ready);
        string patch = await PromptScenario.CallRealAiAsync("Dnd5e", "LevelUpApply", storage, "lang:ru\n" + up.ApplyRequest());
        output.WriteLine(patch);
        var after = up.Apply(patch);
        Assert.Equal(2, after.Level);
        Assert.Equal($"{8+hpGain}/{10+hpGain}", after.Hp);
        Assert.Equal("+2", AbilityNames.Proficiency(after)!.Value);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(after), System.Text.Json.JsonSerializer.Serialize(up.Apply(patch)));
        Assert.Equal(after.Abilities.Count, after.Abilities.Select(a => a.Name).Distinct().Count());
        if (cls == "Воин") Assert.Contains(after.Abilities, a => a.Name.Contains("Всплеск", StringComparison.OrdinalIgnoreCase));
        else
        {
            Assert.Contains(after.Resources, r => r.Value == "3/3" && r.Category == ResourceCategories.Spells);
            Assert.True((after.Spells?.Count ?? 0) >= 2);
            Assert.True(after.Abilities.Count > storage.WorldState.Hero.Abilities.Count);
        }
    }
    [Theory]
    [InlineData("Воин", 3)]
    [InlineData("Воин", 5)]
    [InlineData("Волшебник", 3)]
    [InlineData("Волшебник", 5)]
    public async Task HigherLevelsRespectClassTable(string cls, int targetLevel)
    {
        bool wizard = cls == "Волшебник";
        int from = targetLevel - 1, gain = wizard ? 6 : 8;
        int maximum = (wizard ? 8 : 12) + (from - 1) * gain;
        var storage = new Storage();
        var hero = new Hero { Symbol = "HRO", Name = "Тестер", Level = from,
            Xp = LevelRules.Current.NextXp(from)!.Value, Hp = $"{maximum-2}/{maximum}",
            Stats = [new() { Name = "Класс", Key = StatKeys.Class, Value = wizard ? "Волшебник (Школа воплощения)" : from >= 3 ? "Воин (Чемпион)" : "Воин" },
                new() { Name = "Сила", Value = "+3 (16)" }, new() { Name = "Ловкость", Value = "+2 (14)" },
                new() { Name = "Телосложение", Value = "+2 (14)" }, new() { Name = "Интеллект", Value = "+3 (16)" },
                new() { Name = "Мудрость", Value = "+0 (10)" }, new() { Name = "Харизма", Value = "+0 (10)" },
                new() { Name = "Бонус Мастерства", Value = "+2" }],
            Abilities = wizard ? [new() { Name = "Магическое восстановление", Description = "На коротком отдыхе восстановить ячейки суммарно до половины уровня (округление вверх), раз в день." },
                new() { Name = "Формирование заклинаний", Description = "Союзники защищены от заклинаний воплощения." }]
                : [new() { Name = "Второе дыхание", Description = "Бонусное действие 1d10+уровень, раз за отдых" },
                   new() { Name = "Всплеск действий", Description = "Дополнительное действие, раз за отдых" }] };
        if (wizard)
        {
            hero.Resources = [new() { Name = "Ячейки 1 круга", Value = from == 2 ? "3/3" : "4/4", Category = ResourceCategories.Spells }];
            if (from >= 3) hero.Resources.Add(new() { Name = "Ячейки 2 круга", Value = "3/3", Category = ResourceCategories.Spells });
            hero.Spells = [new() { Name = "Щит", Level = 1 }, new() { Name = "Волшебная стрела", Level = 1 }];
            hero.Stats.Add(new() { Name = "Подготовлено заклинаний", Key = StatKeys.SpellsPrepared, Value = (from+3).ToString() });
        }
        storage.WorldState.Hero = hero;
        var up = new LevelUp(hero, LevelRules.Current);
        string options = await PromptScenario.CallRealAiAsync("Dnd5e", "LevelUpOptions", storage, "lang:ru\n" + up.OptionsRequest());
        output.WriteLine(options);
        Assert.True(up.LoadPlan(options));
        up.ChooseAverage();
        foreach (var choice in up.VisibleChoices)
            for (int i = 0; i < choice.Needed; i++) choice.Toggle(i);
        Assert.True(up.Ready);
        string patch = await PromptScenario.CallRealAiAsync("Dnd5e", "LevelUpApply", storage, "lang:ru\n" + up.ApplyRequest());
        output.WriteLine(patch);
        var after = up.Apply(patch);
        Assert.Equal(targetLevel, after.Level);
        Assert.Equal($"{maximum-2+gain}/{maximum+gain}", after.Hp);
        Assert.Equal(targetLevel == 5 ? "+3" : "+2", AbilityNames.Proficiency(after)!.Value);
        Assert.Equal(after.Abilities.Count, after.Abilities.Select(a => a.Name).Distinct().Count());
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(after), System.Text.Json.JsonSerializer.Serialize(up.Apply(patch)));
        if (wizard)
        {
            int[] slots = targetLevel == 3 ? [4,2] : [4,3,2];
            for (int i = 0; i < slots.Length; i++)
                Assert.Contains(after.Resources, r => r.Name.Contains($"{i+1}") && r.Value == $"{slots[i]}/{slots[i]}" && r.Category == ResourceCategories.Spells);
            Assert.True((after.Spells?.Count ?? 0) >= hero.Spells!.Count + 2);
            Assert.Equal((targetLevel+3).ToString(), after.Stat(StatKeys.SpellsPrepared)!.Value);
        }
        else if (targetLevel == 5)
            Assert.Contains(after.Abilities, a => a.Name.Contains("Дополнительная атака", StringComparison.OrdinalIgnoreCase));
        else
            Assert.Contains('(', after.Stat(StatKeys.Class)!.Value);
    }

    [Fact]
    public async Task ExistingMonkAbilityGrowsWithoutAddingDuplicate()
    {
        var storage = new Storage();
        var hero = new Hero { Symbol = "HRO", Name = "Тестер", Level = 10, Xp = 85000, Hp = "71/73",
            Stats = [new() { Name = "Класс", Key = StatKeys.Class, Value = "Монах (Путь открытой ладони)" },
                new() { Name = "Сила", Value = "+2 (14)" }, new() { Name = "Ловкость", Value = "+3 (16)" },
                new() { Name = "Телосложение", Value = "+2 (14)" }, new() { Name = "Интеллект", Value = "+0 (10)" },
                new() { Name = "Мудрость", Value = "+2 (14)" }, new() { Name = "Харизма", Value = "+0 (10)" },
                new() { Name = "Бонус Мастерства", Value = "+4" }],
            Abilities = [new() { Name = "Боевые искусства", Description = "Безоружный удар и оружие монаха: кость урона 1d6, можно использовать Ловкость." },
                new() { Name = "Дополнительная атака", Description = "Две атаки за действие Атака." },
                new() { Name = "Техника открытой ладони", Description = "Шквал ударов может сбить с ног, оттолкнуть или лишить реакции." }],
            Resources = [new() { Name = "Ки", Value = "10/10", Category = ResourceCategories.Abilities }] };
        storage.WorldState.Hero = hero;
        var up = new LevelUp(hero, LevelRules.Current);
        string options = await PromptScenario.CallRealAiAsync("Dnd5e", "LevelUpOptions", storage, "lang:ru\n" + up.OptionsRequest());
        output.WriteLine(options);
        Assert.True(up.LoadPlan(options));
        Assert.Contains(up.Gains, g => g.Name == "Боевые искусства" && g.Value?.Contains("1d8") == true);
        up.ChooseAverage();
        foreach (var choice in up.VisibleChoices)
            for (int i = 0; i < choice.Needed; i++) choice.Toggle(i);
        Assert.True(up.Ready);
        string patch = await PromptScenario.CallRealAiAsync("Dnd5e", "LevelUpApply", storage, "lang:ru\n" + up.ApplyRequest());
        output.WriteLine(patch);
        var after = up.Apply(patch);
        Assert.Equal(11, after.Level);
        Assert.Equal("78/80", after.Hp);
        Assert.Equal("+4", AbilityNames.Proficiency(after)!.Value);
        Assert.Contains("1d8", Assert.Single(after.Abilities, a => a.Name == "Боевые искусства").Description);
        Assert.Contains(after.Resources, r => r.Name == "Ки" && r.Value == "11/11");
        Assert.Contains("1d8", up.Preview().Abilities.Single(a => a.Name == "Боевые искусства").Description);
    }

}
