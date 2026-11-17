using NaviDnD.Clients;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// Real model call: verify the applied state, including the same-index creature as a wrong-owner sentinel.
public class ObjectExpiryPromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ObjectExpiryRemovesOnlyItsEffectAndPreservesTheObject()
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        var state = storage.WorldState;
        state.Combat = null;
        int round = state.Time.TotalRounds;
        string heroHp = state.Hero!.Hp;
        var creature = state.Map.Entities![0];
        string creatureName = creature.Name;
        string creatureHp = creature.Hp;
        const string creatureEffect = "Защита свидетеля";
        const string sealName = "Каменная печать";
        const string sealEffect = "Временное свечение";
        creature.Effects = [new() { Name = creatureEffect, Color = [100, 150, 200],
            Description = "Безвредная защита, не связанная с каменной печатью.", ExpiresAtRound = round + 1000 }];
        state.Map.Objects = [new()
        {
            Name = sealName, Symbol = "(S)", Position = [.. state.Hero.Position], Color = [120, 140, 180],
            AiInfo = ["An intact stone seal rests here; its temporary glow does not sustain the physical object."],
            Effects = [new()
            {
                Name = sealEffect, Color = [120, 180, 240], ExpiresAtRound = round - 1,
                OnExpire = new() { Effect = "The seal's harmless glow fades. Describe this briefly and remove this effect; the stone seal remains intact. No damage or healing occurs." }
            }]
        }];

        var bundle = storage.TakeRoundBundle();
        Assert.NotNull(bundle);
        var expired = Assert.Single(bundle.ExpiredEffects);
        Assert.Equal("map.objects", expired.OwnerPath);
        Assert.Equal(0, expired.EntityId);
        Assert.Equal(0, expired.EffectId);
        string message = new AiContextBuilder(state).RoundTransitionState(bundle);
        Assert.Contains("map.objects[id:0].effects", message);

        var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, message);
        output.WriteLine(result.RawResponse);
        var parsed = PromptScenario.ApplyAndParse(storage, result.RawResponse);
        Assert.NotEmpty(parsed.HistoryTexts);

        var seal = Assert.Single(state.Map.Objects!.Where(o => o.Deleted != true && o.Name == sealName));
        Assert.DoesNotContain(seal.Effects ?? [], e => e.Deleted != true && e.Name == sealEffect);
        var survivingCreature = state.Map.Entities![0];
        Assert.NotEqual(true, survivingCreature.Deleted);
        Assert.Equal(creatureName, survivingCreature.Name);
        var protection = Assert.Single((survivingCreature.Effects ?? []).Where(e => e.Deleted != true && e.Name == creatureEffect));
        Assert.Equal(round + 1000, protection.ExpiresAtRound);
        Assert.Equal(creatureHp, survivingCreature.Hp);
        Assert.Equal(heroHp, state.Hero!.Hp);
        Assert.Null(storage.TakeRoundBundle());
    }
}
