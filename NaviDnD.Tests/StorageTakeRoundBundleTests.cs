using NaviDnD;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class StorageTakeRoundBundleTests
{
    private static Storage MakeStorage(int round = 5) =>
        new() { WorldState = { Time = { TotalRounds = round } } };

    private static Hero MakeHero(int col = 3, int row = 3) => new()
    {
        Position = [col, row],
        Symbol = "HRO",
        Name = "Герой"
    };

    // ── Effects ──────────────────────────────────────────────────────────────

    [Fact]
    public void ReturnsNull_WhenNoTriggersPresent()
    {
        var storage = MakeStorage();
        storage.WorldState.Hero = MakeHero();
        Assert.Null(storage.TakeRoundBundle());
    }

    [Fact]
    public void CollectsExpiredEffect_WhenRoundExceedsExpiresAtRound()
    {
        var storage = MakeStorage(round: 10);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Hero.Effects =
        [
            new StatusEffect
            {
                Name = "Яд",
                ExpiresAtRound = 8,
                OnExpire = new TriggerEffect { Effect = "Яд прошёл", Once = true }
            }
        ];

        var bundle = storage.TakeRoundBundle();

        Assert.NotNull(bundle);
        Assert.Single(bundle.ExpiredEffects);
        Assert.Equal("Яд", bundle.ExpiredEffects[0].Effect.Name);
        Assert.Equal(0, bundle.ExpiredEffects[0].EffectId);
    }

    [Fact]
    public void DoesNotFireExpiredEffect_Twice()
    {
        var storage = MakeStorage(round: 10);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Hero.Effects =
        [
            new StatusEffect
            {
                Name = "Яд",
                ExpiresAtRound = 8,
                OnExpire = new TriggerEffect { Effect = "Яд прошёл", Once = true }
            }
        ];

        storage.TakeRoundBundle(); // first call fires it
        var second = storage.TakeRoundBundle();

        // expired without OnRound — nothing left to fire
        Assert.Null(second);
    }

    [Fact]
    public void SkipsExpiredEffect_WithNoOnExpire()
    {
        var storage = MakeStorage(round: 10);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Hero.Effects =
        [
            new StatusEffect { Name = "Ожог", ExpiresAtRound = 5, OnExpire = null }
        ];

        Assert.Null(storage.TakeRoundBundle());
    }

    [Fact]
    public void SilentlyDeletesExpiredEffect_WithNoOnExpire_InsteadOfLeavingGhostEntry()
    {
        var storage = MakeStorage(round: 10);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Hero.Effects =
        [
            new StatusEffect { Name = "Ожог", ExpiresAtRound = 5, OnExpire = null }
        ];

        storage.TakeRoundBundle();

        Assert.True(storage.WorldState.Hero.Effects[0].Deleted,
            "Эффект без onExpire должен тихо удаляться движком после истечения, а не висеть вечно с просроченной длительностью.");
    }

    [Fact]
    public void CollectsOnRoundEffect_WhenStillActive()
    {
        var storage = MakeStorage(round: 5);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Hero.Effects =
        [
            new StatusEffect
            {
                Name = "Регенерация",
                ExpiresAtRound = 10,
                OnRound = new TriggerEffect { Effect = "Восстанови 1d4 HP", Once = false }
            }
        ];

        var bundle = storage.TakeRoundBundle();

        Assert.NotNull(bundle);
        Assert.Single(bundle.OnRoundEffects);
        Assert.Equal("Регенерация", bundle.OnRoundEffects[0].Effect.Name);
    }

    [Fact]
    public void DoesNotFireOnRound_TwiceInSameRound()
    {
        var storage = MakeStorage(round: 5);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Hero.Effects =
        [
            new StatusEffect
            {
                Name = "Регенерация",
                ExpiresAtRound = 10,
                OnRound = new TriggerEffect { Effect = "Восстанови 1d4 HP", Once = false }
            }
        ];

        storage.TakeRoundBundle();
        var second = storage.TakeRoundBundle();

        Assert.Null(second);
    }

    [Fact]
    public void FiresOnRound_AgainOnNextRound()
    {
        var storage = MakeStorage(round: 5);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Hero.Effects =
        [
            new StatusEffect
            {
                Name = "Регенерация",
                ExpiresAtRound = 10,
                OnRound = new TriggerEffect { Effect = "Восстанови 1d4 HP", Once = false }
            }
        ];

        storage.TakeRoundBundle();

        // advance to round 6
        storage.WorldState.Time.TotalRounds = 6;
        var nextRound = storage.TakeRoundBundle();

        Assert.NotNull(nextRound);
        Assert.Single(nextRound.OnRoundEffects);
    }

    [Fact]
    public void SkipsDeletedEffect()
    {
        var storage = MakeStorage(round: 10);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Hero.Effects =
        [
            new StatusEffect
            {
                Name = "Яд",
                ExpiresAtRound = 5,
                OnExpire = new TriggerEffect { Effect = "end", Once = true },
                Deleted = true
            }
        ];

        Assert.Null(storage.TakeRoundBundle());
    }

    [Fact]
    public void NeverExpires_WhenExpiresAtRoundIsNull()
    {
        var storage = MakeStorage(round: 999);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Hero.Effects =
        [
            new StatusEffect
            {
                Name = "Проклятие",
                ExpiresAtRound = null,
                OnExpire = new TriggerEffect { Effect = "end", Once = true }
            }
        ];

        Assert.Null(storage.TakeRoundBundle());
    }

    [Fact]
    public void CollectsEntityEffect_TaggedWithEntityOwnerPathAndId()
    {
        var storage = MakeStorage(round: 10);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Map.Entities =
        [
            new LivingEntity
            {
                Symbol = "ORC",
                Name = "Орк",
                Position = [1, 1],
                Effects =
                [
                    new StatusEffect
                    {
                        Name = "Горит",
                        ExpiresAtRound = 12,
                        OnRound = new TriggerEffect { Effect = "1d4 урона от огня", Once = false }
                    }
                ]
            }
        ];

        var bundle = storage.TakeRoundBundle();

        Assert.NotNull(bundle);
        Assert.Single(bundle.OnRoundEffects);
        var effectRef = bundle.OnRoundEffects[0];
        Assert.Equal("map.entities", effectRef.OwnerPath);
        Assert.Equal(0, effectRef.EntityId);
        Assert.Equal("Орк", effectRef.OwnerName);
        Assert.Equal("Горит", effectRef.Effect.Name);
    }

    [Fact]
    public void SkipsEntityEffect_OnDeletedEntity()
    {
        var storage = MakeStorage(round: 10);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Map.Entities =
        [
            new LivingEntity
            {
                Symbol = "ORC",
                Name = "Орк",
                Position = [1, 1],
                Deleted = true,
                Effects =
                [
                    new StatusEffect
                    {
                        Name = "Горит",
                        ExpiresAtRound = 12,
                        OnRound = new TriggerEffect { Effect = "1d4 урона от огня", Once = false }
                    }
                ]
            }
        ];

        Assert.Null(storage.TakeRoundBundle());
    }

    // ── ScheduledEvents ──────────────────────────────────────────────────────

    [Fact]
    public void CollectsScheduledEvent_WhenRoundReached()
    {
        var storage = MakeStorage(round: 10);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.ScheduledEvents =
        [
            new ScheduledEvent { Name = "Взрыв", FireAtRound = 10, Effect = "Бомба взрывается" }
        ];

        var bundle = storage.TakeRoundBundle();

        Assert.NotNull(bundle);
        Assert.Single(bundle.FiredScheduledEvents);
        Assert.Equal("Взрыв", bundle.FiredScheduledEvents[0].ev.Name);
    }

    [Fact]
    public void DoesNotFireScheduledEvent_Twice()
    {
        var storage = MakeStorage(round: 10);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.ScheduledEvents =
        [
            new ScheduledEvent { Name = "Взрыв", FireAtRound = 10, Effect = "Бомба взрывается" }
        ];

        storage.TakeRoundBundle();
        Assert.Null(storage.TakeRoundBundle());
    }

    [Fact]
    public void DoesNotFireScheduledEvent_BeforeItsRound()
    {
        var storage = MakeStorage(round: 9);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.ScheduledEvents =
        [
            new ScheduledEvent { Name = "Взрыв", FireAtRound = 10, Effect = "Бомба взрывается" }
        ];

        Assert.Null(storage.TakeRoundBundle());
    }

    [Fact]
    public void SkipsDeletedScheduledEvent()
    {
        var storage = MakeStorage(round: 10);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.ScheduledEvents =
        [
            new ScheduledEvent { Name = "Взрыв", FireAtRound = 10, Effect = "Бомба взрывается", Deleted = true }
        ];

        Assert.Null(storage.TakeRoundBundle());
    }

    // ── Area round triggers ───────────────────────────────────────────────────

    [Fact]
    public void CollectsAreaRoundTrigger_WhenHeroIsInsideArea()
    {
        var storage = MakeStorage(round: 5);
        storage.WorldState.Hero = MakeHero(col: 3, row: 3);
        storage.WorldState.Map.Area =
        [
            new Area
            {
                Name = "Ядовитый туман",
                Positions = [[3, 3], [3, 4]],
                Triggers = new AreaTriggers
                {
                    OnRound = new TriggerEffect { Effect = "1d4 яда", Once = false }
                }
            }
        ];

        var bundle = storage.TakeRoundBundle();

        Assert.NotNull(bundle);
        Assert.Single(bundle.AreaRoundTriggers);
        Assert.Equal("Ядовитый туман", bundle.AreaRoundTriggers[0].area.Name);
    }

    [Fact]
    public void DoesNotCollectAreaRoundTrigger_WhenHeroOutside()
    {
        var storage = MakeStorage(round: 5);
        storage.WorldState.Hero = MakeHero(col: 7, row: 7);
        storage.WorldState.Map.Area =
        [
            new Area
            {
                Name = "Ядовитый туман",
                Positions = [[3, 3], [3, 4]],
                Triggers = new AreaTriggers
                {
                    OnRound = new TriggerEffect { Effect = "1d4 яда", Once = false }
                }
            }
        ];

        Assert.Null(storage.TakeRoundBundle());
    }

    [Fact]
    public void DoesNotFireAreaRoundTrigger_TwiceInSameRound()
    {
        var storage = MakeStorage(round: 5);
        storage.WorldState.Hero = MakeHero(col: 3, row: 3);
        storage.WorldState.Map.Area =
        [
            new Area
            {
                Name = "Ядовитый туман",
                Positions = [[3, 3]],
                Triggers = new AreaTriggers
                {
                    OnRound = new TriggerEffect { Effect = "1d4 яда", Once = false }
                }
            }
        ];

        storage.TakeRoundBundle();
        Assert.Null(storage.TakeRoundBundle());
    }

    [Fact]
    public void FiresAreaRoundTrigger_AgainOnNextRound()
    {
        var storage = MakeStorage(round: 5);
        storage.WorldState.Hero = MakeHero(col: 3, row: 3);
        storage.WorldState.Map.Area =
        [
            new Area
            {
                Name = "Ядовитый туман",
                Positions = [[3, 3]],
                Triggers = new AreaTriggers
                {
                    OnRound = new TriggerEffect { Effect = "1d4 яда", Once = false }
                }
            }
        ];

        storage.TakeRoundBundle();
        storage.WorldState.Time.TotalRounds = 6;
        var nextRound = storage.TakeRoundBundle();

        Assert.NotNull(nextRound);
        Assert.Single(nextRound.AreaRoundTriggers);
    }

    [Fact]
    public void ReturnsCorrectRoundNumber()
    {
        var storage = MakeStorage(round: 42);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.ScheduledEvents =
        [
            new ScheduledEvent { Name = "X", FireAtRound = 42, Effect = "Y" }
        ];

        var bundle = storage.TakeRoundBundle();

        Assert.NotNull(bundle);
        Assert.Equal(42, bundle.NewRound);
    }
}
