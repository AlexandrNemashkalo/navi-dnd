using NaviDnD.Data.Models;
using System.Text.Json.Serialization;

namespace NaviDnD;

// ── Models ──────────────────────────────────────────────────────────────────

public class MovementRequest
{
    public int EntityId { get; set; }
    public int ReachFt { get; set; } = 5;
    public List<int>? FromPosition { get; set; }
    public int? SpeedFt { get; set; }
    public int DiagonalsUsed { get; set; } = 0;
    public bool SkipOpportunityAttack { get; set; } = false;
    public List<MovementIntent> Intents { get; set; } = [];
}

public class MovementIntent
{
    public string Intent { get; set; } = "";
    public string? Target { get; set; }
    public int? MinFt { get; set; }
    public int? MaxFt { get; set; }
    public string? From { get; set; }
}

public class MovementResult
{
    public string UsedIntent { get; set; } = "";
    // Видна ли цель (герой или существо из intent.target) с конечной клетки: дистанция обзора и линия видимости.
    // Строка seesHero в контексте посчитана до хода — после перемещения стрелку нужен этот ответ.
    public bool? TargetVisible { get; set; }
    public List<List<int>> Steps { get; set; } = [];
    public int RemainingSpeedFt { get; set; }
    public int DiagonalsUsed { get; set; }
    public List<MovementTriggerResult> Triggers { get; set; } = [];
}

public class MovementTriggerResult
{
    public int AtStep { get; set; }
    public string Type { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? PausePosition { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ObjectName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Effect { get; set; }
}

// ── Calculator ───────────────────────────────────────────────────────────────

public static class MovementCalculator
{
    public static MovementResult Calculate(WorldState ws, MovementRequest req)
    {
        var entities = ws.Map.Entities ?? [];

        if (req.EntityId < 0 || req.EntityId >= entities.Count)
            throw new ArgumentOutOfRangeException(nameof(req.EntityId),
                $"entityId {req.EntityId} out of range (0..{entities.Count - 1})");

        var entity = entities[req.EntityId];
        var startPos = req.FromPosition ?? entity.Position
                       ?? throw new ArgumentException($"Entity {req.EntityId} has no position");

        int startCol = startPos[0], startRow = startPos[1];
        int speedFt  = req.SpeedFt ?? entity.SpeedMax ?? 30;
        var heroPos  = ws.Hero?.Position;
        bool initialDiagUsed = req.DiagonalsUsed > 0;

        var reached = Dijkstra(ws, startCol, startRow, speedFt, req.EntityId, initialDiagUsed);

        foreach (var intent in req.Intents)
        {
            // Цель — герой или любое существо по символу (враг идёт к союзнику героя, держит дистанцию от него).
            var targetPos = TargetPosition(ws, intent.Target, heroPos);
            var result = TryIntent(ws, intent, reached, startCol, startRow, speedFt, targetPos, initialDiagUsed)
                         ?? (intent.Intent.Equals("approach", StringComparison.OrdinalIgnoreCase)
                             ? TryApproachFar(ws, req.EntityId, reached, startCol, startRow, speedFt, targetPos, initialDiagUsed)
                             : null);
            if (result == null) continue;

            var lastPos  = result.Steps.Count > 0 ? result.Steps[^1] : [startCol, startRow];
            var bestInfo = GetBestState(reached, lastPos[0], lastPos[1]);
            int usedCost = bestInfo?.cost ?? 0;
            result.RemainingSpeedFt = speedFt - usedCost;
            result.DiagonalsUsed    = req.DiagonalsUsed + CountDiagonals(result.Steps);
            result.Triggers = CollectTriggers(ws, result.Steps, heroPos, req.ReachFt, startCol, startRow);
            if (targetPos is { Count: >= 2 })
            {
                var end = result.Steps.Count > 0 ? result.Steps[^1] : [startCol, startRow];
                int sight = entity.VisionFt ?? DaylightFt(ws, targetPos[0], targetPos[1]) ?? 30;
                result.TargetVisible = sight != 0
                    && (sight < 0 || TargetGeometry.DistanceFt(end[0], end[1], targetPos[0], targetPos[1]) <= Math.Max(sight, entity.DarkvisionFt ?? 0))
                    && HasLineOfSight(ws, end[0], end[1], targetPos[0], targetPos[1]);
            }

            if (!req.SkipOpportunityAttack)
            {
                var oaTrigger = result.Triggers.Find(t => t.Type == "opportunity_attack");
                if (oaTrigger != null)
                {
                    result.Steps = result.Steps.Take(oaTrigger.AtStep + 1).ToList();
                    var pauseInfo = GetBestState(reached, result.Steps[^1][0], result.Steps[^1][1]);
                    result.RemainingSpeedFt = speedFt - (pauseInfo?.cost ?? 0);
                    result.DiagonalsUsed    = req.DiagonalsUsed + CountDiagonals(result.Steps);
                    result.Triggers = [oaTrigger];
                }
            }

            return result;
        }

        return new MovementResult
        {
            UsedIntent    = "stay",
            Steps         = [[startCol, startRow]],
            RemainingSpeedFt = speedFt,
            DiagonalsUsed = req.DiagonalsUsed,
        };
    }

    private static List<int>? TargetPosition(WorldState ws, string? target, List<int>? heroPos)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Equals("hero", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, ws.Hero?.Symbol, StringComparison.OrdinalIgnoreCase))
            return heroPos;
        return ws.Map.Entities?.FirstOrDefault(e => e.Deleted != true && string.Equals(e.Symbol, target, StringComparison.OrdinalIgnoreCase))?.Position
               ?? heroPos;
    }

    // ── Intent resolution ────────────────────────────────────────────────────

    private static MovementResult? TryIntent(
        WorldState ws, MovementIntent intent,
        Dictionary<(int col, int row, bool d), (int cost, (int col, int row, bool d)? parent)> reached,
        int startCol, int startRow, int speedFt, List<int>? heroPos, bool initialDiagUsed)
    {
        return intent.Intent.ToLowerInvariant() switch
        {
            "stay" => new MovementResult
            {
                UsedIntent = "stay",
                Steps = [[startCol, startRow]],
                RemainingSpeedFt = speedFt,
            },
            "approach"          => TryApproach(reached, startCol, startRow, heroPos, initialDiagUsed),
            "maintain_distance" => TryMaintainDistance(ws, intent, reached, startCol, startRow, heroPos, initialDiagUsed),
            "flee"              => TryFlee(intent, reached, startCol, startRow, heroPos, initialDiagUsed),
            _                   => null,
        };
    }

    private static MovementResult? TryApproach(
        Dictionary<(int col, int row, bool d), (int cost, (int col, int row, bool d)? parent)> reached,
        int startCol, int startRow, List<int>? heroPos, bool initialDiagUsed)
    {
        if (heroPos is not { Count: >= 2 }) return null;
        int hCol = heroPos[0], hRow = heroPos[1];

        (int col, int row, int cost)? best = null;
        foreach (var kvp in reached)
        {
            if (ChebyshevDist(kvp.Key.col, kvp.Key.row, hCol, hRow) == 1)
            {
                if (best == null || kvp.Value.cost < best.Value.cost)
                    best = (kvp.Key.col, kvp.Key.row, kvp.Value.cost);
            }
        }
        if (best == null) return null;

        return new MovementResult
        {
            UsedIntent = "approach",
            Steps = ReconstructPath(reached, (startCol, startRow, initialDiagUsed), (best.Value.col, best.Value.row)),
        };
    }

    // Герой дальше, чем враг дойдёт за ход: идти к нему по настоящему пути (в обход деревьев/стен) так далеко,
    // как хватает скорости. Раньше approach в этом случае не срабатывал, и далёкий враг просто стоял («stay»).
    private static MovementResult? TryApproachFar(
        WorldState ws, int entityId,
        Dictionary<(int col, int row, bool d), (int cost, (int col, int row, bool d)? parent)> reached,
        int startCol, int startRow, int speedFt, List<int>? heroPos, bool initialDiagUsed)
    {
        if (heroPos is not { Count: >= 2 }) return null;
        int hCol = heroPos[0], hRow = heroPos[1];
        var far = Dijkstra(ws, startCol, startRow, 2000, entityId, initialDiagUsed);
        (int col, int row, int cost)? best = null;
        foreach (var kvp in far)
            if (ChebyshevDist(kvp.Key.col, kvp.Key.row, hCol, hRow) == 1 && (best == null || kvp.Value.cost < best.Value.cost))
                best = (kvp.Key.col, kvp.Key.row, kvp.Value.cost);
        if (best == null) return null;   // к герою не пройти вовсе

        var full = ReconstructPath(far, (startCol, startRow, initialDiagUsed), (best.Value.col, best.Value.row));
        // Сколько пути влезает в скорость: клетка пути должна быть достижима за этот ход (есть в reached).
        var steps = new List<List<int>>();
        foreach (var step in full)
        {
            if (GetBestState(reached, step[0], step[1]) is not { } st || st.cost > speedFt) break;
            steps.Add(step);
        }
        if (steps.Count == 0) steps.Add([startCol, startRow]);
        return new MovementResult { UsedIntent = "approach", Steps = steps };
    }

    // Держать дистанцию — для стрелков: клетка в диапазоне, откуда цель ВИДНО (линия обзора), ближайшая по пути;
    // раньше стрелок за деревьями «держал дистанцию» на месте и ход за ходом не мог выстрелить. Видимых клеток
    // в досягаемости нет — любая в диапазоне, как раньше.
    private static MovementResult? TryMaintainDistance(
        WorldState ws, MovementIntent intent,
        Dictionary<(int col, int row, bool d), (int cost, (int col, int row, bool d)? parent)> reached,
        int startCol, int startRow, List<int>? heroPos, bool initialDiagUsed)
    {
        if (heroPos is not { Count: >= 2 }) return null;
        int hCol = heroPos[0], hRow = heroPos[1];
        int minFt = intent.MinFt ?? 0;
        int maxFt = intent.MaxFt ?? int.MaxValue;
        bool InRange(int c, int r) { int d = TargetGeometry.DistanceFt(c, r, hCol, hRow); return d >= minFt && d <= maxFt; }
        bool Sees(int c, int r) => HasLineOfSight(ws, c, r, hCol, hRow);

        if (InRange(startCol, startRow) && Sees(startCol, startRow))
            return new MovementResult { UsedIntent = "maintain_distance", Steps = [[startCol, startRow]] };

        (int col, int row, int cost)? best = null, bestSeen = null;
        foreach (var kvp in reached)
        {
            if (!InRange(kvp.Key.col, kvp.Key.row)) continue;
            if (best == null || kvp.Value.cost < best.Value.cost)
                best = (kvp.Key.col, kvp.Key.row, kvp.Value.cost);
            if ((bestSeen == null || kvp.Value.cost < bestSeen.Value.cost) && Sees(kvp.Key.col, kvp.Key.row))
                bestSeen = (kvp.Key.col, kvp.Key.row, kvp.Value.cost);
        }
        best = bestSeen ?? (InRange(startCol, startRow) ? (startCol, startRow, 0) : best);
        if (best == null) return null;

        return new MovementResult
        {
            UsedIntent = "maintain_distance",
            Steps = ReconstructPath(reached, (startCol, startRow, initialDiagUsed), (best.Value.col, best.Value.row)),
        };
    }

    private static MovementResult? TryFlee(
        MovementIntent intent,
        Dictionary<(int col, int row, bool d), (int cost, (int col, int row, bool d)? parent)> reached,
        int startCol, int startRow, List<int>? heroPos, bool initialDiagUsed)
    {
        int fromCol, fromRow;
        if (intent.From == null || intent.From.Equals("hero", StringComparison.OrdinalIgnoreCase))
        {
            if (heroPos is not { Count: >= 2 }) return null;
            fromCol = heroPos[0]; fromRow = heroPos[1];
        }
        else
        {
            var trimmed = intent.From.Trim('[', ']');
            var parts = trimmed.Split(',');
            if (parts.Length < 2
                || !int.TryParse(parts[0].Trim(), out fromCol)
                || !int.TryParse(parts[1].Trim(), out fromRow))
                return null;
        }

        int currentDist = ChebyshevDist(startCol, startRow, fromCol, fromRow);

        (int col, int row, int dist, int cost)? best = null;
        foreach (var kvp in reached)
        {
            int d = ChebyshevDist(kvp.Key.col, kvp.Key.row, fromCol, fromRow);
            if (d > currentDist)
            {
                if (best == null || d > best.Value.dist || (d == best.Value.dist && kvp.Value.cost < best.Value.cost))
                    best = (kvp.Key.col, kvp.Key.row, d, kvp.Value.cost);
            }
        }
        if (best == null) return null;

        return new MovementResult
        {
            UsedIntent = "flee",
            Steps = ReconstructPath(reached, (startCol, startRow, initialDiagUsed), (best.Value.col, best.Value.row)),
        };
    }

    // ── Dijkstra ─────────────────────────────────────────────────────────────

    public static Dictionary<(int col, int row, bool d), (int cost, (int col, int row, bool d)? parent)> Dijkstra(
        WorldState ws, int startCol, int startRow, int maxCostFt, int entityId, bool initialDiagUsed = false, bool passPeaceful = false)
    {
        var dist = new Dictionary<(int, int, bool), (int cost, (int, int, bool)? parent)>();
        var pq   = new PriorityQueue<(int col, int row, bool d), int>();

        dist[(startCol, startRow, initialDiagUsed)] = (0, null);
        pq.Enqueue((startCol, startRow, initialDiagUsed), 0);

        Span<int> dcols = [-1, 0, 1, -1, 1, -1, 0, 1];
        Span<int> drows = [-1, -1, -1, 0, 0, 1, 1, 1];

        while (pq.Count > 0)
        {
            var (col, row, diagUsed) = pq.Dequeue();
            int curCost = dist[(col, row, diagUsed)].cost;

            for (int i = 0; i < 8; i++)
            {
                int nc = col + dcols[i], nr = row + drows[i];
                bool isDiag    = dcols[i] != 0 && drows[i] != 0;
                if (!CanMoveTo(ws, col, row, nc, nr, entityId))
                {
                    // Герой протискивается мимо мирного существа на свободную клетку за ним (два шага разом).
                    if (!passPeaceful || PeacefulEntityIndex(ws, nc, nr) is not int bystander) continue;
                    int fc = nc + dcols[i], fr = nr + drows[i];
                    if (!CanMoveTo(ws, col, row, nc, nr, bystander) || !CanMoveTo(ws, nc, nr, fc, fr, entityId)) continue;
                    bool passDiag = diagUsed || isDiag;
                    int passCost = curCost + GetStepCost(ws, nc, nr, isDiag, diagUsed) + GetStepCost(ws, fc, fr, isDiag, passDiag);
                    if (passCost > maxCostFt) continue;
                    if (dist.TryGetValue((fc, fr, passDiag), out var seen) && seen.cost <= passCost) continue;
                    dist[(fc, fr, passDiag)] = (passCost, (col, row, diagUsed));
                    pq.Enqueue((fc, fr, passDiag), passCost);
                    continue;
                }

                bool newDiag   = diagUsed || isDiag;
                int  newCost   = curCost + GetStepCost(ws, nc, nr, isDiag, diagUsed);
                if (newCost > maxCostFt) continue;
                if (dist.TryGetValue((nc, nr, newDiag), out var existing) && existing.cost <= newCost) continue;

                dist[(nc, nr, newDiag)] = (newCost, (col, row, diagUsed));
                pq.Enqueue((nc, nr, newDiag), newCost);
            }
        }

        return dist;
    }

    // ── Path reconstruction ──────────────────────────────────────────────────

    private static List<List<int>> ReconstructPath(
        Dictionary<(int col, int row, bool d), (int cost, (int col, int row, bool d)? parent)> reached,
        (int col, int row, bool d) from, (int col, int row) to)
    {
        var path = new List<(int, int)>();
        var best = GetBestState(reached, to.col, to.row)
                   ?? throw new InvalidOperationException($"Destination ({to.col},{to.row}) not in reached");
        var cur  = best.state;
        while ((cur.col, cur.row) != (from.col, from.row))
        {
            path.Add((cur.col, cur.row));
            if (!reached.TryGetValue(cur, out var info) || info.parent == null) break;
            cur = info.parent.Value;
        }
        path.Add((from.col, from.row));
        path.Reverse();
        return path.Select(p => new List<int> { p.Item1, p.Item2 }).ToList();
    }

    // Returns the minimum-cost state among (col,row,false) and (col,row,true).
    private static (int cost, (int col, int row, bool d)? parent, (int col, int row, bool d) state)?
        GetBestState(Dictionary<(int, int, bool), (int cost, (int, int, bool)? parent)> reached, int col, int row)
    {
        (int cost, (int, int, bool)? parent, (int, int, bool) state)? best = null;
        if (reached.TryGetValue((col, row, false), out var f))
            best = (f.cost, f.parent, (col, row, false));
        if (reached.TryGetValue((col, row, true), out var t) && (best == null || t.cost < best.Value.cost))
            best = (t.cost, t.parent, (col, row, true));
        return best;
    }

    private static int CountDiagonals(List<List<int>> steps)
    {
        int count = 0;
        for (int i = 1; i < steps.Count; i++)
        {
            if (steps[i].Count >= 2 && steps[i-1].Count >= 2
                && steps[i][0] != steps[i-1][0] && steps[i][1] != steps[i-1][1])
                count++;
        }
        return count;
    }

    // ── Trigger collection ───────────────────────────────────────────────────

    public static List<MovementTriggerResult> CollectTriggers(
        WorldState ws, List<List<int>> steps, List<int>? heroPos,
        int reachFt, int startCol, int startRow)
    {
        var triggers = new List<MovementTriggerResult>();
        if (steps.Count == 0) return triggers;

        int hCol = heroPos is { Count: >= 2 } ? heroPos[0] : -1;
        int hRow = heroPos is { Count: >= 2 } ? heroPos[1] : -1;
        int reachCells = Math.Max(1, reachFt / 5);

        bool wasInReach = hCol >= 0 && ChebyshevDist(startCol, startRow, hCol, hRow) <= reachCells;

        for (int s = 0; s < steps.Count; s++)
        {
            int col = steps[s][0], row = steps[s][1];

            if (hCol >= 0)
            {
                bool isInReach = ChebyshevDist(col, row, hCol, hRow) <= reachCells;
                if (wasInReach && !isInReach && s > 0)
                {
                    triggers.Add(new MovementTriggerResult
                    {
                        AtStep       = s - 1,
                        Type         = "opportunity_attack",
                        PausePosition = steps[s - 1],
                    });
                }
                wasInReach = isInReach;
            }

            foreach (var obj in (ws.Map.Objects ?? []).Cast<CellEntity>().Concat(ws.Map.Entities ?? []))
            {
                if (obj.Deleted == true || obj.Triggers?.OnStep == null) continue;
                if (obj.Position is { Count: >= 2 } && obj.Position[0] == col && obj.Position[1] == row)
                {
                    triggers.Add(new MovementTriggerResult
                    {
                        AtStep     = s,
                        Type       = "onStep",
                        ObjectName = obj.Name,
                        Effect     = obj.Triggers.OnStep.Effect,
                    });
                }
            }

            foreach (var area in ws.Map.Area ?? [])
            {
                if (area.Deleted == true || area.Triggers == null) continue;
                if (area.Triggers.OnRound == null && area.Triggers.OnExplored == null) continue;
                if (area.Positions?.Any(p => p.Count >= 2 && p[0] == col && p[1] == row) != true) continue;

                // At step 0 the entity IS at the start — if already inside, it's not an entry.
                bool wasAlreadyIn = s == 0
                    ? area.Positions!.Any(p => p.Count >= 2 && p[0] == col && p[1] == row)
                    : area.Positions!.Any(p => p.Count >= 2 && p[0] == steps[s - 1][0] && p[1] == steps[s - 1][1]);
                if (!wasAlreadyIn)
                {
                    triggers.Add(new MovementTriggerResult
                    {
                        AtStep     = s,
                        Type       = "area_enter",
                        ObjectName = area.Name,
                        Effect     = area.Triggers.OnRound?.Effect ?? area.Triggers.OnExplored?.Effect,
                    });
                }
            }
        }

        return triggers;
    }

    // ── Map helpers ──────────────────────────────────────────────────────────

    // Мирное существо: не в бою и без триггера «заметил героя» (spotted — у врагов). Сквозь такое герой
    // может протиснуться на свободную клетку за ним (MovementHandler, двойное нажатие стрелки).
    public static bool IsPeaceful(WorldState ws, LivingEntity entity) =>
        entity.Triggers?.Spotted == null
        && !(ws.Combat?.Active == true && ws.Combat.Initiative?.Any(e => e.Deleted != true && e.Symbol == entity.Symbol) == true);

    public static int? PeacefulEntityIndex(WorldState ws, int col, int row)
    {
        var entities = ws.Map.Entities ?? [];
        for (int i = 0; i < entities.Count; i++)
            if (entities[i].Deleted != true && entities[i].Position is { Count: >= 2 } p && p[0] == col && p[1] == row)
                return IsPeaceful(ws, entities[i]) ? i : null;
        return null;
    }

    public static bool CanMoveTo(WorldState ws, int fromCol, int fromRow, int toCol, int toRow, int entityId)
    {
        if (toCol < 1 || toCol > ws.Map.Cols || toRow < 1 || toRow > ws.Map.Rows) return false;
        if (ws.Map.TerrainAt(toCol, toRow) is { BlocksMove: true }) return false;
        if (ws.Map.FloorAt(fromCol, fromRow) != ws.Map.FloorAt(toCol, toRow)) return false;

        var fromRoom = GetRoom(ws, fromCol, fromRow);
        var toRoom   = GetRoom(ws, toCol, toRow);
        bool crossingBoundary = fromRoom != toRoom && (fromRoom != null || toRoom != null);
        if (crossingBoundary && !HasPassableConnection(ws, fromCol, fromRow, toCol, toRow)) return false;

        var entities = ws.Map.Entities ?? [];
        for (int i = 0; i < entities.Count; i++)
        {
            if (i == entityId) continue;
            var e = entities[i];
            if (e.Deleted == true) continue;
            if (e.Position is { Count: >= 2 } && e.Position[0] == toCol && e.Position[1] == toRow) return false;
        }

        if (ws.Hero?.Position is { Count: >= 2 } hp && hp[0] == toCol && hp[1] == toRow) return false;

        return true;
    }

    // Индекс клеток (MapConfig.RoomAt) — Дейкстра спрашивает это для каждой клетки локации.
    public static Room? GetRoom(WorldState ws, int col, int row) => ws.Map.RoomAt(col, row);

    public static bool HasPassableConnection(WorldState ws, int fromCol, int fromRow, int toCol, int toRow)
    {
        foreach (var door in ws.Map.DoorsBetween(fromCol, fromRow, toCol, toRow))
        {
            if (door.Hidden == true) continue;
            bool isPassage = !(door.IsDoor ?? (door.Color?.Count == 3));
            if (!isPassage && !(door.IsDoorOpen ?? false)) continue;
            return true;
        }
        return false;
    }

    public static int GetStepCost(WorldState ws, int col, int row, bool isDiagonal = false, bool diagUsed = false)
    {
        int baseCost = ws.Map.TerrainAt(col, row)?.StepCostFt ?? 5;
        foreach (var area in ws.Map.AreasAt(col, row))
        {
            if (area.Deleted == true || area.StepCostFt == null) continue;
            baseCost = area.StepCostFt.Value;
            break;
        }
        if (isDiagonal && diagUsed)
            baseCost += 5;
        return baseCost;
    }

    // Дальность обзора при свете дня на клетке под открытым небом (как MapObjectsProvider.OutdoorVisionFt у героя):
    // день 120 фт, утро/вечер 60; ночь или под крышей — null.
    public static int? DaylightFt(WorldState ws, int col, int row) =>
        ws.Map.TerrainAt(col, row) is { Indoor: false }
            ? ws.Time.PartOfDay switch { PartsOfDay.Night => null, PartsOfDay.Morning or PartsOfDay.Evening => 60, _ => 120 }
            : null;

    public static int ChebyshevDist(int c1, int r1, int c2, int r2)
        => Math.Max(Math.Abs(c1 - c2), Math.Abs(r1 - r2));

    // VisionFt: null = 30 фт, 0 = слепое, <0 = без ограничения.
    // Герой в неосвещённой клетке виден только в пределах DarkvisionFt существа (если оно задано).
    public static bool EntitySeesHero(WorldState ws, LivingEntity entity)
    {
        if (entity.Position is not { Count: >= 2 } ep) return false;
        if (ws.Hero?.Position is not { Count: >= 2 } hp) return false;

        // Без своего зрения: днём под открытым небом видно так же далеко, как герою (свет дня), иначе 30 фт.
        int visionFt = entity.VisionFt ?? DaylightFt(ws, hp[0], hp[1]) ?? 30;
        if (visionFt == 0) return false;
        // Дистанция — как у обзора героя (5-10-5, TargetGeometry): иначе враг «видел» героя, которого герой не видит
        // на той же дистанции (по Чебышёву 12 клеток наискосок — 60 фт, по 5-10-5 — 70).
        int distFt = TargetGeometry.DistanceFt(ep[0], ep[1], hp[0], hp[1]);
        if ((visionFt > 0 && distFt > visionFt) || !HasLineOfSight(ws, ep[0], ep[1], hp[0], hp[1])) return false;

        if (entity.DarkvisionFt is int darkvision && !IsCellLit(ws, hp[0], hp[1]))
            return distFt <= darkvision;
        return true;
    }

    // Клетка освещена: свет самого героя (если стоит на ней) или любой включённый источник в радиусе
    // с прямой видимостью до клетки. Light.Ft <= 0 — без ограничения радиуса.
    public static bool IsCellLit(WorldState ws, int col, int row)
    {
        // Открытая местность не ночью — дневной свет.
        if (ws.Time.PartOfDay != PartsOfDay.Night && ws.Map.TerrainAt(col, row) is { Indoor: false }) return true;

        if (ws.Hero is { Light.On: true, Position: { Count: >= 2 } heroPos }
            && heroPos[0] == col && heroPos[1] == row)
            return true;

        IEnumerable<CellEntity> sources = (ws.Map.Entities ?? []).Cast<CellEntity>().Concat(ws.Map.Objects ?? []);
        foreach (var src in sources)
        {
            if (src.Deleted == true || src.Light is not { On: true } light) continue;
            if (src.Position is not { Count: >= 2 } sp) continue;
            if (light.Ft > 0 && ChebyshevDist(sp[0], sp[1], col, row) * 5 > light.Ft) continue;
            if (HasLineOfSight(ws, sp[0], sp[1], col, row)) return true;
        }
        return false;
    }

    // DDA raycast: same algorithm as MapObjectsProvider.HasLineOfSightPure but uses WorldState directly.
    public static bool HasLineOfSight(WorldState ws, int fromX, int fromY, int toX, int toY)
    {
        double dx = toX - fromX;
        double dy = toY - fromY;
        if (dx == 0 && dy == 0) return true;
        if (ws.Map.FloorAt(fromX, fromY) != ws.Map.FloorAt(toX, toY)) return false; // другой этаж

        int stepX = dx > 0 ? 1 : dx < 0 ? -1 : 0;
        int stepY = dy > 0 ? 1 : dy < 0 ? -1 : 0;

        int cx = fromX, cy = fromY;
        double tMaxX  = dx == 0 ? double.MaxValue : (cx + 0.5 * stepX - fromX) / dx;
        double tMaxY  = dy == 0 ? double.MaxValue : (cy + 0.5 * stepY - fromY) / dy;
        double tDeltaX = dx == 0 ? double.MaxValue : Math.Abs(1.0 / dx);
        double tDeltaY = dy == 0 ? double.MaxValue : Math.Abs(1.0 / dy);

        while (cx != toX || cy != toY)
        {
            if (Math.Abs(tMaxX - tMaxY) < 1e-9)
            {
                bool wallX  = IsLosWall(ws, cx + 0.5 * stepX, cy,          horizontal: false);
                bool wallY  = IsLosWall(ws, cx,                cy + 0.5 * stepY, horizontal: true);
                if (wallX && wallY) return false;
                bool wallXY = IsLosWall(ws, cx + stepX,        cy + 0.5 * stepY, horizontal: true);
                bool wallYX = IsLosWall(ws, cx + 0.5 * stepX,  cy + stepY,       horizontal: false);
                if (!(!wallX && !wallXY) && !(!wallY && !wallYX)) return false;
                if (cx != toX) cx += stepX;
                if (cy != toY) cy += stepY;
                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
            }
            else if (tMaxX < tMaxY)
            {
                if (IsLosWall(ws, cx + 0.5 * stepX, cy, horizontal: false)) return false;
                cx += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                if (IsLosWall(ws, cx, cy + 0.5 * stepY, horizontal: true)) return false;
                cy += stepY;
                tMaxY += tDeltaY;
            }
            // Рельеф, закрывающий обзор (чаща, скалы): саму клетку видно, что за ней — нет.
            if ((cx != toX || cy != toY) && ws.Map.TerrainAt(cx, cy) is { BlocksSight: true }) return false;
        }
        return true;
    }

    private static bool IsLosWall(WorldState ws, double x, double y, bool horizontal)
    {
        int x1, y1, x2, y2;
        if (horizontal) { x1 = x2 = (int)x; y1 = (int)(y - 0.5); y2 = (int)(y + 0.5); }
        else            { x1 = (int)(x - 0.5); x2 = (int)(x + 0.5); y1 = y2 = (int)y; }

        if (HasLosPassage(ws, x1, y1, x2, y2)) return false;

        var room1 = GetRoom(ws, x1, y1);
        var room2 = GetRoom(ws, x2, y2);
        if (room1 == room2) return false;          // same room or both outside rooms
        if (room1 == null || room2 == null) return true; // one side outside, other in a room
        return true;                                // different rooms
    }

    private static bool HasLosPassage(WorldState ws, int x1, int y1, int x2, int y2)
    {
        foreach (var door in ws.Map.DoorsBetween(x1, y1, x2, y2))
        {
            if (door.Hidden == true) continue;
            bool isDoor = door.IsDoor ?? (door.Color?.Count == 3);
            return !isDoor || (door.IsDoorOpen ?? false) || door.IsWindow == true;
        }
        return false;
    }
}
