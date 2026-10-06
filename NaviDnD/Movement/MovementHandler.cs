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
            // Подсказка один раз, а не на каждое нажатие стрелки.
            const string noSpeed = "Скорость на этот ход потрачена — действуй или [F10] конец хода.";
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

        // Шаг наружу через выход на карту мира (вне боя) — покинуть локацию (MapScreenLoop → GameWorld.LeaveLocation).
        if (_world.Combat?.Active != true && (_world.Map.Doors ?? []).Any(d => d.IsWorldExit == true && d.From is { Count: >= 2 } f && d.To is { Count: >= 2 } t
                && ((f[0] == heroCol && f[1] == heroRow && t[0] == newCol && t[1] == newRow) || (t[0] == heroCol && t[1] == heroRow && f[0] == newCol && f[1] == newRow))))
        {
            LeaveRequested = true;
            return false;
        }

        var blockReason = GetBlockReason(heroCol, heroRow, newCol, newRow);

        // Мирное существо на пути: второе быстрое нажатие в ту же сторону — протиснуться мимо него на клетку
        // за ним (если она свободна), за два шага.
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
                activatedTriggers.AddRange(GetOpportunityAttackTriggers(heroCol, heroRow, behindCol, behindRow));
                _storage.ApplyClientMovement(GetStepCost(newCol, newRow, diag), diag);
                hero.Position = [behindCol, behindRow];
                _storage.ApplyClientMovement(GetStepCost(behindCol, behindRow, diag), diag);
                _world.History?.Add(new DialogMessage { Text = $"Ты протискиваешься мимо: {bystander.Name}." });
                _storage.Save();
                activatedTriggers.AddRange(GetStepTriggers(behindCol, behindRow));
                activatedTriggers.AddRange(GetVisibleTriggers(behindCol, behindRow));
                activatedTriggers.AddRange(GetEncounterTriggers());
                activatedAreaTriggers.AddRange(GetExploredTriggers());
                activatedDoorTriggers.AddRange(GetDoorVisibleTriggers(behindCol, behindRow));
                return activatedTriggers.Count > 0 || activatedAreaTriggers.Count > 0 || activatedDoorTriggers.Count > 0;
            }
            _passAttempt = (dirNow, DateTime.UtcNow, heroCol, heroRow);
            blockReason = $"Путь преграждён: {bystander.Name}. Нажми ещё раз — протиснуться мимо.";
        }

        if (blockReason != null)
        {
            // Упёрся в то же препятствие ещё раз — без новой строки в диалоге (раньше каждое нажатие — ещё одна строка).
            if (_world.History?.LastOrDefault()?.Text != blockReason)
            {
                _world.History?.Add(new DialogMessage { Text = blockReason });
                _storage.Save();
            }
            return false;
        }

        bool isDiagonal = dir.Value.dcol != 0 && dir.Value.drow != 0;
        activatedTriggers.AddRange(GetOpportunityAttackTriggers(heroCol, heroRow, newCol, newRow));
        hero.Position = [newCol, newRow];
        int stepCost = GetStepCost(newCol, newRow, isDiagonal);
        _storage.ApplyClientMovement(stepCost, isDiagonal);
        // Шаг на лестницу — сразу на связанную клетку соседнего этажа (стоимость — обычный шаг).
        if (_world.Map.StairTarget(newCol, newRow) is var (stairCol, stairRow))
        {
            int fromZ = _world.Map.FloorAt(newCol, newRow), toZ = _world.Map.FloorAt(stairCol, stairRow);
            (newCol, newRow) = (stairCol, stairRow);
            hero.Position = [newCol, newRow];
            string verb = toZ > fromZ ? "поднимаешься" : "спускаешься";
            _world.History?.Add(new DialogMessage { Text = $"Ты {verb} по лестнице: {MapConfig.FloorName(toZ)}." });
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

    // Герой шагнул в выход на карту мира — цикл карты уводит его из локации и сбрасывает флаг.
    public bool LeaveRequested { get; set; }

    // Последнее упирание в мирное существо: направление, время, откуда.
    private ((int dcol, int drow) dir, DateTime at, int col, int row)? _passAttempt;
    private const int PassWindowMs = 800;

    // Существо на клетке, если оно не враждебно (MovementCalculator.IsPeaceful).
    private LivingEntity? PeacefulEntityAt(int col, int row) =>
        MovementCalculator.PeacefulEntityIndex(_world, col, row) is int i ? _world.Map.Entities![i] : null;

    private IEnumerable<(CellEntity, string)> GetOpportunityAttackTriggers(int oldCol, int oldRow, int newCol, int newRow)
    {
        if (_world.Combat?.Active != true) yield break;
        int round = _world.Time.TotalRounds;
        if (_world.Hero?.NoOpportunityAttacksRound >= round) yield break;

        // Только враги-участники боя: дружественный/нейтральный NPC и союзник в бою не бьют герою в спину.
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

    // spotted — только у существ с объявленным triggers.spotted (враждебные), и только на фронте
    // «не видело героя → видит». Нейтральные NPC без триггера не дёргают ИИ вовсе; враг, которого
    // герой не стал атаковать, не повторяет триггер на каждом шаге, пока не потеряет и снова не найдёт героя.
    // Видимость обновляется и у тех, кто уже в инициативе — чтобы после конца боя ещё видимый враг
    // не начал новый бой сразу же. Скрытые (hidden) не пропускаются — это засада, ИИ разрешает её сам.
    // Во время боя существо замечает и сам бой: союзник-участник рядом (≤ AllyAlertFt, прямая видимость) —
    // значит, оно тоже вступает, даже если героя пока не видит.
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

    // Проверка встречи вне шага героя: ответ ИИ мог открыть дверь/убрать преграду, а враги в бою —
    // подойти к ещё не вступившему союзнику. Без неё увидевшие героя враги молчали до его следующего шага.
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
            return "Путь преграждён.";

        // Этажи лежат на поле в разных местах: шагнуть с одного на другой нельзя (только лестницей).
        if (_world.Map.FloorAt(fromCol, fromRow) != _world.Map.FloorAt(toCol, toRow))
            return "Путь преграждён.";

        // Рельеф открытой местности: чаща, скалы, глубокая вода — непроходимы.
        var terrain = _world.Map.TerrainAt(toCol, toRow);
        if (terrain is { BlocksMove: true })
            return $"Путь преграждён: {terrain.Name.ToLowerInvariant()}.";

        // Клетка вне комнат, зон и рельефа — пустота (поле локации 100×100 сгенерировано не целиком): туда нельзя.
        // Выход из локации (дверь-вход на краю карты) ведёт именно туда — переход в мир пока не сделан.
        if ((_world.Map.Rooms is { Count: > 0 } || _world.Map.Chunks is { Count: > 0 })
            && terrain == null && GetRoom(toCol, toRow) == null && !IsAreaCell(toCol, toRow))
            return IsLocationExit(fromCol, fromRow, toCol, toRow)
                ? "Здесь выход на карту мира — путь наружу пока недоступен."
                : "Путь преграждён.";

        // Target cell blocked by a living entity
        foreach (var entity in _world.Map.Entities ?? [])
            if (entity != ignore && entity.Deleted != true && entity.Position?.Count >= 2
                && entity.Position[0] == toCol && entity.Position[1] == toRow)
                return "Путь преграждён.";

        if (fromCol != toCol && fromRow != toRow)
        {
            // Diagonal: allow if at least one intermediate cardinal step is terrain-passable.
            // Entities on intermediate cells don't block — only walls do.
            bool path1 = IsTerrainCrossable(fromCol, fromRow, toCol,   fromRow)  // hero → E/W
                      && IsTerrainCrossable(toCol,   fromRow, toCol,   toRow);   // E/W → target
            bool path2 = IsTerrainCrossable(fromCol, fromRow, fromCol, toRow)    // hero → N/S
                      && IsTerrainCrossable(fromCol, toRow,   toCol,   toRow);   // N/S → target
            if (!path1 && !path2)
                return "Путь преграждён.";
        }
        else
        {
            // Cardinal: direct room boundary + door check.
            var fromRoom = GetRoom(fromCol, fromRow);
            var toRoom   = GetRoom(toCol,   toRow);
            bool crossingRoomBoundary = fromRoom != toRoom && (fromRoom != null || toRoom != null);
            if (crossingRoomBoundary && !HasDoor(fromCol, fromRow, toCol, toRow))
                return "Путь преграждён.";
        }

        return null;
    }

    // Checks terrain passability between two adjacent cells, ignoring entities.
    private bool IsTerrainCrossable(int fromCol, int fromRow, int toCol, int toRow)
    {
        if (toCol < 1 || toCol > _world.Map.Cols || toRow < 1 || toRow > _world.Map.Rows)
            return false;
        // Диагональ между двумя деревьями/скалами не протиснуться.
        if (_world.Map.TerrainAt(toCol, toRow) is { BlocksMove: true })
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

    private int GetStepCost(int col, int row, bool isDiagonal = false)
    {
        int baseCost = _world.Map.TerrainAt(col, row)?.StepCostFt ?? 5;
        foreach (var area in _world.Map.AreasAt(col, row))
        {
            if (area.Deleted == true || area.StepCostFt == null) continue;
            baseCost = area.StepCostFt.Value;
            break;
        }
        // First diagonal per turn is free; each subsequent costs +5ft
        if (isDiagonal && _storage.DiagonalUsed >= 1)
            baseCost += 5;
        return baseCost;
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
