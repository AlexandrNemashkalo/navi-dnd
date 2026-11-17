using NaviDnD;
using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class ObjectEffectRoundTests
{
    [Fact]
    public void ObjectExpiryIsDeliveredOnceWithAnObjectDeletePatch()
    {
        var storage = new Storage();
        storage.WorldState.Time.TotalRounds = 10;
        storage.WorldState.Map.Objects =
        [
            new() { Name = "Unrelated object" },
            new() { Name = "Seal", Effects = [new() { Name = "Ward", ExpiresAtRound = 9,
                OnExpire = new() { Effect = "Open the seal" } }] }
        ];
        storage.WorldState.Map.Entities =
        [
            new() { Name = "Watcher", Effects = [new() { Name = "Unrelated effect" }] }
        ];

        var bundle = storage.TakeRoundBundle()!;
        var effect = Assert.Single(bundle.ExpiredEffects);
        Assert.Equal("map.objects", effect.OwnerPath);
        Assert.Equal(1, effect.EntityId);
        Assert.Equal(0, effect.EffectId);
        Assert.Null(storage.TakeRoundBundle());
        storage.WorldState.Time.TotalRounds++;
        Assert.Null(storage.TakeRoundBundle());

        string context = new AiContextBuilder(storage.WorldState).RoundTransitionState(bundle);
        Assert.Contains("map.objects[id:1].effects", context);
        const string patch = "{\"map\":{\"objects\":[{\"id\":1,\"effects\":[{\"id\":0,\"deleted\":true}]}]}}";
        Assert.Contains(patch, context);
        storage.ApplyUpdateWorldState(patch);
        Assert.Empty(storage.WorldState.Map.Objects[1].Effects!);
        Assert.Single(storage.WorldState.Map.Entities[0].Effects!);
    }

    [Fact]
    public void DeletedObjectsDoNotRunOrExpireEffects()
    {
        var storage = new Storage();
        storage.WorldState.Time.TotalRounds = 10;
        storage.WorldState.Map.Objects =
        [
            new() { Name = "Destroyed seal", Deleted = true, Effects =
            [
                new() { Name = "Ward", ExpiresAtRound = 9, OnExpire = new() { Effect = "Open" } },
                new() { Name = "Pulse", OnRound = new() { Effect = "Pulse" } },
                new() { Name = "Fading light", ExpiresAtRound = 9 }
            ] }
        ];

        Assert.Null(storage.TakeRoundBundle());
        Assert.All(storage.WorldState.Map.Objects[0].Effects!, e => Assert.NotEqual(true, e.Deleted));
    }

    [Fact]
    public void ObjectRecurringEffectRunsOncePerRoundAndStopsAtExpiry()
    {
        var storage = new Storage();
        storage.WorldState.Time.TotalRounds = 10;
        var effect = new StatusEffect { Name = "Pulse", ExpiresAtRound = 11,
            OnRound = new() { Effect = "Pulse" } };
        storage.WorldState.Map.Objects = [new() { Name = "Beacon", Effects = [effect] }];

        Assert.Equal("map.objects", Assert.Single(storage.TakeRoundBundle()!.OnRoundEffects).OwnerPath);
        Assert.Null(storage.TakeRoundBundle());
        storage.WorldState.Time.TotalRounds = 11;
        Assert.Single(storage.TakeRoundBundle()!.OnRoundEffects);
        storage.WorldState.Time.TotalRounds = 12;
        Assert.Null(storage.TakeRoundBundle());
        Assert.True(effect.Deleted);
    }
}
