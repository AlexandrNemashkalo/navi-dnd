using NaviDnD.Data;
using NaviDnD.Data.Models;

namespace NaviDnD;

public class MovementHandler
{
    private readonly WorldState _world;
    private readonly Storage _storage;
    private readonly DisplayConfig _display;

    public MovementHandler(WorldState world, Storage storage, DisplayConfig display)
    {
        _world = world;
        _storage = storage;
        _display = display;
    }

    // Returns: null  = not a movement command → call AI (SendAction)
    //          true  = moved onto entity/object/area trigger → call AI (FireTrigger)
    //          false = moved normally or blocked → no AI needed
    public bool? TryMove(string input, out string aiInput, out List<(CellEntity entity, string triggerType)> activatedTriggers, out List<Area> activatedAreaTriggers, out List<(Door door, string triggerType)> activatedDoorTriggers)
    {
        aiInput = input;
        activatedTriggers = [];
        activatedAreaTriggers = [];
        activatedDoorTriggers = [];

        var dir = ParseDirection(input.Trim());
        if (dir == null) return null;

        var hero = _world.Hero;
        if (hero?.Position == null || hero.Position.Count < 2) return false;

        if (_world.Combat?.Active == true && (hero.SpeedLeft ?? 0) <= 0)
        {
            // Hint appears once, rather than every time the arrow is pressed.
            string noSpeed = L.T("No movement left this turn — act or press [F10] to end the turn.");
            if (_world.History?.LastOrDefault()?.Text != noSpeed)
            {
                _world.History?.Add(new DialogMessage { Text = noSpeed });
                _storage.Save();
            }
            return false;
        }

        int heroCol = hero.Position[0];
        int heroRow = hero.Position[1];
        int newCol  = heroCol + dir.Value.dcol;
        int newRow  = heroRow + dir.Value.drow;

        // Step outside through the exit to the world map (outside of combat) - leave the location (MapScreenLoop → GameWorld.LeaveLocation).
        if (_world.Combat?.Active != true && (_world.Map.Doors ?? []).Any(d => d.IsWorldExit == true && d.From is { Count: >= 2 } f && d.To is { Count: >= 2 } t
                && ((f[0] == heroCol && f[1] == heroRow && t[0] == newCol && t[1] == newRow) || (t[0] == heroCol && t[1] == heroRow && f[0] == newCol && f[1] == newRow))))
        {
            LeaveRequested = true;
            return false;
        }

        var blockReason = GetBlockReason(heroCol, heroRow, newCol, newRow);

        // A peaceful creature is on the way: a second quick press in the same direction - squeeze past it onto
        // the square behind it (if it is free), in two steps.
        var bystander = blockReason != null ? PeacefulEntityAt(newCol, newRow) : null;
        if (bystander != null)
        {
            var dirNow = dir.Value;
            bool repeat = _passAttempt is { } pa && pa.dir == dirNow && (DateTime.UtcNow - pa.at).TotalMilliseconds <= PassWindowMs
                && pa.col == heroCol && pa.row == heroRow;
            int behindCol = newCol + dirNow.dcol, behindRow = newRow + dirNow.drow;
            if (repeat && GetBlockReason(heroCol, heroRow, newCol, newRow, ignore: bystander) == null
                && GetBlockReason(newCol, newRow, behindCol, behindRow) == null)
            {
                _passAttempt = null;
                bool diag = dirNow.dcol != 0 && dirNow.drow != 0;
                int passCost = GetStepCost(newCol, newRow, diag)
                    + MovementCalculator.GetStepCost(_world, behindCol, behindRow, diag, _storage.DiagonalUsed > 0 || diag);
                if (!HasMovementBudget(passCost)) return false;
                activatedTriggers.AddRange(GetOpportunityAttackTriggers(heroCol, heroRow, behindCol, behindRow));
                _storage.ApplyClientMovement(GetStepCost(newCol, newRow, diag), diag, MovementCalculator.GetStepCost(_world, newCol, newRow, diag));
                hero.Position = [behindCol, behindRow];
                _storage.ApplyClientMovement(GetStepCost(behindCol, behindRow, diag), diag, MovementCalculator.GetStepCost(_world, behindCol, behindRow, diag));
                _world.History?.Add(new DialogMessage { Text = L.WF("You squeeze past: {0}.", bystander.Name) });
                _storage.Save();
                activatedTriggers.AddRange(GetStepTriggers(behindCol, behindRow));
                activatedTriggers.AddRange(GetVisibleTriggers(behindCol, behindRow));
                activatedTriggers.AddRange(GetEncounterTriggers());
                activatedAreaTriggers.AddRange(GetExploredTriggers());
                activatedDoorTriggers.AddRange(GetDoorVisibleTriggers(behindCol, behindRow));
                return activatedTriggers.Count > 0 || activatedAreaTriggers.Count > 0 || activatedDoorTriggers.Count > 0;
            }
            _passAttempt = (dirNow, DateTime.UtcNow, heroCol, heroRow);
            blockReason = L.F("The way is blocked: {0}. Press again to squeeze past.", bystander.Name);
        }

        if (blockReason != null)
        {
            // I ran into the same obstacle again - without a new line in the dialogue (previously, each click was another line).
            if (_world.History?.LastOrDefault()?.Text != blockReason)
            {
                _world.History?.Add(new DialogMessage { Text = blockReason });
                _storage.Save();
            }
            return false;
        }

        bool isDiagonal = dir.Value.dcol != 0 && dir.Value.drow != 0;
        int stepCost = GetStepCost(newCol, newRow, isDiagonal);
        if (!HasMovementBudget(stepCost)) return false;
        activatedTriggers.AddRange(GetOpportunityAttackTriggers(heroCol, heroRow, newCol, newRow));
        hero.Position = [newCol, newRow];
        _storage.ApplyClientMovement(stepCost, isDiagonal, MovementCalculator.GetStepCost(_world, newCol, newRow, isDiagonal));
        // Step onto the stairs - immediately onto the connected cell of the adjacent floor (cost - normal step).
        if (_world.Map.StairTarget(newCol, newRow) is var (stairCol, stairRow))
        {
            int fromZ = _world.Map.FloorAt(newCol, newRow), toZ = _world.Map.FloorAt(stairCol, stairRow);
            (newCol, newRow) = (stairCol, stairRow);
            hero.Position = [newCol, newRow];
            string floor = MapConfig.FloorName(toZ);
            _world.History?.Add(new DialogMessage
            {
                Text = toZ > fromZ ? L.WF("You climb the stairs: {0}.", floor) : L.WF("You go down the stairs: {0}.", floor),
            });
        }
        _storage.Save();

        activatedTriggers.AddRange(GetStepTriggers(newCol, newRow));
        activatedTriggers.AddRange(GetVisibleTriggers(newCol, newRow));
        activatedTriggers.AddRange(GetEncounterTriggers());
        activatedAreaTriggers.AddRange(GetExploredTriggers());
        activatedDoorTriggers.AddRange(GetDoorStepTriggers(heroCol, heroRow, newCol, newRow));
        activatedDoorTriggers.AddRange(GetDoorVisibleTriggers(newCol, newRow));

        return activatedTriggers.Count > 0 || activatedAreaTriggers.Count > 0 || activatedDoorTriggers.Count > 0;
    }

    // The hero stepped into the exit to the world map - the map cycle takes him away from the location and resets the flag.
    public bool LeaveRequested { get; set; }

    // The last rest against a peaceful creature: direction, time, from.
    private ((int dcol, int drow) dir, DateTime at, int col, int row)? _passAttempt;
    private const int PassWindowMs = 800;

    // Creature on the square, if it is not hostile (MovementCalculator.IsPeaceful).
    private LivingEntity? PeacefulEntityAt(int col, int row) =>
        MovementCalculator.PeacefulEntityIndex(_world, col, row) is int i ? _world.Map.Entities![i] : null;

    private IEnumerable<(CellEntity, string)> GetOpportunityAttackTriggers(int oldCol, int oldRow, int newCol, int newRow)
    {
        if (_world.Combat?.Active != true) yield break;
        int round = _world.Time.TotalRounds;
        if (_world.Hero?.NoOpportunityAttacksRound >= round) yield break;

        // Only enemies participating in the battle: friendly/neutral NPC and an ally in battle do not hit the hero in the back.
        var combatants = CombatantSymbols(enemiesOnly: true);

        foreach (var entity in _world.Map.Entities ?? [])
        {
            if (entity.Deleted == true || entity.Position?.Count < 2) continue;
            if (combatants?.Contains(entity.Symbol) != true) continue;

            int entCol = entity.Position[0];
            int entRow = entity.Position[1];
            int distBefore = Math.Max(Math.Abs(entCol - oldCol), Math.Abs(entRow - oldRow));
            int distAfter  = Math.Max(Math.Abs(entCol - newCol), Math.Abs(entRow - newRow));

            if (distBefore <= 1 && distAfter > 1 && _storage.TryMarkOpportunityAttackOnHero(entity.Symbol, round))
                yield return (entity, "opportunity_attack_on_hero");
        }
    }

    private IEnumerable<(CellEntity, string)> GetStepTriggers(int col, int row)
    {
        var all = (_world.Map.Entities ?? []).Concat(_world.Map.Objects ?? []);
        foreach (var entity in all)
        {
            if (entity.Deleted == true) continue;
            if (entity.Triggers?.OnStep == null) continue;
            if (entity.Position?.Count >= 2 && entity.Position[0] == col && entity.Position[1] == row)
                yield return (entity, "onStep");
        }
    }

    private IEnumerable<(CellEntity, string)> GetVisibleTriggers(int heroCol, int heroRow)
    {
        int visionCells = (_world.Hero?.VisionFt ?? 0) / 5;
        if (visionCells <= 0) yield break;

        var all = (_world.Map.Entities ?? []).Concat(_world.Map.Objects ?? []);
        foreach (var entity in all)
        {
            if (entity.Deleted == true) continue;
            if (entity.Hidden != true) continue;
            if (entity.Triggers?.OnVisible == null) continue;
            if (entity.Position?.Count < 2) continue;

            double dist = Math.Sqrt(
                Math.Pow(entity.Position[0] - heroCol, 2) +
                Math.Pow(entity.Position[1] - heroRow, 2));
            if (dist <= visionCells)
                yield return (entity, "onVisible");
        }
    }

    // spotted - only for creatures with the declared triggers.spotted (hostile), and only at the front “did not
    // see the hero → sees.” Neutral NPC without a trigger does not pull the AI at all; the enemy that the
    // hero did not attack does not repeat the trigger at each step until he loses and finds the hero again.
    // Visibility is also updated for those who are already in the initiative - so that after the end of the
    // battle, the still visible enemy does not immediately start a new battle. Hidden ones (hidden) are not
    // missed - this is an ambush, the AI resolves it itself. During the battle, the creature also notices the
    // battle itself: an ally-participant is nearby (≤ AllyAlertFt, direct visibility) - which means that it
    // also enters, even if it does not see the hero yet.
    private IEnumerable<(CellEntity, string)> GetEncounterTriggers()
    {
        var inInitiative = CombatantSymbols();
        List<LivingEntity> fighting = inInitiative == null ? [] : (_world.Map.Entities ?? [])
            .Where(e => e.Deleted != true && e.Position is { Count: >= 2 } && inInitiative.Contains(e.Symbol))
            .ToList();

        foreach (var entity in _world.Map.Entities ?? [])
        {
            if (entity.Deleted == true) continue;
            if (entity.Triggers?.Spotted == null) continue;

            bool notices = MovementCalculator.EntitySeesHero(_world, entity) || SeesFightingAlly(entity, fighting);
            bool newlyNotices = _storage.UpdateEntitySeesHero(entity.Symbol, notices);
            if (newlyNotices && inInitiative?.Contains(entity.Symbol) != true)
                yield return (entity, "spotted");
        }
    }

    // Checking an encounter outside the hero's step: the AI's response could open a door/remove an obstacle,
    // and enemies in battle could approach an ally who had not yet joined. Without her, the enemies who saw the
    // hero remained silent until his next step.
    public List<(CellEntity entity, string triggerType)> CollectEncounterTriggers() => GetEncounterTriggers().ToList();

    private const int AllyAlertFt = 30;

    private bool SeesFightingAlly(LivingEntity entity, List<LivingEntity> fighting)
    {
        if (entity.Position is not { Count: >= 2 } p) return false;
        return fighting.Any(a => a != entity
            && TargetGeometry.DistanceFt(p[0], p[1], a.Position[0], a.Position[1]) <= AllyAlertFt
            && MovementCalculator.HasLineOfSight(_world, p[0], p[1], a.Position[0], a.Position[1]));
    }

    private HashSet<string>? CombatantSymbols(bool enemiesOnly = false) => _world.Combat?.Active == true
        ? _world.Combat.Initiative?.Where(e => e.Deleted != true && e.Symbol != _world.Hero?.Symbol && !(enemiesOnly && e.Ally == true))
            .Select(e => e.Symbol).ToHashSet()
        : null;

    // Returns null if walkable, or a human-readable block reason string.
    private string? GetBlockReason(int fromCol, int fromRow, int toCol, int toRow, LivingEntity? ignore = null)
    {
        if (toCol < 1 || toCol > _world.Map.Cols || toRow < 1 || toRow > _world.Map.Rows)
            return L.T("The way is blocked.");

        // The floors lie on the field in different places: you cannot step from one to another (only by stairs).
        if (_world.Map.FloorAt(fromCol, fromRow) != _world.Map.FloorAt(toCol, toRow))
            return L.T("The way is blocked.");

        // The terrain of the open area: thicket, rocks, deep water - impassable.
        var terrain = _world.Map.TerrainAt(toCol, toRow);
        if (terrain is { BlocksMove: true })
            return L.F("The way is blocked: {0}.", L.T(terrain.Name).ToLowerInvariant());

        // A cell outside rooms, zones and terrain is empty (the 100x100 location field is not entirely
        // generated): you cannot go there. The exit from the location (entrance door at the edge of the map)
        // leads exactly there - the transition to the world has not yet been made.
        if ((_world.Map.Rooms is { Count: > 0 } || _world.Map.Chunks is { Count: > 0 })
            && terrain == null && GetRoom(toCol, toRow) == null && !IsAreaCell(toCol, toRow))
            return IsLocationExit(fromCol, fromRow, toCol, toRow)
                ? L.T("This is the exit to the world map — the way out isn't available yet.")
                : L.T("The way is blocked.");

        // Target cell blocked by a living entity
        foreach (var entity in _world.Map.Entities ?? [])
            if (entity != ignore && entity.Deleted != true && entity.Position?.Count >= 2
                && entity.Position[0] == toCol && entity.Position[1] == toRow)
                return L.T("The way is blocked.");

        if (fromCol != toCol && fromRow != toRow)
        {
            // Diagonal: squeeze between terrain obstacles; room walls still require an open route.
            // Intermediate terrain and entities do not occupy the destination cell.
            bool path1 = IsBoundaryCrossable(fromCol, fromRow, toCol,   fromRow)  // hero → E/W
                      && IsBoundaryCrossable(toCol,   fromRow, toCol,   toRow);   // E/W → target
            bool path2 = IsBoundaryCrossable(fromCol, fromRow, fromCol, toRow)    // hero → N/S
                      && IsBoundaryCrossable(fromCol, toRow,   toCol,   toRow);   // N/S → target
            if (!path1 && !path2)
                return L.T("The way is blocked.");
        }
        else
        {
            // Cardinal: direct room boundary + door check.
            var fromRoom = GetRoom(fromCol, fromRow);
            var toRoom   = GetRoom(toCol,   toRow);
            bool crossingRoomBoundary = fromRoom != toRoom && (fromRoom != null || toRoom != null);
            if (crossingRoomBoundary && !HasDoor(fromCol, fromRow, toCol, toRow))
                return L.T("The way is blocked.");
        }

        return null;
    }

    // Checks room boundaries between adjacent cells, ignoring terrain and entities.
    private bool IsBoundaryCrossable(int fromCol, int fromRow, int toCol, int toRow)
    {
        if (toCol < 1 || toCol > _world.Map.Cols || toRow < 1 || toRow > _world.Map.Rows)
            return false;
        var fromRoom = GetRoom(fromCol, fromRow);
        var toRoom   = GetRoom(toCol,   toRow);
        bool crossingRoomBoundary = fromRoom != toRoom && (fromRoom != null || toRoom != null);
        return !crossingRoomBoundary || HasDoor(fromCol, fromRow, toCol, toRow);
    }

    private bool IsAreaCell(int col, int row) => _world.Map.AreasAt(col, row).Any(a => a.Deleted != true);

    private bool IsLocationExit(int fromCol, int fromRow, int toCol, int toRow) =>
        (_world.Map.Doors ?? []).Any(d => d.IsEntry == true && d.From is { Count: >= 2 } f && d.To is { Count: >= 2 } t
            && ((f[0] == fromCol && f[1] == fromRow && t[0] == toCol && t[1] == toRow)
             || (t[0] == fromCol && t[1] == fromRow && f[0] == toCol && f[1] == toRow)));

    private Room? GetRoom(int col, int row) => _world.Map.RoomAt(col, row);

    // Can pass through if: not hidden AND (it's a passage OR door is open)
    private bool HasDoor(int fromCol, int fromRow, int toCol, int toRow) =>
        _world.Map.DoorsBetween(fromCol, fromRow, toCol, toRow).Any(x =>
            x.Hidden != true && ((x.IsDoorOpen ?? false) || !(x.IsDoor ?? (x.Color?.Count == 3))));

    private IEnumerable<(Door, string)> GetDoorStepTriggers(int fromCol, int fromRow, int toCol, int toRow)
    {
        foreach (var door in _world.Map.Doors ?? [])
        {
            if (door.Triggers?.OnStep == null) continue;
            bool fwd = door.From?.Count >= 2 && door.To?.Count >= 2
                    && door.From[0] == fromCol && door.From[1] == fromRow
                    && door.To[0]   == toCol   && door.To[1]   == toRow;
            bool rev = door.From?.Count >= 2 && door.To?.Count >= 2
                    && door.From[0] == toCol   && door.From[1] == toRow
                    && door.To[0]   == fromCol && door.To[1]   == fromRow;
            if (fwd || rev)
                yield return (door, "onStep");
        }
    }

    private IEnumerable<(Door, string)> GetDoorVisibleTriggers(int heroCol, int heroRow)
    {
        int visionCells = (_world.Hero?.VisionFt ?? 0) / 5;
        if (visionCells <= 0) yield break;

        foreach (var door in _world.Map.Doors ?? [])
        {
            if (door.Hidden != true) continue;
            if (door.Triggers?.OnVisible == null) continue;
            if (door.From?.Count < 2 || door.To?.Count < 2) continue;

            double distFrom = Math.Sqrt(Math.Pow(door.From[0] - heroCol, 2) + Math.Pow(door.From[1] - heroRow, 2));
            double distTo   = Math.Sqrt(Math.Pow(door.To[0]   - heroCol, 2) + Math.Pow(door.To[1]   - heroRow, 2));
            if (distFrom <= visionCells || distTo <= visionCells)
                yield return (door, "onVisible");
        }
    }

    private int GetStepCost(int col, int row, bool isDiagonal = false) =>
        MovementCalculator.GetStepCost(_world, col, row, isDiagonal, _storage.DiagonalUsed > 0);

    private bool HasMovementBudget(int cost)
    {
        if (_world.Combat?.Active != true || (_world.Hero?.SpeedLeft ?? 0) >= cost) return true;
        string message = L.T("Not enough movement for this step — [F10] end turn.");
        if (_world.History?.LastOrDefault()?.Text != message)
        {
            _world.History?.Add(new DialogMessage { Text = message });
            _storage.Save();
        }
        return false;
    }

    private IEnumerable<Area> GetExploredTriggers()
    {
        var all = (_world.Map.Rooms?.Cast<Area>() ?? []).Concat(_world.Map.Area ?? []);
        foreach (var area in all)
        {
            if (area.Triggers?.OnExplored == null) continue;
            if (area.Positions == null) continue;
            var countExp = area.Positions.Count(p => _storage.ExploredCells.Contains((p[0], p[1])));
            if (countExp > area.Positions.Count / 2)
                yield return area;
        }
    }

    // Returns (direction, humanReadableLabel). Direction is null if not a movement command.
    private static (int dcol, int drow)? ParseDirection(string input)
    {
        // Arrow key tokens (from InputBox when ArrowKeysMovement = true)
        // Map renders rows top-to-bottom from rows→1, so row 1 is at the bottom of the screen.
        // ↑ key = visual up = larger row number = drow +1 (north)
        // ↓ key = visual down = smaller row number = drow -1 (south)
        if (input is "MoveNorth")     return ( 0, +1);
        if (input is "MoveSouth")     return ( 0, -1);
        if (input is "MoveWest")      return (-1,  0);
        if (input is "MoveEast")      return (+1,  0);
        if (input is "MoveNorthEast") return (+1, +1);
        if (input is "MoveNorthWest") return (-1, +1);
        if (input is "MoveSouthEast") return (+1, -1);
        if (input is "MoveSouthWest") return (-1, -1);

        return null;
    }
}
