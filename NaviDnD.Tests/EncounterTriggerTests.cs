using NaviDnD;
using NaviDnD.Data;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

// Срабатывание триггеров встречи/боя чистой логикой движка, без ИИ: spotted, onRound существ,
// видимость в темноте (darkvisionFt/light), атака по возможности и Отход, патч triggers по подключу.
// Save() внутри TryMove пишет в NaviDnD.Tests/Storage (AppConfig.ProjectRoot тестовой сборки), не в боевое сохранение.
public class EncounterTriggerTests
{
    // Одна комната 10×10 — между всеми клетками внутри есть прямая видимость.
    private static Room MakeRoom(int fromCol, int toCol, int fromRow = 1, int toRow = 10) => new()
    {
        Name = "Зал",
        Positions = [.. Enumerable.Range(fromCol, toCol - fromCol + 1)
            .SelectMany(c => Enumerable.Range(fromRow, toRow - fromRow + 1).Select(r => new List<int> { c, r }))]
    };

    private static Storage MakeStorage(int heroCol = 2, int heroRow = 2, int round = 5)
    {
        var storage = new Storage();
        var ws = storage.WorldState;
        ws.Time.TotalRounds = round;
        ws.Map.Rooms = [MakeRoom(1, 10)];
        ws.Hero = new Hero
        {
            Symbol = "HRO",
            Name = "Герой",
            Position = [heroCol, heroRow],
            SpeedMax = 30,
            SpeedLeft = 30,
            VisionFt = 60
        };
        return storage;
    }

    private static LivingEntity MakeEnemy(string symbol, int col, int row, bool spotted = true) => new()
    {
        Symbol = symbol,
        Name = symbol,
        Position = [col, row],
        Triggers = spotted ? new CellTriggers { Spotted = new TriggerEffect { Effect = "Атакует" } } : null
    };

    private static List<(CellEntity entity, string type)> Move(Storage storage, string direction)
    {
        var handler = new MovementHandler(storage.WorldState, storage, new DisplayConfig());
        handler.TryMove(direction, out _, out var triggers, out _, out _);
        return triggers;
    }

    private static List<string> Types(List<(CellEntity entity, string type)> triggers, string type) =>
        triggers.Where(t => t.type == type).Select(t => t.entity.Symbol).ToList();

    private static void StartCombat(Storage storage, params string[] symbols) =>
        storage.WorldState.Combat = new CombatState
        {
            Active = true,
            CurrentTurn = "HRO",
            Initiative = [new InitiativeEntry { Symbol = "HRO", Score = 15 },
                          .. symbols.Select(s => new InitiativeEntry { Symbol = s, Score = 10 })]
        };

    // ── Storage.UpdateEntitySeesHero ─────────────────────────────────────────

    [Fact]
    public void UpdateEntitySeesHero_TrueOnlyOnTransitionToSeeing()
    {
        var storage = new Storage();

        Assert.True(storage.UpdateEntitySeesHero("GB1", true));
        Assert.False(storage.UpdateEntitySeesHero("GB1", true));
        Assert.False(storage.UpdateEntitySeesHero("GB1", false));
        Assert.True(storage.UpdateEntitySeesHero("GB1", true));
    }

    // ── spotted ──────────────────────────────────────────────────────────────

    [Fact]
    public void Spotted_NeutralNpcWithoutTrigger_NeverFires()
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Entities = [MakeEnemy("BRN", 6, 2, spotted: false)];

        Assert.Empty(Types(Move(storage, "MoveEast"), "spotted"));
        Assert.Empty(Types(Move(storage, "MoveNorth"), "spotted"));
    }

    [Fact]
    public void Spotted_HostileFiresOnce_NotOnEveryStep()
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Entities = [MakeEnemy("GB1", 6, 2)];

        Assert.Equal(["GB1"], Types(Move(storage, "MoveEast"), "spotted"));
        Assert.Empty(Types(Move(storage, "MoveNorth"), "spotted"));
        Assert.Empty(Types(Move(storage, "MoveSouth"), "spotted"));
    }

    [Fact]
    public void Spotted_FiresAgain_AfterLosingAndRegainingSight()
    {
        var storage = MakeStorage();
        var enemy = MakeEnemy("GB1", 6, 2);
        enemy.VisionFt = 10; // 2 клетки
        storage.WorldState.Map.Entities = [enemy];

        Assert.Empty(Types(Move(storage, "MoveEast"), "spotted"));          // (3,2): 3 клетки — не видит
        Assert.Equal(["GB1"], Types(Move(storage, "MoveEast"), "spotted")); // (4,2): видит
        Assert.Empty(Types(Move(storage, "MoveWest"), "spotted"));          // (3,2): потерял
        Assert.Equal(["GB1"], Types(Move(storage, "MoveEast"), "spotted")); // (4,2): снова нашёл
    }

    [Fact]
    public void Spotted_EntityAlreadyInInitiative_DoesNotFire()
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Entities = [MakeEnemy("GB1", 6, 2)];
        StartCombat(storage, "GB1");

        Assert.Empty(Types(Move(storage, "MoveEast"), "spotted"));
    }

    [Fact]
    public void Spotted_HiddenAmbusher_Fires()
    {
        var storage = MakeStorage();
        var ambusher = MakeEnemy("MIM", 6, 2);
        ambusher.Hidden = true;
        storage.WorldState.Map.Entities = [ambusher];

        Assert.Equal(["MIM"], Types(Move(storage, "MoveEast"), "spotted"));
    }

    [Fact]
    public void Spotted_EnemyWithoutDarkvision_DoesNotFireInDark_FiresWhenHeroLightsTorch()
    {
        var storage = MakeStorage();
        var enemy = MakeEnemy("CU1", 6, 2);
        enemy.DarkvisionFt = 0;
        storage.WorldState.Map.Entities = [enemy];

        Assert.Empty(Types(Move(storage, "MoveEast"), "spotted"));

        storage.WorldState.Hero!.Light = new LightSource { Ft = 20, On = true };
        Assert.Equal(["CU1"], Types(Move(storage, "MoveNorth"), "spotted"));
    }

    // ── MovementCalculator.EntitySeesHero ────────────────────────────────────

    [Fact]
    public void EntitySeesHero_NullDarkvision_IgnoresDarkness()
    {
        var storage = MakeStorage();
        var enemy = MakeEnemy("GB1", 6, 2);

        Assert.True(MovementCalculator.EntitySeesHero(storage.WorldState, enemy));
    }

    [Fact]
    public void EntitySeesHero_NoDarkvision_DoesNotSeeHeroInDark()
    {
        var storage = MakeStorage();
        var enemy = MakeEnemy("CU1", 6, 2);
        enemy.DarkvisionFt = 0;

        Assert.False(MovementCalculator.EntitySeesHero(storage.WorldState, enemy));
    }

    [Fact]
    public void EntitySeesHero_NoDarkvision_SeesHeroWithLitTorch()
    {
        var storage = MakeStorage();
        storage.WorldState.Hero!.Light = new LightSource { Ft = 20, On = true };
        var enemy = MakeEnemy("CU1", 6, 2);
        enemy.DarkvisionFt = 0;

        Assert.True(MovementCalculator.EntitySeesHero(storage.WorldState, enemy));
    }

    [Fact]
    public void EntitySeesHero_NoDarkvision_HeroUnderWallTorch_OnlyWhenTorchIsOn()
    {
        var storage = MakeStorage();
        var torch = new CellEntity { Symbol = "(T)", Name = "Факел", Position = [2, 4], Light = new LightSource { Ft = 20, On = false } };
        storage.WorldState.Map.Objects = [torch];
        var enemy = MakeEnemy("CU1", 6, 2);
        enemy.DarkvisionFt = 0;

        Assert.False(MovementCalculator.EntitySeesHero(storage.WorldState, enemy));

        torch.Light.On = true;
        Assert.True(MovementCalculator.EntitySeesHero(storage.WorldState, enemy));
    }

    [Fact]
    public void EntitySeesHero_TorchOutOfRange_DoesNotLightHeroCell()
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Objects =
            [new CellEntity { Symbol = "(T)", Name = "Факел", Position = [2, 9], Light = new LightSource { Ft = 10, On = true } }];
        var enemy = MakeEnemy("CU1", 6, 2);
        enemy.DarkvisionFt = 0;

        Assert.False(MovementCalculator.EntitySeesHero(storage.WorldState, enemy));
    }

    [Fact]
    public void EntitySeesHero_Darkvision_SeesInDarkOnlyWithinRange()
    {
        var storage = MakeStorage(); // герой (2,2), враг (6,2) — 20 фт
        var enemy = MakeEnemy("GB1", 6, 2);

        enemy.DarkvisionFt = 60;
        Assert.True(MovementCalculator.EntitySeesHero(storage.WorldState, enemy));

        enemy.DarkvisionFt = 10;
        Assert.False(MovementCalculator.EntitySeesHero(storage.WorldState, enemy));
    }

    [Fact]
    public void EntitySeesHero_BeyondVisionRange_DoesNotSee()
    {
        var storage = MakeStorage();
        var enemy = MakeEnemy("GB1", 6, 2);
        enemy.VisionFt = 15;

        Assert.False(MovementCalculator.EntitySeesHero(storage.WorldState, enemy));
    }

    [Fact]
    public void EntitySeesHero_WallBetweenRooms_BlocksSight_OpenPassageDoesNot()
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Rooms = [MakeRoom(1, 3), MakeRoom(4, 10)];
        var enemy = MakeEnemy("GB1", 6, 2);

        Assert.False(MovementCalculator.EntitySeesHero(storage.WorldState, enemy));

        storage.WorldState.Map.Doors = [new Door { From = [3, 2], To = [4, 2], IsDoor = false }];
        Assert.True(MovementCalculator.EntitySeesHero(storage.WorldState, enemy));
    }

    // ── Атака по возможности и Отход ─────────────────────────────────────────

    [Fact]
    public void OpportunityAttack_LeavingAdjacentEnemyInCombat_Fires()
    {
        var storage = MakeStorage(heroCol: 3);
        storage.WorldState.Map.Entities = [MakeEnemy("GB1", 4, 2)];
        StartCombat(storage, "GB1");

        Assert.Equal(["GB1"], Types(Move(storage, "MoveWest"), "opportunity_attack_on_hero"));
    }

    [Fact]
    public void OpportunityAttack_OutsideCombat_DoesNotFire()
    {
        var storage = MakeStorage(heroCol: 3);
        storage.WorldState.Map.Entities = [MakeEnemy("GB1", 4, 2, spotted: false)];

        Assert.Empty(Types(Move(storage, "MoveWest"), "opportunity_attack_on_hero"));
    }

    [Fact]
    public void OpportunityAttack_AfterDisengageThisRound_DoesNotFire()
    {
        var storage = MakeStorage(heroCol: 3, round: 5);
        storage.WorldState.Map.Entities = [MakeEnemy("GB1", 4, 2)];
        StartCombat(storage, "GB1");
        storage.WorldState.Hero!.NoOpportunityAttacksRound = 5;

        Assert.Empty(Types(Move(storage, "MoveWest"), "opportunity_attack_on_hero"));
    }

    [Fact]
    public void OpportunityAttack_DisengageFromPreviousRound_NoLongerProtects()
    {
        var storage = MakeStorage(heroCol: 3, round: 5);
        storage.WorldState.Map.Entities = [MakeEnemy("GB1", 4, 2)];
        StartCombat(storage, "GB1");
        storage.WorldState.Hero!.NoOpportunityAttacksRound = 4;

        Assert.Equal(["GB1"], Types(Move(storage, "MoveWest"), "opportunity_attack_on_hero"));
    }

    [Fact]
    public void Disengage_PatchedFromAi_SetsHeroField()
    {
        var storage = MakeStorage();
        storage.ApplyUpdateWorldState("""{"hero":{"noOpportunityAttacksRound":5,"stealth":17,"light":{"ft":20,"on":true}}}""");

        var hero = storage.WorldState.Hero!;
        Assert.Equal(5, hero.NoOpportunityAttacksRound);
        Assert.Equal(17, hero.Stealth);
        Assert.True(hero.Light?.On);

        storage.ApplyUpdateWorldState("""{"hero":{"stealth":null,"light":null}}""");
        Assert.Null(hero.Stealth);
        Assert.Null(hero.Light);
    }

    // ── onRound существ ──────────────────────────────────────────────────────

    private static LivingEntity MakeTalkativeNpc(int col = 6, int row = 2) => new()
    {
        Symbol = "BRN",
        Name = "Бронн",
        Position = [col, row],
        Triggers = new CellTriggers { OnRound = new TriggerEffect { Effect = "Просит воды" } }
    };

    [Fact]
    public void EntityOnRound_FiresOncePerRound_WhenSeesHero()
    {
        var storage = MakeStorage(round: 5);
        storage.WorldState.Map.Entities = [MakeTalkativeNpc()];

        var bundle = storage.TakeRoundBundle();
        Assert.NotNull(bundle);
        Assert.Equal("BRN", Assert.Single(bundle.EntityRoundTriggers).entity.Symbol);

        Assert.Null(storage.TakeRoundBundle());

        storage.WorldState.Time.TotalRounds = 6;
        Assert.Single(storage.TakeRoundBundle()!.EntityRoundTriggers);
    }

    [Fact]
    public void EntityOnRound_DoesNotFire_WhenNpcDoesNotSeeHero()
    {
        var storage = MakeStorage();
        var npc = MakeTalkativeNpc();
        npc.VisionFt = 10;
        storage.WorldState.Map.Entities = [npc];

        Assert.Null(storage.TakeRoundBundle());
    }

    [Fact]
    public void EntityOnRound_DoesNotFire_WhenNpcInInitiative()
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Entities = [MakeTalkativeNpc()];
        StartCombat(storage, "BRN");

        Assert.Null(storage.TakeRoundBundle());
    }

    [Fact]
    public void EntityOnRound_DoesNotFire_OnMovementSteps()
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Entities = [MakeTalkativeNpc()];

        Assert.Empty(Move(storage, "MoveNorth"));
        Assert.Empty(Move(storage, "MoveNorth"));
    }

    // ── Патч triggers по подключу ────────────────────────────────────────────

    [Fact]
    public void TriggersPatch_NullSubKey_RemovesOnlyThatTrigger()
    {
        var storage = MakeStorage();
        var enemy = MakeEnemy("GB1", 6, 2);
        enemy.Triggers!.OnRound = new TriggerEffect { Effect = "Рычит" };
        storage.WorldState.Map.Entities = [enemy];

        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"triggers":{"spotted":null}}]}}""");

        var triggers = storage.WorldState.Map.Entities![0].Triggers;
        Assert.Null(triggers?.Spotted);
        Assert.Equal("Рычит", triggers?.OnRound?.Effect);
    }

    [Fact]
    public void TriggersPatch_AddSpotted_KeepsExistingTriggers()
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Entities = [MakeTalkativeNpc()];

        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"triggers":{"spotted":{"effect":"Нападает"}}}]}}""");

        var triggers = storage.WorldState.Map.Entities![0].Triggers;
        Assert.Equal("Нападает", triggers?.Spotted?.Effect);
        Assert.Equal("Просит воды", triggers?.OnRound?.Effect);
    }

    [Fact]
    public void Spotted_AddedToNeutralNpcAfterTurningHostile_FiresOnNextEncounter()
    {
        var storage = MakeStorage();
        var npc = MakeEnemy("BRN", 6, 2, spotted: false);
        npc.VisionFt = 10;
        storage.WorldState.Map.Entities = [npc];

        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"triggers":{"spotted":{"effect":"Нападает"}}}]}}""");

        Move(storage, "MoveEast");
        Assert.Equal(["BRN"], Types(Move(storage, "MoveEast"), "spotted"));
    }
}
