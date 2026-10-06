using NaviDnD;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class CalculateMovementTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static WorldState MakeWorld(int cols = 10, int rows = 10)
        => new() { Map = new MapConfig { Cols = cols, Rows = rows } };

    private static int AddEntity(WorldState ws, int col, int row, int speedMax = 30, string symbol = "ORC")
    {
        var entity = new LivingEntity { Symbol = symbol, Position = [col, row], SpeedMax = speedMax };
        (ws.Map.Entities ??= []).Add(entity);
        return ws.Map.Entities.Count - 1;
    }

    private static void SetHero(WorldState ws, int col, int row)
        => ws.Hero = new Hero { Symbol = "HRO", Position = [col, row] };

    private static MovementRequest Req(int entityId, params (string intent, string? target, int? minFt, int? maxFt, string? from)[] intents)
        => new()
        {
            EntityId = entityId,
            Intents = intents.Select(i => new MovementIntent
            {
                Intent = i.intent,
                Target = i.target,
                MinFt  = i.minFt,
                MaxFt  = i.maxFt,
                From   = i.from,
            }).ToList()
        };

    private static int Chebyshev(List<int> a, List<int> b)
        => Math.Max(Math.Abs(a[0] - b[0]), Math.Abs(a[1] - b[1]));

    private static bool IsReachable(
        Dictionary<(int col, int row, bool d), (int cost, (int col, int row, bool d)? parent)> reached,
        int col, int row)
        => reached.ContainsKey((col, row, false)) || reached.ContainsKey((col, row, true));

    // ── Approach ─────────────────────────────────────────────────────────────

    [Fact]
    public void Approach_MovesAdjacentToHero()
    {
        var ws = MakeWorld();
        SetHero(ws, 3, 5);
        int id = AddEntity(ws, 7, 5, speedMax: 30);

        var result = MovementCalculator.Calculate(ws, Req(id, ("approach", "hero", null, null, null)));

        Assert.Equal("approach", result.UsedIntent);
        Assert.Equal(1, Chebyshev(result.Steps[^1], ws.Hero!.Position!));
    }

    [Fact]
    public void Approach_PathStartsAtEntityPosition()
    {
        var ws = MakeWorld();
        SetHero(ws, 3, 5);
        int id = AddEntity(ws, 7, 5, speedMax: 30);

        var result = MovementCalculator.Calculate(ws, Req(id, ("approach", "hero", null, null, null)));

        Assert.Equal(7, result.Steps[0][0]);
        Assert.Equal(5, result.Steps[0][1]);
    }

    [Fact]
    public void Approach_HeroTooFar_MovesAsFarAsSpeedAllows()
    {
        var ws = MakeWorld(cols: 10, rows: 10);
        SetHero(ws, 1, 1);
        int id = AddEntity(ws, 10, 10, speedMax: 10); // 9 cells away, speed only 2 cells

        var result = MovementCalculator.Calculate(ws,
            Req(id, ("approach", "hero", null, null, null), ("stay", null, null, null, null)));

        // Не стоит на месте: идёт к герою, сколько хватает скорости (вторая диагональ — 10 фт, в 10 фт влезает одна).
        Assert.Equal("approach", result.UsedIntent);
        Assert.Equal([9, 9], result.Steps[^1]);
        Assert.True(result.RemainingSpeedFt >= 0);
    }

    [Fact]
    public void Approach_ReturnsNullWhenNoHero()
    {
        var ws = MakeWorld();
        int id = AddEntity(ws, 5, 5, speedMax: 30);
        // no hero

        var result = MovementCalculator.Calculate(ws,
            Req(id, ("approach", "hero", null, null, null), ("stay", null, null, null, null)));

        Assert.Equal("stay", result.UsedIntent);
    }

    [Fact]
    public void Approach_AlreadyAdjacent_StaysOrMovesOneStep()
    {
        var ws = MakeWorld();
        SetHero(ws, 5, 5);
        int id = AddEntity(ws, 6, 5, speedMax: 30); // already adjacent

        var result = MovementCalculator.Calculate(ws, Req(id, ("approach", "hero", null, null, null)));

        Assert.Equal("approach", result.UsedIntent);
        Assert.Equal(1, Chebyshev(result.Steps[^1], ws.Hero!.Position!));
    }

    // ── Maintain Distance ────────────────────────────────────────────────────

    [Fact]
    public void MaintainDistance_AlreadyInRange_Stays()
    {
        var ws = MakeWorld();
        SetHero(ws, 3, 5);
        int id = AddEntity(ws, 6, 5, speedMax: 30); // dist = 3 cells = 15ft

        var result = MovementCalculator.Calculate(ws,
            Req(id, ("maintain_distance", "hero", 10, 20, null)));

        Assert.Equal("maintain_distance", result.UsedIntent);
        Assert.Single(result.Steps);
        Assert.Equal(6, result.Steps[0][0]);
    }

    [Fact]
    public void MaintainDistance_TooClose_MovesAway()
    {
        var ws = MakeWorld();
        SetHero(ws, 5, 5);
        int id = AddEntity(ws, 6, 5, speedMax: 30); // dist=1 cell=5ft, want 15-30ft

        // Начиная позиция в пределах досягаемости героя (5ft) — уход из неё честно провоцирует
        // атаку по возможности (см. OA_TriggeredWhenLeavingHeroReach) и обрезает путь по границе
        // досягаемости. Здесь проверяется именно выбор целевой клетки Dijkstra, поэтому провокацию
        // отключаем явно, а не полагаемся на дефолт.
        var req = Req(id, ("maintain_distance", "hero", 15, 30, null));
        req.SkipOpportunityAttack = true;
        var result = MovementCalculator.Calculate(ws, req);

        Assert.Equal("maintain_distance", result.UsedIntent);
        int distFt = Chebyshev(result.Steps[^1], ws.Hero!.Position!) * 5;
        Assert.InRange(distFt, 15, 30);
    }

    [Fact]
    public void MaintainDistance_TooFar_MovesCloser()
    {
        var ws = MakeWorld();
        SetHero(ws, 1, 1);
        int id = AddEntity(ws, 9, 9, speedMax: 60); // dist=8 cells=40ft, want 15-25ft

        var result = MovementCalculator.Calculate(ws,
            Req(id, ("maintain_distance", "hero", 15, 25, null)));

        Assert.Equal("maintain_distance", result.UsedIntent);
        int distFt = Chebyshev(result.Steps[^1], ws.Hero!.Position!) * 5;
        Assert.InRange(distFt, 15, 25);
    }

    [Fact]
    public void MaintainDistance_FallsBackWhenImpossible()
    {
        var ws = MakeWorld(cols: 5, rows: 5);
        SetHero(ws, 3, 3);
        int id = AddEntity(ws, 3, 3, speedMax: 5); // on hero (distance 0), can only move 1 cell

        // Wants 20-30ft — impossible on 5x5 map with speed 5
        var result = MovementCalculator.Calculate(ws,
            Req(id, ("maintain_distance", "hero", 20, 30, null), ("stay", null, null, null, null)));

        Assert.Equal("stay", result.UsedIntent);
    }

    // ── Flee ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Flee_MovesAwayFromHero()
    {
        var ws = MakeWorld();
        SetHero(ws, 5, 5);
        int id = AddEntity(ws, 6, 5, speedMax: 30);

        // Начиная позиция в пределах досягаемости героя — см. комментарий в
        // MaintainDistance_TooClose_MovesAway: провокацию атаки по возможности отключаем явно,
        // чтобы проверить именно выбор целевой клетки, а не обрезку пути (это отдельно покрыто
        // OA_TriggeredWhenLeavingHeroReach).
        var req = Req(id, ("flee", null, null, null, "hero"));
        req.SkipOpportunityAttack = true;
        var result = MovementCalculator.Calculate(ws, req);

        Assert.Equal("flee", result.UsedIntent);
        int distBefore = Chebyshev([6, 5], ws.Hero!.Position!);
        int distAfter  = Chebyshev(result.Steps[^1], ws.Hero!.Position!);
        Assert.True(distAfter > distBefore);
    }

    [Fact]
    public void Flee_FromCoordinate()
    {
        var ws = MakeWorld();
        // No hero needed — flee from coordinate
        int id = AddEntity(ws, 5, 5, speedMax: 30);

        var result = MovementCalculator.Calculate(ws, Req(id, ("flee", null, null, null, "[3,5]")));

        Assert.Equal("flee", result.UsedIntent);
        int distBefore = MovementCalculator.ChebyshevDist(5, 5, 3, 5);
        int distAfter  = MovementCalculator.ChebyshevDist(result.Steps[^1][0], result.Steps[^1][1], 3, 5);
        Assert.True(distAfter > distBefore);
    }

    [Fact]
    public void Flee_FallsBackWhenAlreadyAtMax()
    {
        var ws = MakeWorld(cols: 3, rows: 1);
        SetHero(ws, 1, 1);
        int id = AddEntity(ws, 3, 1, speedMax: 30); // cornered at right edge, hero at left

        var result = MovementCalculator.Calculate(ws,
            Req(id, ("flee", null, null, null, "hero"), ("stay", null, null, null, null)));

        // Can't flee further (at map boundary)
        Assert.Equal("stay", result.UsedIntent);
    }

    // ── Stay ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Stay_AlwaysSucceeds()
    {
        var ws = MakeWorld();
        int id = AddEntity(ws, 5, 5, speedMax: 30);

        var result = MovementCalculator.Calculate(ws, Req(id, ("stay", null, null, null, null)));

        Assert.Equal("stay", result.UsedIntent);
        Assert.Single(result.Steps);
        Assert.Equal(5, result.Steps[0][0]);
        Assert.Equal(5, result.Steps[0][1]);
    }

    // ── Speed ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RemainingSpeed_CorrectAfterMove()
    {
        var ws = MakeWorld();
        SetHero(ws, 1, 5);
        int id = AddEntity(ws, 7, 5, speedMax: 30);

        var result = MovementCalculator.Calculate(ws, Req(id, ("approach", "hero", null, null, null)));

        Assert.Equal("approach", result.UsedIntent);
        Assert.True(result.RemainingSpeedFt >= 0);
        Assert.Equal(30 - (result.Steps.Count - 1) * 5, result.RemainingSpeedFt);
    }

    [Fact]
    public void SpeedFt_LimitsReachableArea()
    {
        var ws = MakeWorld();
        SetHero(ws, 1, 5);
        int id = AddEntity(ws, 8, 5, speedMax: 30); // hero 6 cells away; need 6 steps = 30ft to reach adjacent

        // With only 10ft (2 cells), can't reach — moves 2 cells toward the hero and stops.
        var req = new MovementRequest
        {
            EntityId = id,
            SpeedFt  = 10,
            Intents  = [new() { Intent = "approach" }, new() { Intent = "stay" }]
        };
        var result = MovementCalculator.Calculate(ws, req);

        Assert.Equal("approach", result.UsedIntent);
        Assert.Equal([6, 5], result.Steps[^1]);
        Assert.Equal(0, result.RemainingSpeedFt);
    }

    // ── Difficult terrain ────────────────────────────────────────────────────

    [Fact]
    public void DifficultTerrain_ReducesRange()
    {
        var ws = MakeWorld(cols: 10, rows: 1);
        // 1-row map forces straight path; cols 2-9 are difficult terrain (10ft per step)
        ws.Map.Area = [new Area
        {
            Name       = "Болото",
            StepCostFt = 10,
            Positions  = Enumerable.Range(2, 8).Select(c => new List<int> { c, 1 }).ToList()
        }];
        int id = AddEntity(ws, 1, 1, speedMax: 20); // 20ft = 2 difficult cells

        var reached = MovementCalculator.Dijkstra(ws, 1, 1, 20, id);

        // [2,1]=10ft, [3,1]=20ft reachable; [4,1]=30ft is not
        Assert.True(IsReachable(reached, 2, 1));
        Assert.True(IsReachable(reached, 3, 1));
        Assert.False(IsReachable(reached, 4, 1));
    }

    // ── Blocking ─────────────────────────────────────────────────────────────

    [Fact]
    public void EntityBlocks_CannotMoveThrough()
    {
        var ws = MakeWorld(cols: 5, rows: 1);
        SetHero(ws, 1, 1);
        AddEntity(ws, 3, 1, speedMax: 30, symbol: "BLK"); // blocker in between
        int id = AddEntity(ws, 5, 1, speedMax: 30);

        // Blocker at [3,1] — on a 1-row map, entity at [5,1] can't reach [2,1] adjacent to hero
        var reached = MovementCalculator.Dijkstra(ws, 5, 1, 30, id);

        Assert.False(IsReachable(reached, 2, 1));
        Assert.False(IsReachable(reached, 1, 1)); // hero also blocks
    }

    [Fact]
    public void HeroBlocks_CannotMoveOntoHero()
    {
        var ws = MakeWorld();
        SetHero(ws, 5, 5);
        int id = AddEntity(ws, 3, 5, speedMax: 30);

        var reached = MovementCalculator.Dijkstra(ws, 3, 5, 30, id);

        Assert.False(IsReachable(reached, 5, 5));
    }

    // ── Room boundaries ───────────────────────────────────────────────────────

    [Fact]
    public void RoomBoundary_BlocksWithoutDoor()
    {
        var ws = MakeWorld(cols: 10, rows: 5);
        SetHero(ws, 1, 3);

        // Two rooms separated by a boundary between col 5 and col 6
        ws.Map.Rooms =
        [
            new Room { Name = "Left",  Positions = Enumerable.Range(1, 5).SelectMany(c => Enumerable.Range(1, 5).Select(r => new List<int> { c, r })).ToList() },
            new Room { Name = "Right", Positions = Enumerable.Range(6, 5).SelectMany(c => Enumerable.Range(1, 5).Select(r => new List<int> { c, r })).ToList() },
        ];
        int id = AddEntity(ws, 8, 3, speedMax: 60);

        var reached = MovementCalculator.Dijkstra(ws, 8, 3, 60, id);

        // Entity in Right room cannot reach Left room without door
        Assert.False(IsReachable(reached, 1, 3));
        Assert.False(IsReachable(reached, 5, 3));
    }

    [Fact]
    public void RoomBoundary_PassableThroughOpenDoor()
    {
        var ws = MakeWorld(cols: 10, rows: 5);
        SetHero(ws, 1, 3);

        ws.Map.Rooms =
        [
            new Room { Name = "Left",  Positions = Enumerable.Range(1, 5).SelectMany(c => Enumerable.Range(1, 5).Select(r => new List<int> { c, r })).ToList() },
            new Room { Name = "Right", Positions = Enumerable.Range(6, 5).SelectMany(c => Enumerable.Range(1, 5).Select(r => new List<int> { c, r })).ToList() },
        ];
        ws.Map.Doors = [new Door { From = [5, 3], To = [6, 3], IsDoor = true, IsDoorOpen = true }];
        int id = AddEntity(ws, 8, 3, speedMax: 60);

        var reached = MovementCalculator.Dijkstra(ws, 8, 3, 60, id);

        Assert.True(IsReachable(reached, 5, 3));
        Assert.True(IsReachable(reached, 4, 3));
    }

    // ── Triggers ─────────────────────────────────────────────────────────────

    [Fact]
    public void OA_TriggeredWhenLeavingHeroReach()
    {
        var ws = MakeWorld();
        SetHero(ws, 5, 5);
        int id = AddEntity(ws, 6, 5, speedMax: 30); // adjacent to hero

        var req = new MovementRequest
        {
            EntityId = id,
            ReachFt  = 5,
            Intents  = [new() { Intent = "flee", From = "hero" }, new() { Intent = "stay" }]
        };
        var result = MovementCalculator.Calculate(ws, req);

        Assert.Equal("flee", result.UsedIntent);
        Assert.Contains(result.Triggers, t => t.Type == "opportunity_attack");
    }

    [Fact]
    public void OA_NotTriggeredWhenNotAdjacent()
    {
        var ws = MakeWorld();
        SetHero(ws, 1, 1);
        int id = AddEntity(ws, 8, 5, speedMax: 30); // far from hero

        var result = MovementCalculator.Calculate(ws,
            Req(id, ("flee", null, null, null, "hero")));

        Assert.DoesNotContain(result.Triggers, t => t.Type == "opportunity_attack");
    }

    [Fact]
    public void OnStep_TriggeredOnTrap()
    {
        // 1-row map forces the only path through [4,1] where the trap sits
        var ws = MakeWorld(cols: 7, rows: 1);
        SetHero(ws, 1, 1);
        ws.Map.Objects = [new CellEntity
        {
            Name     = "Капкан",
            Position = [4, 1],
            Triggers = new CellTriggers { OnStep = new TriggerEffect { Effect = "2d6 урон" } }
        }];
        int id = AddEntity(ws, 7, 1, speedMax: 30);

        var result = MovementCalculator.Calculate(ws, Req(id, ("approach", "hero", null, null, null)));

        var trap = result.Triggers.FirstOrDefault(t => t.Type == "onStep");
        Assert.NotNull(trap);
        Assert.Equal("Капкан", trap.ObjectName);
        Assert.Equal("2d6 урон", trap.Effect);
    }

    [Fact]
    public void AreaEnter_TriggeredOnFirstEntry()
    {
        var ws = MakeWorld();
        SetHero(ws, 1, 5);
        ws.Map.Area = [new Area
        {
            Name      = "Паутина",
            Positions = Enumerable.Range(4, 3).Select(c => new List<int> { c, 5 }).ToList(),
            Triggers  = new AreaTriggers { OnRound = new TriggerEffect { Effect = "Обездвижен" } }
        }];
        int id = AddEntity(ws, 8, 5, speedMax: 30);

        var result = MovementCalculator.Calculate(ws, Req(id, ("approach", "hero", null, null, null)));

        var enter = result.Triggers.FirstOrDefault(t => t.Type == "area_enter");
        Assert.NotNull(enter);
        Assert.Equal("Паутина", enter.ObjectName);
    }

    [Fact]
    public void AreaEnter_NotFiredAgainInsideArea()
    {
        var ws = MakeWorld();
        SetHero(ws, 1, 5);
        ws.Map.Area = [new Area
        {
            Name      = "Паутина",
            Positions = [[3, 5], [4, 5], [5, 5], [6, 5], [7, 5]],
            Triggers  = new AreaTriggers { OnRound = new TriggerEffect { Effect = "Обездвижен" } }
        }];
        int id = AddEntity(ws, 5, 5, speedMax: 30); // already inside area

        var result = MovementCalculator.Calculate(ws, Req(id, ("approach", "hero", null, null, null)));

        // Should not fire area_enter since entity starts inside and stays inside
        Assert.DoesNotContain(result.Triggers, t => t.Type == "area_enter");
    }

    // ── fromPosition override ────────────────────────────────────────────────

    [Fact]
    public void FromPosition_OverridesEntityPosition()
    {
        var ws = MakeWorld();
        SetHero(ws, 5, 5);
        int id = AddEntity(ws, 1, 1, speedMax: 30); // entity at [1,1]

        var req = new MovementRequest
        {
            EntityId     = id,
            FromPosition = [7, 5], // override: start from [7,5]
            Intents      = [new() { Intent = "approach" }]
        };
        var result = MovementCalculator.Calculate(ws, req);

        Assert.Equal(7, result.Steps[0][0]);
        Assert.Equal(5, result.Steps[0][1]);
        Assert.Equal(1, Chebyshev(result.Steps[^1], ws.Hero!.Position!));
    }

    // ── EntityId validation ──────────────────────────────────────────────────

    [Fact]
    public void InvalidEntityId_Throws()
    {
        var ws = MakeWorld();
        AddEntity(ws, 5, 5);

        var req = new MovementRequest { EntityId = 99, Intents = [new() { Intent = "stay" }] };
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementCalculator.Calculate(ws, req));
    }

    // ── ChebyshevDist ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(1, 1, 2, 2, 1)]
    [InlineData(1, 1, 4, 1, 3)]
    [InlineData(1, 1, 1, 5, 4)]
    [InlineData(3, 3, 1, 1, 2)]
    public void ChebyshevDist_Correct(int c1, int r1, int c2, int r2, int expected)
        => Assert.Equal(expected, MovementCalculator.ChebyshevDist(c1, r1, c2, r2));
}
