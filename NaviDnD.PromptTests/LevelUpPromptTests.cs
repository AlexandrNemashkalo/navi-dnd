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
}
