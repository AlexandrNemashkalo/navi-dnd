using NaviDnD;
using NaviDnD.Data;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

// Triggering of meeting/combat triggers by pure engine logic, without AI: spotted, onRound creatures,
// visibility in the dark (darkvisionFt/light), attack of opportunity and Withdrawal, patch triggers by plug-in.
// Save() inside TryMove writes to NaviDnD.Tests/Storage (AppConfig.ProjectRoot test build), not in combat save.
public class EncounterTriggerTests
{
    // One 10x10 room - there is a direct line of sight between all cells inside.
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
        enemy.VisionFt = 10; // 2 cells
        storage.WorldState.Map.Entities = [enemy];

        Assert.Empty(Types(Move(storage, "MoveEast"), "spotted"));          // (3,2): 3 cells - does not see
        Assert.Equal(["GB1"], Types(Move(storage, "MoveEast"), "spotted")); // (4,2): sees
        Assert.Empty(Types(Move(storage, "MoveWest"), "spotted"));          // (3,2): lost
        Assert.Equal(["GB1"], Types(Move(storage, "MoveEast"), "spotted")); // (4,2): found again
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
        var storage = MakeStorage(); // hero (2.2), enemy (6.2) - 20 ft.
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

    // ── Opportunity Attack and Retreat ──────────────────── ─────────────────────

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

    // ── onRound creatures ─────────────────────────── ───────────────────────────

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

    // ── The body of the murdered ──────────────────────────── ─────────────────────────────

    // The patch for id replaces the creature object with a new one: according to the link, a living rat after a
    // step looked killed - a body appeared on its cell, and the species ended up in the bestiary.
    [Fact]
    public void EnemyMovingInCombat_LeavesNoBodyAndStaysOutOfBestiary()
    {
        var storage = MakeStorage();
        var rat = MakeEnemy("RT1", 6, 2);
        rat.MonsterKey = "Крыса";
        storage.WorldState.Map.Entities = [rat, MakeEnemy("RT2", 7, 2)];
        StartCombat(storage, "RT1", "RT2");

        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"position":[3,2],"movePath":[[6,2],[5,2],[4,2],[3,2]]}]}}""");

        Assert.DoesNotContain(storage.WorldState.Map.Objects ?? [], o => o.Name?.Contains("RT1") == true || o.Symbol == "(†)");
        Assert.DoesNotContain("Крыса", storage.WorldState.Narrative?.KnownMonsters ?? []);
        Assert.Equal([3, 2], storage.WorldState.Map.Entities![0].Position);
    }

    // Killed - hit points 0: the game itself removes the creature from the map, leaves the body and replenishes the bestiary - and outside of battle too.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnemyAtZeroHp_IsRemovedLeavesBodyAndJoinsBestiary(bool inCombat)
    {
        var storage = MakeStorage();
        var rat = MakeEnemy("RT1", 6, 2);
        rat.MonsterKey = "Крыса";
        rat.Hp = "4/4";
        storage.WorldState.Map.Entities = [rat, MakeEnemy("RT2", 7, 2)];
        if (inCombat) StartCombat(storage, "RT1", "RT2");

        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"hp":"0/4"}]}}""");

        Assert.True(storage.WorldState.Map.Entities![0].Deleted);
        Assert.Equal("RT2", storage.WorldState.Map.Entities[1].Symbol);
        var body = Assert.Single(storage.WorldState.Map.Objects ?? [], o => o.Symbol == "(†)");
        Assert.Equal([6, 2], body.Position);
        Assert.Contains("Крыса", storage.WorldState.Narrative?.KnownMonsters ?? []);
    }

    // Escaped, left, dissipated - the GM removes a creature with hit points above 0: there is no body, it does not appear in the bestiary.
    [Fact]
    public void FledEnemy_LeavesNoBody()
    {
        var storage = MakeStorage();
        var rat = MakeEnemy("RT1", 6, 2);
        rat.MonsterKey = "Крыса";
        rat.Hp = "2/4";
        storage.WorldState.Map.Entities = [rat, MakeEnemy("RT2", 7, 2)];
        StartCombat(storage, "RT1", "RT2");

        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"deleted":true}]}}""");

        Assert.DoesNotContain(storage.WorldState.Map.Objects ?? [], o => o.Symbol == "(†)");
        Assert.DoesNotContain("Крыса", storage.WorldState.Narrative?.KnownMonsters ?? []);
    }

    // Hit points 0, but “Unconscious” - stunned, alive: remains on the map, no body.
    [Fact]
    public void KnockedOutEnemy_StaysOnMap()
    {
        var storage = MakeStorage();
        var bandit = MakeEnemy("BN1", 6, 2);
        bandit.Hp = "5/11";
        storage.WorldState.Map.Entities = [bandit];
        StartCombat(storage, "BN1");

        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"hp":"0/11","effects":[{"name":"Без сознания"}]}]}}""");

        Assert.NotEqual(true, storage.WorldState.Map.Entities![0].Deleted);
        Assert.DoesNotContain(storage.WorldState.Map.Objects ?? [], o => o.Symbol == "(†)");
    }

    // Hit points are reduced to 0 only by the mechanics in the text (“[… RT1 3→0]”) - the same death as the patch.
    [Fact]
    public void EnemyKilledByMechanicsText_LeavesBody()
    {
        var storage = MakeStorage();
        var rat = MakeEnemy("RT1", 6, 2);
        rat.Hp = "3/4";
        storage.WorldState.Map.Entities = [rat];

        Assert.True(storage.ReconcileMechanics("Крыса падает. [Атака 15, урон 5, RT1 3→0]"));

        Assert.True(storage.WorldState.Map.Entities![0].Deleted);
        Assert.Single(storage.WorldState.Map.Objects ?? [], o => o.Symbol == "(†)");
    }

    // ── Patch triggers via plug-in ────────────────────── ──────────────────────

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
    [Fact]
    public void CombatTriggerIncludesOtherHostilesEvenWhenTheyCannotSeeHero()
    {
        var storage = MakeStorage();
        var watcher = MakeEnemy("GB1", 6, 2);
        var other = MakeEnemy("GB2", 9, 9);
        other.VisionFt = 5;
        other.Hp = "7/7";
        storage.WorldState.Map.Entities = [watcher, other];
        Assert.False(MovementCalculator.EntitySeesHero(storage.WorldState, other));
        string context = new NaviDnD.Clients.AiContextBuilder(storage.WorldState)
            .TriggerState([(watcher, "spotted")], [], []);
        Assert.Contains("GB2", context);
        Assert.Contains("7/7", context);
        Assert.Contains("\"id\":1", context);
    }

    [Fact]
    public void FleeingEnemyKeepsIndicesForLaterHistoryPatches()
    {
        var storage = MakeStorage();
        var first = MakeEnemy("GB1", 6, 2); first.Hp = "7/7";
        var second = MakeEnemy("GB2", 7, 2); second.Hp = "7/7";
        storage.WorldState.Map.Entities = [first, second];
        StartCombat(storage, "GB1", "GB2");
        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"deleted":true}]}}""");
        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":1,"hp":"3/7"}]}}""");
        Assert.True(storage.WorldState.Map.Entities![0].Deleted);
        Assert.Equal("GB2", storage.WorldState.Map.Entities[1].Symbol);
        Assert.Equal("3/7", storage.WorldState.Map.Entities[1].Hp);
        Assert.DoesNotContain(storage.WorldState.Map.Objects ?? [], o => o.Symbol == "(†)");
    }

    [Fact]
    public void ZeroHpAndDeletedInSamePatchStillLeavesBody()
    {
        var storage = MakeStorage();
        var enemy = MakeEnemy("RT1", 6, 2); enemy.Hp = "3/4"; enemy.MonsterKey = "Крыса";
        storage.WorldState.Map.Entities = [enemy];
        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"hp":"0/4","deleted":true}]}}""");
        Assert.Single(storage.WorldState.Map.Objects ?? [], o => o.Symbol == "(†)");
        Assert.Contains("Крыса", storage.WorldState.Narrative?.KnownMonsters ?? []);
    }

    [Fact]
    public void TriggerContextResolvesPatchedCreatureBySymbolAndUsesCurrentData()
    {
        var storage = MakeStorage();
        var watcher = MakeEnemy("GB1", 6, 2); watcher.Hp = "7/7";
        storage.WorldState.Map.Entities = [watcher];
        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"hp":"5/7","position":[5,2]}]}}""");
        string context = new NaviDnD.Clients.AiContextBuilder(storage.WorldState)
            .TriggerState([(watcher, "spotted")], [], []);
        Assert.Contains("5/7", context);
        Assert.DoesNotContain("7/7", context);
        Assert.DoesNotContain("\"id\":-1", context);
        Assert.Equal(1, context.Split("\"symbol\":\"GB1\"").Length-1);
    }

    [Fact]
    public void DeathAfterMovementInSamePatchLeavesBodyAtFinalCell()
    {
        var storage = MakeStorage();
        var enemy = MakeEnemy("RT1", 6, 2); enemy.Hp = "3/4"; enemy.MonsterKey = "Крыса";
        storage.WorldState.Map.Entities = [enemy];
        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"position":[7,2],"hp":"0/4"}]}}""");
        var body = Assert.Single(storage.WorldState.Map.Objects ?? [], o => o.Symbol == "(†)");
        Assert.Equal([7,2], body.Position);
    }

    [Fact]
    public void RenamingCreatureDuringDeathStillUsesItsStableIndexAndFinalMetadata()
    {
        var storage = MakeStorage();
        var old = MakeEnemy("OLD", 1, 1); old.Deleted = true; old.Hp = "0/4";
        var rat = MakeEnemy("RT1", 6, 2); rat.Hp = "3/4"; rat.MonsterKey = "Крыса";
        storage.WorldState.Map.Entities = [old, rat];
        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":1,"symbol":"RT2","name":"Уличная крыса","position":[7,2],"hp":"0/4"}]}}""");
        var body = Assert.Single(storage.WorldState.Map.Objects ?? [], o => o.Symbol == "(†)");
        Assert.Equal("Тело: Уличная крыса", body.Name);
        Assert.Equal([7,2], body.Position);
        Assert.Contains("Крыса", storage.WorldState.Narrative?.KnownMonsters ?? []);
    }

    [Fact]
    public void ChangingNpcSymbolDoesNotRepeatItsRoundTrigger()
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Entities = [MakeTalkativeNpc()];
        Assert.Single(storage.TakeRoundBundle()!.EntityRoundTriggers);
        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"symbol":"BR2"}]}}""");
        Assert.Null(storage.TakeRoundBundle());
        storage.WorldState.Time.TotalRounds++;
        Assert.Equal("BR2", Assert.Single(storage.TakeRoundBundle()!.EntityRoundTriggers).entity.Symbol);
    }

    [Fact]
    public void RenamingCreaturePreservesCombatTurnReactionAndSightTracking()
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Entities = [MakeEnemy("GB1", 6, 2)];
        StartCombat(storage, "GB1");
        storage.WorldState.Combat!.CurrentTurn = "GB1";
        Assert.True(storage.TryMarkOpportunityAttackOnHero("GB1", 5));
        Assert.True(storage.UpdateEntitySeesHero("GB1", true));
        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"symbol":"GB2"}]}}""");
        Assert.Equal("GB2", storage.WorldState.Combat.CurrentTurn);
        Assert.Contains(storage.WorldState.Combat.Initiative!, i => i.Symbol == "GB2");
        Assert.DoesNotContain(storage.WorldState.Combat.Initiative!, i => i.Symbol == "GB1");
        Assert.False(storage.TryMarkOpportunityAttackOnHero("GB2", 5));
        Assert.False(storage.UpdateEntitySeesHero("GB2", true));
    }

    [Fact]
    public void RenamingHeroPreservesItsCombatTurn()
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Entities = [MakeEnemy("GB1", 6, 2)];
        StartCombat(storage, "GB1");
        string old = storage.WorldState.Hero!.Symbol;
        storage.WorldState.Combat!.CurrentTurn = old;
        storage.ApplyUpdateWorldState("""{"hero":{"symbol":"NEW"}}""");
        Assert.Equal("NEW", storage.WorldState.Combat.CurrentTurn);
        Assert.Contains(storage.WorldState.Combat.Initiative!, i => i.Symbol == "NEW");
    }

    [Fact]
    public void SwappingCreatureSymbolsRebindsReferencesSimultaneously()
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Entities = [MakeEnemy("GB1", 6, 2), MakeEnemy("GB2", 7, 2)];
        StartCombat(storage, "GB1", "GB2");
        storage.WorldState.Combat!.CurrentTurn = "GB1";
        storage.WorldState.Combat.Initiative!.Single(i => i.Symbol == "GB1").Score = 13;
        storage.WorldState.Combat.Initiative!.Single(i => i.Symbol == "GB2").Score = 7;
        int firstScore = storage.WorldState.Combat.Initiative!.Single(i => i.Symbol == "GB1").Score;
        int secondScore = storage.WorldState.Combat.Initiative!.Single(i => i.Symbol == "GB2").Score;
        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"symbol":"GB2"},{"id":1,"symbol":"GB1"}]}}""");
        Assert.Equal("GB2", storage.WorldState.Combat.CurrentTurn);
        Assert.Equal(firstScore, storage.WorldState.Combat.Initiative!.Single(i => i.Symbol == "GB2").Score);
        Assert.Equal(secondScore, storage.WorldState.Combat.Initiative!.Single(i => i.Symbol == "GB1").Score);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitNewCombatTurnIsNotReboundTwiceWhenSymbolsSwap(bool combatFirst)
    {
        var storage = MakeStorage();
        storage.WorldState.Map.Entities = [MakeEnemy("GB1", 6, 2), MakeEnemy("GB2", 7, 2)];
        StartCombat(storage, "GB1", "GB2");
        const string map = "\"map\":{\"entities\":[{\"id\":0,\"symbol\":\"GB2\"},{\"id\":1,\"symbol\":\"GB1\"}]}";
        const string combat = "\"combat\":{\"currentTurn\":\"GB1\"}";
        storage.ApplyUpdateWorldState("{" + (combatFirst ? combat + "," + map : map + "," + combat) + "}");
        Assert.Equal("GB1", storage.WorldState.Combat!.CurrentTurn);
        Assert.Contains(storage.WorldState.Combat.Initiative!, i => i.Symbol == "GB1");
        Assert.Contains(storage.WorldState.Combat.Initiative!, i => i.Symbol == "GB2");
    }

}
