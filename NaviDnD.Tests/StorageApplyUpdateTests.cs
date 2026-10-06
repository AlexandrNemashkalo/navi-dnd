using NaviDnD;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class StorageApplyUpdateTests
{
    private static Storage MakeStorage()
    {
        var s = new Storage();
        s.WorldState.Hero = new Hero
        {
            Symbol = "HRO",
            Name = "Герой",
            Position = [1, 1],
            Inventory = [],
            Abilities = [],
            Stats = [],
            Skills = [],
            Resources = [],
            EquipmentSlots = []
        };
        return s;
    }

    // ── Hero scalar fields ────────────────────────────────────────────────────

    [Fact]
    public void UpdatesHeroHp()
    {
        var storage = MakeStorage();
        storage.ApplyUpdateWorldState("""{"hero":{"hp":"8/20"}}""");
        Assert.Equal("8/20", storage.WorldState.Hero!.Hp);
    }

    [Fact]
    public void UpdatesHeroName()
    {
        var storage = MakeStorage();
        storage.ApplyUpdateWorldState("""{"hero":{"name":"Арагорн"}}""");
        Assert.Equal("Арагорн", storage.WorldState.Hero!.Name);
    }

    [Fact]
    public void UpdatesHeroSpeedLeft()
    {
        var storage = MakeStorage();
        storage.WorldState.Hero!.SpeedMax = 30;
        storage.ApplyUpdateWorldState("""{"hero":{"speedLeft":15}}""");
        Assert.Equal(15, storage.WorldState.Hero!.SpeedLeft);
    }

    // ── Time ─────────────────────────────────────────────────────────────────

    [Fact]
    public void UpdatesTotalRounds()
    {
        var storage = MakeStorage();
        storage.ApplyUpdateWorldState("""{"time":{"totalRounds":42}}""");
        Assert.Equal(42, storage.WorldState.Time.TotalRounds);
    }

    [Fact]
    public void UpdatesPartOfDay()
    {
        var storage = MakeStorage();
        storage.ApplyUpdateWorldState("""{"time":{"partOfDay":"Полдень"}}""");
        Assert.Equal("Полдень", storage.WorldState.Time.PartOfDay);
    }

    // ── History ───────────────────────────────────────────────────────────────

    [Fact]
    public void AppendsHistory()
    {
        var storage = MakeStorage();
        storage.WorldState.History = [new DialogMessage { Text = "Начало" }];
        storage.ApplyUpdateWorldState("""{"history":[{"text":"Продолжение"}]}""");
        Assert.Equal(2, storage.WorldState.History.Count);
        Assert.Equal("Продолжение", storage.WorldState.History[1].Text);
    }

    // ── PatchByIndex — Inventory ──────────────────────────────────────────────

    [Fact]
    public void PatchByIndex_AddsNewItem_WhenNoId()
    {
        var storage = MakeStorage();
        storage.ApplyUpdateWorldState("""
            {"hero":{"inventory":[{"name":"Меч","description":"Длинный меч","quantity":1,"color":[200,200,200]}]}}
            """);
        Assert.Single(storage.WorldState.Hero!.Inventory);
        Assert.Equal("Меч", storage.WorldState.Hero.Inventory[0].Name);
    }

    [Fact]
    public void PatchByIndex_UpdatesExistingItem_WithId()
    {
        var storage = MakeStorage();
        storage.WorldState.Hero!.Inventory =
        [
            new HeroInventory { Name = "Меч", Description = "Стандартный", Quantity = 1, Color = [200, 200, 200] }
        ];

        storage.ApplyUpdateWorldState("""{"hero":{"inventory":[{"id":0,"quantity":3}]}}""");

        Assert.Single(storage.WorldState.Hero.Inventory);
        Assert.Equal("Меч", storage.WorldState.Hero.Inventory[0].Name); // name preserved
        Assert.Equal(3, storage.WorldState.Hero.Inventory[0].Quantity);  // quantity updated
    }

    [Fact]
    public void PatchByIndex_DeletesItem_WithDeletedTrue()
    {
        var storage = MakeStorage();
        storage.WorldState.Hero!.Inventory =
        [
            new HeroInventory { Name = "Меч", Description = "Длинный меч", Quantity = 1, Color = [200, 200, 200] },
            new HeroInventory { Name = "Щит", Description = "Деревянный", Quantity = 1, Color = [150, 100, 50] }
        ];

        storage.ApplyUpdateWorldState("""{"hero":{"inventory":[{"id":0,"deleted":true}]}}""");

        Assert.Single(storage.WorldState.Hero.Inventory);
        Assert.Equal("Щит", storage.WorldState.Hero.Inventory[0].Name);
    }

    [Fact]
    public void PatchByIndex_AppendsWhenIdGeCount()
    {
        var storage = MakeStorage();
        storage.WorldState.Hero!.Inventory =
        [
            new HeroInventory { Name = "Меч", Description = "d", Quantity = 1, Color = [200, 200, 200] }
        ];

        storage.ApplyUpdateWorldState("""
            {"hero":{"inventory":[{"id":99,"name":"Лук","description":"d","quantity":1,"color":[100,100,100]}]}}
            """);

        Assert.Equal(2, storage.WorldState.Hero.Inventory.Count);
        Assert.Equal("Лук", storage.WorldState.Hero.Inventory[1].Name);
    }

    // ── PatchByIndex — Effects ────────────────────────────────────────────────

    [Fact]
    public void PatchByIndex_AddsEffect()
    {
        var storage = MakeStorage();
        storage.ApplyUpdateWorldState("""
            {"hero":{"effects":[{"name":"Яд","expiresAtRound":10}]}}
            """);
        Assert.Single(storage.WorldState.Hero!.Effects!);
        Assert.Equal("Яд", storage.WorldState.Hero.Effects![0].Name);
        Assert.Equal(10, storage.WorldState.Hero.Effects![0].ExpiresAtRound);
    }

    [Fact]
    public void PatchByIndex_UpdatesEffect_WithId()
    {
        var storage = MakeStorage();
        storage.WorldState.Hero!.Effects =
        [
            new StatusEffect { Name = "Яд", ExpiresAtRound = 10 }
        ];

        storage.ApplyUpdateWorldState("""{"hero":{"effects":[{"id":0,"expiresAtRound":15}]}}""");

        Assert.Single(storage.WorldState.Hero.Effects!);
        Assert.Equal("Яд", storage.WorldState.Hero.Effects![0].Name);
        Assert.Equal(15, storage.WorldState.Hero.Effects![0].ExpiresAtRound);
    }

    [Fact]
    public void PatchByIndex_DeletesEffect()
    {
        var storage = MakeStorage();
        storage.WorldState.Hero!.Effects =
        [
            new StatusEffect { Name = "Яд", ExpiresAtRound = 10 },
            new StatusEffect { Name = "Благословение", ExpiresAtRound = 20 }
        ];

        storage.ApplyUpdateWorldState("""{"hero":{"effects":[{"id":0,"deleted":true}]}}""");

        Assert.Single(storage.WorldState.Hero.Effects!);
        Assert.Equal("Благословение", storage.WorldState.Hero.Effects![0].Name);
    }

    // ── Nested effects (map.entities[i].effects, patched by id like hero.effects) ──

    [Fact]
    public void NestedEntityEffects_AddedById_WithoutClobberingOthers()
    {
        var storage = MakeStorage();
        storage.ApplyUpdateWorldState("""
            {"map":{"entities":[{"symbol":"ORC","name":"Орк","position":[2,2],"effects":[{"name":"Патрулирует"}]}]}}
            """);

        // add a second effect to the same (already-existing) entity by patching just "effects"
        storage.ApplyUpdateWorldState("""
            {"map":{"entities":[{"id":0,"effects":[{"name":"Горит","expiresAtRound":12}]}]}}
            """);

        var effects = storage.WorldState.Map.Entities![0].Effects!;
        Assert.Equal(2, effects.Count);
        Assert.Equal("Патрулирует", effects[0].Name);
        Assert.Equal("Горит", effects[1].Name);
        Assert.Equal(12, effects[1].ExpiresAtRound);
    }

    [Fact]
    public void NestedEntityEffects_UpdatedAndDeletedById()
    {
        var storage = MakeStorage();
        storage.ApplyUpdateWorldState("""
            {"map":{"entities":[{"symbol":"ORC","name":"Орк","position":[2,2],
              "effects":[{"name":"Яд","expiresAtRound":10},{"name":"Горит","expiresAtRound":12}]}]}}
            """);

        storage.ApplyUpdateWorldState("""
            {"map":{"entities":[{"id":0,"effects":[{"id":0,"expiresAtRound":15},{"id":1,"deleted":true}]}]}}
            """);

        var effects = storage.WorldState.Map.Entities![0].Effects!;
        Assert.Single(effects);
        Assert.Equal("Яд", effects[0].Name);
        Assert.Equal(15, effects[0].ExpiresAtRound);
    }

    // ── ScheduledEvents ───────────────────────────────────────────────────────

    [Fact]
    public void PatchByIndex_AddsScheduledEvent()
    {
        var storage = MakeStorage();
        storage.ApplyUpdateWorldState("""
            {"scheduledEvents":[{"name":"Взрыв","fireAtRound":15,"effect":"Бомба взрывается"}]}
            """);
        Assert.Single(storage.WorldState.ScheduledEvents!);
        Assert.Equal("Взрыв", storage.WorldState.ScheduledEvents![0].Name);
    }

    [Fact]
    public void ApplyUpdate_NullScheduledEvents_DoesNotCrash()
    {
        var storage = MakeStorage();
        // This was the crash bug: scheduledEvents: null in JSON
        storage.ApplyUpdateWorldState("""{"scheduledEvents":null}""");
        // should survive without exception, events unchanged
        Assert.Null(storage.WorldState.ScheduledEvents);
    }

    [Fact]
    public void PatchByIndex_DeletesScheduledEvent()
    {
        var storage = MakeStorage();
        storage.WorldState.ScheduledEvents =
        [
            new ScheduledEvent { Name = "Взрыв", FireAtRound = 10, Effect = "Бомба" },
            new ScheduledEvent { Name = "Враги", FireAtRound = 20, Effect = "Подкрепление" }
        ];

        storage.ApplyUpdateWorldState("""{"scheduledEvents":[{"id":0,"deleted":true}]}""");

        Assert.Single(storage.WorldState.ScheduledEvents!);
        Assert.Equal("Враги", storage.WorldState.ScheduledEvents![0].Name);
    }

    // ── MergeInto — partial update preserves other fields ────────────────────

    [Fact]
    public void MergeInto_PreservesUnchangedFields()
    {
        var storage = MakeStorage();
        storage.WorldState.Hero!.Inventory =
        [
            new HeroInventory { Name = "Меч", Description = "Длинный меч", Quantity = 5, Color = [200, 200, 200] }
        ];

        // Update only quantity, description should survive
        storage.ApplyUpdateWorldState("""{"hero":{"inventory":[{"id":0,"quantity":2}]}}""");

        var item = storage.WorldState.Hero.Inventory[0];
        Assert.Equal("Меч", item.Name);
        Assert.Equal("Длинный меч", item.Description);
        Assert.Equal(2, item.Quantity);
    }

    // ── Narrative ─────────────────────────────────────────────────────────────

    [Fact]
    public void UpdatesNarrativeWorld()
    {
        var storage = MakeStorage();
        storage.ApplyUpdateWorldState("""{"narrative":{"world":"Тёмное фэнтези"}}""");
        Assert.Equal("Тёмное фэнтези", storage.WorldState.Narrative!.World);
    }

    [Fact]
    public void AppendsPlotThreads()
    {
        var storage = MakeStorage();
        storage.WorldState.Narrative = new Narrative { PlotThreads = [] };
        storage.ApplyUpdateWorldState("""
            {"narrative":{"plotThreads":[{"name":"Заговор","status":"активна","description":"Тайный орден"}]}}
            """);
        Assert.Single(storage.WorldState.Narrative.PlotThreads!);
        Assert.Equal("Заговор", storage.WorldState.Narrative.PlotThreads![0].Name);
    }

    // ── Map ───────────────────────────────────────────────────────────────────

    [Fact]
    public void UpdatesMapColsAndRows()
    {
        var storage = MakeStorage();
        storage.ApplyUpdateWorldState("""{"map":{"cols":20,"rows":15}}""");
        Assert.Equal(20, storage.WorldState.Map.Cols);
        Assert.Equal(15, storage.WorldState.Map.Rows);
    }
}
