using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class ReuseHeroStateTests
{
    private sealed class EmptyProvider : IAiProvider
    {
        public Task<string> Complete(IReadOnlyList<string> blocks, string message, string path, string model) => Task.FromResult("{}");
        public Task<string> CompleteWithStreaming(IReadOnlyList<string> blocks, string message, string path, string model, Action<string>? onChunk)
            => Complete(blocks, message, path, model);
    }

    [Fact]
    public async Task NewAdventureClearsDeathAndCombatFlagsWithoutChangingProgress()
    {
        string ruleSet = $"test_reuse_{Guid.NewGuid():N}";
        string ruleDirectory = Path.Combine(AppConfig.ProjectRoot, "Prompts", ruleSet);
        string actionDirectory = Path.Combine(ruleDirectory, "FixHeroForNewGame");
        Directory.CreateDirectory(actionDirectory);
        string promptPath = Path.Combine(actionDirectory, "systemPrompt.md");
        File.WriteAllText(promptPath, "Return JSON");
        try
        {
            var storage = new Storage();
            var client = new GameAiClient(storage.WorldState, storage, new EmptyProvider(), new AppConfig { RuleSet = ruleSet });
            var source = new Hero { Symbol = "HRO", Name = "Герой", Level = 5, Xp = 6500, Hp = "0/44", Dead = true,
                Hidden = true, Stealth = 20, Inspiration = true, NoOpportunityAttacksRound = 500,
                SpeedMax = 30, SpeedLeft = 0, Position = [3,3], Light = new() { On = true, Ft = 60 },
                Actions = [new() { Name = "Реакция", Value = 0, MaxValue = 1 }],
                Effects = [new() { Name = "Без сознания" }],
                Stats = [new() { Name = "Класс", Key = StatKeys.Class, Value = "Воин" }] };
            await client.ReuseHero(new NewGameData { Name = source.Name, Symbol = source.Symbol }, JsonSerializer.Serialize(source));
            var hero = storage.WorldState.Hero!;
            Assert.Equal("44/44", hero.Hp);
            Assert.NotEqual(true, hero.Dead);
            Assert.NotEqual(true, hero.Hidden);
            Assert.Null(hero.NoOpportunityAttacksRound);
            Assert.Null(hero.Light);
            Assert.Null(hero.Stealth);
            Assert.Equal(30, hero.SpeedLeft);
            Assert.Equal(1, hero.Actions![0].Value);
            Assert.Empty(hero.Effects!);
            Assert.Equal(5, hero.Level);
            Assert.Equal(6500, hero.Xp);
        }
        finally
        {
            File.Delete(promptPath);
            Directory.Delete(actionDirectory);
            Directory.Delete(ruleDirectory);
        }
    }
}
