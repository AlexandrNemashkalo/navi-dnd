using NaviDnD.Data.Models;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace NaviDnD.Clients;

/// <summary>
/// Нарезает WorldState на кэш-блоки для Claude API.
/// Каждый блок кэшируется независимо — блоки выше по списку инвалидируют блоки ниже.
/// </summary>
public class AiContextBuilder
{
    private readonly WorldState _ws;
    private readonly HashSet<(int, int)>? _visibleCells;

    private const int MaxHistoryForAi = 5;

    private static readonly JsonSerializerOptions _json = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AiContextBuilder(WorldState worldState, HashSet<(int, int)>? visibleCells = null)
    {
        _ws = worldState;
        _visibleCells = visibleCells;
    }

    // ─── System blocks (кэшируются) ────────────────────────────────────────
    public string Instructions(string promptText) => promptText;

    // ─── User message (не кэшируется) ──────────────────────────────────────

    /// <summary>
    /// Шаг 1: минимальный контекст (~400-500 токенов).
    /// Только hp/state/position + краткий нарратив + индекс имён + последние события.
    /// </summary>
    public string MinimalState(string playerAction)
    {
        var sb = new System.Text.StringBuilder();

        // ── Герой ──────────────────────────────────────────────────────────
        sb.AppendLine($"round:{_ws.Time.TotalRounds} day:{_ws.Time.Day} timeOfDay:{_ws.Time.PartOfDay}");

        if (_ws.Hero != null)
        {
            var pos = _ws.Hero.Position is { } p ? $"[{string.Join(",", p)}]" : "?";
            var speedStr = _ws.Hero.SpeedMax.HasValue
                ? $" speed:{_ws.Hero.SpeedLeft ?? _ws.Hero.SpeedMax}/{_ws.Hero.SpeedMax}"
                : "";
            var acStat = _ws.Hero.Stats?.FirstOrDefault(s =>
                s.Name?.Contains("Класс Доспеха", StringComparison.OrdinalIgnoreCase) == true);
            var acStr = acStat?.Value != null ? $" ac:{acStat.Value}" : "";
            var inspStr = _ws.Hero.Inspiration == true ? " inspiration:да" : "";
            var stealthStr = _ws.Hero.Stealth is int st ? $" stealth:{st}" : "";
            var noOaStr = (_ws.Hero.NoOpportunityAttacksRound >= _ws.Time.TotalRounds ? " отход:да" : "")
                          + (_ws.Hero.Dead == true ? " dead:да" : "");
            var lightStr = _ws.Hero.Light is { On: true } l ? $" light:{l.Ft}фт" : "";
            // Кто герой — имя, раса, класс и заметка о нём: обращаться к нему в верном роде и характере.
            string Stat(string n) => _ws.Hero.Stats?.FirstOrDefault(s => s.Name == n)?.Value ?? "";
            string who = string.Join(", ", new[] { Stat("Раса"), Stat("Класс") }.Where(x => x.Length > 0));
            sb.AppendLine($"hero: {_ws.Hero.Name}{(who.Length > 0 ? $" ({who})" : "")}"
                          + (_ws.Hero.AiInfo is { Count: > 0 } note ? $" — {string.Join(" ", note)}" : ""));
            sb.AppendLine($"HP:{_ws.Hero.Hp} symbol:{_ws.Hero.Symbol} position:{pos}{speedStr}{acStr}{inspStr}{stealthStr}{noOaStr}{lightStr}");
            if (HeroWhereabouts() is { } where) sb.AppendLine(where);
            if (WorldLine() is { } worldLine) sb.AppendLine(worldLine);
            if (_ws.Narrative?.Style is { Length: > 0 } style) sb.AppendLine($"style: {style}");

            if (_ws.Hero.Actions is { Count: > 0 } acts)
            {
                var actParts = acts
                    .Select((a, i) => a.Deleted != true ? $"{a.Name}(id:{i}) {a.Value}/{a.MaxValue}" : null)
                    .Where(s => s != null);
                if (actParts.Any())
                    sb.AppendLine($"actions: {string.Join(", ", actParts)}");
            }

            var equipped = _ws.Hero.Inventory?
                .Where(i => i.Deleted != true && i.EquipmentedSlot?.Count > 0)
                .Select(i => $"{string.Join("+", i.EquipmentedSlot!)}: {i.Name}");
            if (equipped?.Any() == true)
                sb.AppendLine($"equipped: {string.Join(", ", equipped)}");
        }

        // ── Нарратив ───────────────────────────────────────────────────────
        if (_ws.Narrative != null)
        {
            if (!string.IsNullOrEmpty(_ws.Narrative.CurrentArc))
                sb.AppendLine($"arc: {_ws.Narrative.CurrentArc}");
            if (!string.IsNullOrEmpty(_ws.Narrative.World))
                sb.AppendLine($"world: {_ws.Narrative.World}");
        }

        // ── Индекс имён (для поиска по name через MCP) ─────────────────────
        if (_ws.Hero?.Inventory is { Count: > 0 } inv)
            sb.AppendLine($"hero.inventory: {Join(inv.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.Hero?.Abilities is { Count: > 0 } abl)
            sb.AppendLine($"hero.abilities: {Join(abl.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.Hero?.Spells is { Count: > 0 } spl)
            sb.AppendLine($"hero.spells: {Join(spl.Where(x => x.Deleted != true).Select(x => $"{x.Name} (ур.{x.Level})"))}");
        if (_ws.Hero?.Resources is { Count: > 0 } res)
            sb.AppendLine($"hero.resources: {Join(res.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.Hero?.Effects is { Count: > 0 } eff)
            sb.AppendLine($"hero.effects: {Join(eff.Where(x => x.Deleted != true).Select(x =>
                x.Description is { Length: > 0 } d ? $"{x.Name} ({x.ExpiresAtRound}): {d}" : $"{x.Name} ({x.ExpiresAtRound})"))}");
        if (_ws.Map.Rooms is { Count: > 0 } rooms)
        {
            var visibleRooms = _visibleCells != null
                ? rooms.Where(r => r.Deleted != true && r.Positions?.Any(p => p.Count >= 2 && _visibleCells.Contains((p[0], p[1]))) == true)
                : rooms.Where(r => r.Deleted != true);
            var visibleRoomNames = visibleRooms.Select(r => r.Name).ToList();
            if (visibleRoomNames.Count > 0)
                sb.AppendLine($"map.rooms: {Join(visibleRoomNames)}");
        }
        if (_ws.Map.Area is { Count: > 0 } area)
            sb.AppendLine($"map.area: {Join(area.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.Map.Entities is { Count: > 0 } ent)
            sb.AppendLine($"map.entities: {Join(ent.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.Map.Objects is { Count: > 0 } obj)
            sb.AppendLine($"map.objects: {Join(obj.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.Map.Doors is { Count: > 0 } allDoors)
        {
            var physIds = string.Join("; ", allDoors.Select((d, i) => (d, i)).Where(x => x.d.IsDoor == true && x.d.IsWindow != true).Select(x => x.i));
            var passIds = string.Join("; ", allDoors.Select((d, i) => (d, i)).Where(x => x.d.IsDoor != true).Select(x => x.i));
            if (physIds.Length > 0) sb.AppendLine($"map.doors (двери): {physIds}");
            if (passIds.Length > 0) sb.AppendLine($"map.doors (проходы): {passIds}");
        }
        if (_ws.Narrative?.PlotThreads is { Count: > 0 } threads)
            sb.AppendLine($"narrative.plotThreads: {Join(threads.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.Narrative?.Npcs is { Count: > 0 } npcs)
            sb.AppendLine($"narrative.npcs: {Join(npcs.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.ScheduledEvents is { Count: > 0 } sched)
            sb.AppendLine($"scheduledEvents: {Join(sched.Where(x => x.Deleted != true).Select(x => $"{x.Name} ({x.FireAtRound})"))}");


        // ── Бой ────────────────────────────────────────────────────────────
        if (_ws.Combat?.Active == true)
        {
            var initiative = _ws.Combat.Initiative?
                .Where(e => e.Deleted != true)
                .Select(e => $"{e.Symbol}({e.Score})");
            // Кто из участников боя может видеть героя сейчас (дистанция, линия обзора, свет/тёмное
            // зрение) — факт для решений «преследовать/искать/бросить» и конца боя при бегстве.
            var seesHero = _ws.Combat.Initiative?
                .Where(e => e.Deleted != true && e.Symbol != _ws.Hero?.Symbol)
                .Select(e => _ws.Map.Entities?.FirstOrDefault(x => x.Deleted != true && x.Symbol == e.Symbol))
                .Where(x => x != null && MovementCalculator.EntitySeesHero(_ws, x))
                .Select(x => x!.Symbol);
            sb.AppendLine($"combat: currentTurn={_ws.Combat.CurrentTurn} initiative=[{string.Join(", ", initiative ?? [])}] seesHero=[{string.Join(", ", seesHero ?? [])}]");
            if (TurnQueue() is { } queue) sb.AppendLine($"  очередь ходов до героя: {queue}");
            // Участники боя: хиты (что уже нанесено), КД из бестиария, клетка, состояния — без этого мастер вёл урон
            // врагам по памяти, и раненый враг в следующем вызове оказывался целым.
            foreach (var e in _ws.Combat.Initiative?.Where(x => x.Deleted != true && x.Symbol != _ws.Hero?.Symbol) ?? [])
            {
                int idx = _ws.Map.Entities?.FindIndex(x => x.Deleted != true && x.Symbol == e.Symbol) ?? -1;
                if (idx < 0) continue;
                var who = _ws.Map.Entities![idx];
                var bestiary = Helpers.MonsterDatabase.Find(who.MonsterKey);
                string ac = bestiary?.Ac is { Length: > 0 } a ? $" ac:{a.Split(' ')[0]}" : "";
                string fx = who.Effects?.Where(f => f.Deleted != true).Select(f => f.Name).ToList() is { Count: > 0 } list ? $" effects:{string.Join("; ", list)}" : "";
                string pos = who.Position is { Count: >= 2 } p ? $" [{p[0]},{p[1]}]" : "";
                if (who.Position is { Count: >= 2 } wp && _ws.Hero?.Position is { Count: >= 2 } hp2)
                    pos += $" до героя {TargetGeometry.DistanceFt(wp[0], wp[1], hp2[0], hp2[1])} фт";
                if (e.Ally == true) pos += " союзник";
                sb.AppendLine($"  {who.Symbol} map.entities id:{idx} {who.Name}{(who.MonsterKey != null ? $" ({who.MonsterKey})" : "")} hp:{(string.IsNullOrEmpty(who.Hp) ? "?" : who.Hp)}{ac}{pos}{fx}");
            }
        }

        // ── История ────────────────────────────────────────────────────────
        // Хвост последних реплик — без него ИИ не помнит, что уже сказал сам/что ответил игрок
        // внутри одного разговора с NPC (только narrative.npcs.notes переживает между вызовами).
        // skipLast: текущее действие игрока уже добавлено в _ws.History ДО вызова SendAction
        // (см. MouseUiHelper.HandleGameScreen) — не показываем его тут же вторым разом.
        var all = _ws.History ?? [];
        var skipLast = all.LastOrDefault()?.Text == playerAction ? 1 : 0;
        var history = all.SkipLast(skipLast).TakeLast(MaxHistoryForAi);

        foreach (var msg in history)
        {
            sb.AppendLine(msg.Author is { } a
                ? $"• [{a}]: {msg.Text}"
                : $"• {msg.Text}");
        }

        // ── Ближайшие клетки героя ─────────────────────────────────────────
        int heroCol = 0, heroRow = 0;
        HashSet<(int, int)>? nearbyCells = null;
        if (_ws.Hero?.Position is { Count: >= 2 } heroPos)
        {
            heroCol = heroPos[0];
            heroRow = heroPos[1];
            nearbyCells = [];
            for (int dc = -1; dc <= 1; dc++)
                for (int dr = -1; dr <= 1; dr++)
                    nearbyCells.Add((heroCol + dc, heroRow + dr));
        }

        // ── Видимое ────────────────────────────────────────────────────────
        var visSb = new System.Text.StringBuilder();
        if (_visibleCells != null)
        {
            var visObj = _ws.Map.Objects?
                .Where(o => o.Deleted != true && o.Position is { Count: >= 2 } && _visibleCells.Contains((o.Position[0], o.Position[1])))
                .Select(o => o.Hidden == true ? $"{o.Name}(скрыт)" : o.Name);
            if (visObj?.Any() == true)
                visSb.AppendLine($"map.objects: {Join(visObj)}");

            var visEnt = _ws.Map.Entities?
                .Where(e => e.Deleted != true && e.Position is { Count: >= 2 } && _visibleCells.Contains((e.Position[0], e.Position[1])))
                .Select(e => e.Hidden == true ? $"{e.Name}(скрыт)" : e.Name);
            if (visEnt?.Any() == true)
                visSb.AppendLine($"map.entities: {Join(visEnt)}");

            AppendDoorIds(visSb, _ws.Map.Doors, isVisible: d =>
                d.From is { Count: >= 2 } && d.To is { Count: >= 2 } &&
                (_visibleCells.Contains((d.From[0], d.From[1])) || _visibleCells.Contains((d.To[0], d.To[1]))));
        }

        // ── Рядом ──────────────────────────────────────────────────────────
        var nearSb = new System.Text.StringBuilder();
        if (_visibleCells != null && nearbyCells != null)
        {
            var currentRoom = _ws.Map.Rooms?.FirstOrDefault(r =>
                r.Deleted != true &&
                r.Positions?.Any(p => p.Count >= 2 && p[0] == heroCol && p[1] == heroRow) == true);
            if (currentRoom != null)
                nearSb.AppendLine($"map.rooms: {currentRoom.Name}");

            var nearObj = _ws.Map.Objects?
                .Where(o => o.Deleted != true && o.Position is { Count: >= 2 } &&
                            _visibleCells.Contains((o.Position[0], o.Position[1])) &&
                            nearbyCells.Contains((o.Position[0], o.Position[1])))
                .Select(o => o.Hidden == true ? $"{o.Name}(скрыт)" : o.Name);
            if (nearObj?.Any() == true)
                nearSb.AppendLine($"map.objects: {Join(nearObj)}");

            var nearEnt = _ws.Map.Entities?
                .Where(e => e.Deleted != true && e.Position is { Count: >= 2 } &&
                            _visibleCells.Contains((e.Position[0], e.Position[1])) &&
                            nearbyCells.Contains((e.Position[0], e.Position[1])))
                .Select(e => e.Hidden == true ? $"{e.Name}(скрыт)" : e.Name);
            if (nearEnt?.Any() == true)
                nearSb.AppendLine($"map.entities: {Join(nearEnt)}");

            AppendDoorIds(nearSb, _ws.Map.Doors, isVisible: d =>
                d.From is { Count: >= 2 } && d.To is { Count: >= 2 } &&
                (_visibleCells.Contains((d.From[0], d.From[1])) || _visibleCells.Contains((d.To[0], d.To[1]))) &&
                (nearbyCells.Contains((d.From[0], d.From[1])) || nearbyCells.Contains((d.To[0], d.To[1]))));
        }

        var visSection  = visSb.Length  > 0 ? $"\n## Видимое\n{visSb}"  : "";
        var nearSection = nearSb.Length > 0 ? $"\n## Рядом\n{nearSb}"   : "";
        return $"## Текущее состояние\n{sb}{visSection}{nearSection}{NpcMemorySection()}\n## Действие игрока\n{playerAction}";
    }

    // Память NPC, которых герой сейчас видит: хвост истории короткий, а разговор с NPC должен
    // переживать его — факты из npcs[i].memory идут в контекст сразу, без отдельного MCP-запроса.
    private string NpcMemorySection()
    {
        if (_visibleCells == null || _ws.Narrative?.Npcs is not { Count: > 0 } npcs) return "";
        var visibleNames = (_ws.Map.Entities ?? [])
            .Where(e => e.Deleted != true && e.Hidden != true && e.Position is { Count: >= 2 }
                        && _visibleCells.Contains((e.Position[0], e.Position[1])))
            .Select(e => e.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sb = new System.Text.StringBuilder();
        foreach (var npc in npcs.Where(n => n.Deleted != true && n.Memory is { Count: > 0 } && visibleNames.Contains(n.Name)))
            sb.AppendLine($"{npc.Name}: {string.Join(" | ", npc.Memory!)}");
        return sb.Length > 0 ? $"\n## Память NPC\n{sb}" : "";
    }

    /// <summary>
    /// Минимальный контекст для редактирования героя до начала игры.
    /// Только hp/state/name + индекс имён — данные AI получает через MCP.
    /// </summary>
    // Блок локации для наполнения нейронкой (StartNewGame, PopulateChunk): геометрию сгенерировал код,
    // нейронке — комнаты с id и клетками (свёрнуты по строкам в диапазоны), двери/проходы и выходы с id.
    // План локации (plan_location): все блоки с назначением — чтобы наполнение блока вязалось с остальными.
    // Где герой: блок локации (тип, назначение, этаж), комната или снаружи, соседние блоки — чтобы ИИ учитывал окружение.
    // Строка о мире: «world: Мир «X»; место: Y — город, королевство Z, северо-запад; окружение» (подробнее —
    // MCP world_query). Нет мира игры (старые сохранения) — нет строки.
    public string? WorldLine()
    {
        if (_ws.World is not { } link || GameWorld.ForMaster(_ws) is not { } world) return null;
        string line = $"world: «{world.Name}»" + (GameWorld.HasLocation(_ws) ? "" : " — без тактической карты (сцена текстом)");
        if (link.Place is { Length: > 0 } name && MapGen.Generators.WorldAtlas.FindPlace(world, name) is { } p)
            line += $"; место: {p.Name} — {WorldPlaceTypes.Label(p.Type)}, {MapGen.Generators.WorldAtlas.KingdomName(world, p)}, " +
                    MapGen.Generators.WorldAtlas.Surroundings(world, p.X, p.Y, named: true);
        else if (link.X is int x && link.Y is int y)
        {
            int own = world.OwnerAt(x, y);
            line += $"; в пути: [{x},{y}] {(own >= 0 && own < world.Kingdoms.Count ? "королевство " + world.Kingdoms[own].Name : "ничья земля")}, "
                    + MapGen.Generators.WorldAtlas.Surroundings(world, x, y, named: true);
        }
        return line;
    }

    public string? HeroWhereabouts()
    {
        if (_ws.Hero?.Position is not { Count: >= 2 } hp || _ws.Map.ChunkAt(hp[0], hp[1]) is not { } chunk) return null;
        var room = (_ws.Map.Rooms ?? []).FirstOrDefault(r => r.Deleted != true && r.Positions?.Any(p => p.Count >= 2 && p[0] == hp[0] && p[1] == hp[1]) == true);
        string place = room != null ? $"в комнате «{room.Name}»" : "снаружи, под открытым небом";
        string floor = _ws.Map.HasFloors ? $", {MapConfig.FloorName(chunk.Z)}" : "";
        var line = $"where: {place}; блок {ChunkLabel(chunk)}{floor}: {chunk.Purpose ?? "—"}";
        var furniture = _ws.Map.Furniture ?? [];
        var nearFurniture = furniture.Select((f, i) => (f, i)).Where(x => x.f.Deleted != true
                && x.f.Positions?.Any(p => p.Count >= 2 && Math.Max(Math.Abs(p[0] - hp[0]), Math.Abs(p[1] - hp[1])) <= 1) == true)
            .Select(x => $"{FurnitureCatalog.DisplayName(x.f)}(furniture id:{x.i})").ToList();
        if (nearFurniture.Count > 0) line += $"; рядом мебель: {string.Join(", ", nearFurniture)}";
        var near = Neighbors(chunk).Select(n => $"{n.side} — {ChunkLabel(n.chunk)}: {n.chunk.Purpose ?? "—"}").ToList();
        return near.Count > 0 ? $"{line}; рядом: {string.Join("; ", near)}" : line;
    }

    private static string ChunkLabel(MapChunk c) => c.Style != null ? $"{c.Theme}/{c.Style}" : c.Theme;

    // Соседние блоки того же этажа по сторонам (на поле этажи разнесены — сосед по полю того же Z = сосед по плану).
    private IEnumerable<(string side, MapChunk chunk)> Neighbors(MapChunk chunk)
    {
        foreach (var (side, dx, dy) in new[] { ("север", 0, 1), ("юг", 0, -1), ("восток", 1, 0), ("запад", -1, 0) })
            if ((_ws.Map.Chunks ?? []).FirstOrDefault(c => c.X == chunk.X + dx && c.Y == chunk.Y + dy && c.Z == chunk.Z) is { } n)
                yield return (side, n);
    }

    public string LocationPlan(MapChunk? current = null)
    {
        if (_ws.Map.Chunks is not { Count: > 0 } chunks) return "";
        var sb = new System.Text.StringBuilder("## План локации (блоки 20×15, x — восток, y — север)\n");
        foreach (var c in chunks)
        {
            string state = c == current ? "ЭТОТ БЛОК" : c.Populated ? "наполнен" : "не наполнен";
            string floor = _ws.Map.HasFloors ? $", {MapConfig.FloorName(c.Z)}" : "";
            sb.AppendLine($"- блок x:{c.X} y:{c.Y} {ChunkLabel(c)}{floor} [{state}]: {c.Purpose ?? "—"}");
        }
        return sb.ToString();
    }

    public string ChunkState(MapChunk chunk)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Блок локации ({ChunkLabel(chunk)}): клетки {chunk.OriginCol + 1}–{chunk.OriginCol + MapChunk.Cols} × {chunk.OriginRow + 1}–{chunk.OriginRow + MapChunk.Rows}");
        if (!string.IsNullOrEmpty(chunk.Purpose)) sb.AppendLine($"Назначение по плану: {chunk.Purpose}");
        if (_ws.Map.HasFloors) sb.AppendLine($"Этаж: {MapConfig.FloorName(chunk.Z)}");
        var neighbors = Neighbors(chunk).Select(n => $"{n.side} — {ChunkLabel(n.chunk)}: {n.chunk.Purpose ?? "—"}").ToList();
        if (neighbors.Count > 0) sb.AppendLine($"Соседние блоки (учитывай при наполнении): {string.Join("; ", neighbors)}");

        if (MapChunk.IsOutdoorTheme(chunk.Theme) && chunk.Terrain is { } terrain)
        {
            // Открытая местность: комнат нет, ставить можно на любую проходимую клетку рельефа.
            var walkable = new List<List<int>>();
            var kinds = new Dictionary<string, int>();
            for (int r = 0; r < terrain.Count; r++)
                for (int c = 0; c < terrain[r].Length; c++)
                {
                    if (TerrainCatalog.Get(terrain[r][c]) is not { } kind) continue;
                    kinds[kind.Name] = kinds.GetValueOrDefault(kind.Name) + 1;
                    if (!kind.BlocksMove) walkable.Add([chunk.OriginCol + c + 1, chunk.OriginRow + r + 1]);
                }
            sb.AppendLine($"Открытая местность, комнат нет. Рельеф (клеток): {string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
            sb.AppendLine($"Проходимые клетки (позиции существ/объектов — только эти): {CompactCells(walkable)}");
        }
        else
        {
            var rooms = _ws.Map.Rooms ?? [];
            sb.AppendLine("Комнаты (позиции существ/объектов — только эти клетки):");
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room.Deleted == true || room.Positions is not { Count: > 0 } || !chunk.Contains(room.Positions[0][0], room.Positions[0][1])) continue;
                // Колонны/завалы (рельеф пола) заняты — их клетки не предлагаем.
                var free = room.Positions.Where(p => _ws.Map.TerrainAt(p[0], p[1]) is not { BlocksMove: true }).ToList();
                var features = room.Positions.Select(p => _ws.Map.TerrainAt(p[0], p[1]))
                    .Where(k => k != null && TerrainCatalog.InLegend(k)).Select(k => k!.Name).Distinct().ToList();
                string extra = features.Count > 0 ? $" [{string.Join(", ", features)}]" : "";
                sb.AppendLine($"- rooms id:{i} \"{room.Name}\"{extra} ({free.Count} клеток): {CompactCells(free)}");
            }
            // Двор здания (первый этаж) / улицы поселения: проходимые клетки вне комнат.
            if (chunk.Theme is MapChunk.Building or MapChunk.Village && chunk.Terrain is { } yardRows)
            {
                var roomCells = rooms.SelectMany(r => r.Positions ?? []).Where(p => p.Count >= 2).Select(p => (p[0], p[1])).ToHashSet();
                var yard = new List<List<int>>();
                for (int r = 0; r < yardRows.Count; r++)
                    for (int c = 0; c < yardRows[r].Length; c++)
                    {
                        int col = chunk.OriginCol + c + 1, row = chunk.OriginRow + r + 1;
                        if (!roomCells.Contains((col, row)) && TerrainCatalog.Get(yardRows[r][c]) is { BlocksMove: false }) yard.Add([col, row]);
                    }
                if (yard.Count > 0) sb.AppendLine($"{(chunk.Theme == MapChunk.Village ? "Улицы и дворы" : "Двор вокруг здания")} ({yard.Count} клеток): {CompactCells(yard)}");
            }
        }

        var pieces = (_ws.Map.Furniture ?? []).Select((f, i) => (f, i))
            .Where(x => x.f.Deleted != true && x.f.Positions is { Count: > 0 } ps && ps[0].Count >= 2 && chunk.Contains(ps[0][0], ps[0][1]))
            .Select(x =>
            {
                var ps = x.f.Positions;
                string cells = ps.Count == 1 ? $"[{ps[0][0]},{ps[0][1]}]" : $"[{ps[0][0]},{ps[0][1]}]–[{ps[^1][0]},{ps[^1][1]}]";
                return $"id:{x.i} {FurnitureCatalog.DisplayName(x.f)} {cells}";
            }).ToList();
        if (pieces.Count > 0)
            sb.AppendLine($"Мебель (map.furniture, уже на карте — объектами не дублировать, клетки заняты): {string.Join("; ", pieces)}");

        var external = chunk.Exits.Where(e => e.External).Select(e => e.AnchorCell(chunk)).ToHashSet();
        var doors = _ws.Map.Doors ?? [];
        sb.AppendLine("Двери и проходы:");
        var windows = new List<int>();
        for (int i = 0; i < doors.Count; i++)
        {
            var d = doors[i];
            if (d.From is not { Count: >= 2 } f || d.To is not { Count: >= 2 } t || !chunk.Contains(f[0], f[1])) continue;
            if (d.IsWindow == true) { windows.Add(i); continue; }
            string kind = d.IsEntry == true
                ? external.Contains((f[0], f[1])) ? "вход снаружи (выход из локации)" : "переход в соседний блок"
                : (d.IsDoor ?? d.Color?.Count == 3) ? (d.IsDoorOpen == true ? "дверь (открыта)" : "дверь") : "проход";
            sb.AppendLine($"- doors id:{i} [{f[0]},{f[1]}]→[{t[0]},{t[1]}] {kind}");
        }

        foreach (var s in _ws.Map.Stairs ?? [])
            foreach (var (here, there) in new[] { (s.A, s.B), (s.B, s.A) })
                if (here is { Count: >= 2 } && there is { Count: >= 2 } && chunk.Contains(here[0], here[1]))
                    sb.AppendLine($"Лестница [{here[0]},{here[1]}] → {MapConfig.FloorName(_ws.Map.FloorAt(there[0], there[1]))} (клетку не занимать)");

        if (windows.Count > 0) sb.AppendLine($"Окна (doors id, видно насквозь, пройти нельзя): {string.Join(" ", windows)}");

        if (_ws.Hero?.Position is { Count: >= 2 } hp && chunk.Contains(hp[0], hp[1]))
            sb.AppendLine($"Герой: [{hp[0]},{hp[1]}], {HeroWhereabouts()?["where: ".Length..].Split(';')[0]} — описание начала строго по этому месту");

        var usedSymbols = (_ws.Map.Entities ?? []).Where(e => e.Deleted != true).Select(e => e.Symbol)
            .Concat((_ws.Map.Objects ?? []).Where(o => o.Deleted != true).Select(o => o.Symbol))
            .Append(_ws.Hero?.Symbol ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (usedSymbols.Count > 0)
            sb.AppendLine($"Занятые символы существ/объектов (не повторяй): {string.Join(", ", usedSymbols)}");
        return sb.ToString();
    }

    // [[41,46],[42,46],[43,46],[47,46],…] → "ряд 46: 41–43, 47; ряд 45: …" (строки сверху вниз).
    private static string CompactCells(List<List<int>> cells) =>
        string.Join("; ", cells.Where(p => p.Count >= 2).GroupBy(p => p[1]).OrderByDescending(g => g.Key).Select(g =>
        {
            var cols = g.Select(p => p[0]).Distinct().Order().ToList();
            var ranges = new List<string>();
            for (int i = 0; i < cols.Count;)
            {
                int j = i;
                while (j + 1 < cols.Count && cols[j + 1] == cols[j] + 1) j++;
                ranges.Add(i == j ? $"{cols[i]}" : $"{cols[i]}–{cols[j]}");
                i = j + 1;
            }
            return $"ряд {g.Key}: {string.Join(", ", ranges)}";
        }));

    public string HeroMinimalState(string request)
    {
        var sb = new System.Text.StringBuilder();

        if (_ws.Hero != null)
        {
            if (!string.IsNullOrEmpty(_ws.Hero.Name))
                sb.AppendLine($"name: {_ws.Hero.Name} symbol: {_ws.Hero.Symbol}");
            sb.AppendLine($"HP:{_ws.Hero.Hp}");
        }

        if (_ws.Hero?.Inventory is { Count: > 0 } inv)
            sb.AppendLine($"hero.inventory: {Join(inv.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.Hero?.Abilities is { Count: > 0 } abl)
            sb.AppendLine($"hero.abilities: {Join(abl.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.Hero?.Spells is { Count: > 0 } spl)
            sb.AppendLine($"hero.spells: {Join(spl.Where(x => x.Deleted != true).Select(x => $"{x.Name} (ур.{x.Level})"))}");

        return $"## Герой\n{sb}\n## Запрос игрока\n{request}";
    }

    public string TriggerState(List<(CellEntity entity, string triggerType)> triggers, List<Area> areaTriggers, List<(Door door, string triggerType)> doorTriggers)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Сработали триггеры. Данные объектов полностью включены — MCP по ним не вызывать:");

        foreach (var (entity, triggerType) in triggers)
        {
            string stealthNote = triggerType == "spotted" && _ws.Hero?.Stealth is int st
                ? $" (герой скрытен: Скрытность {st})"
                : "";
            if (triggerType == "spotted" && _ws.Combat?.Active == true && entity is LivingEntity watcher
                && !MovementCalculator.EntitySeesHero(_ws, watcher))
                stealthNote += " (героя не видит — заметило идущий бой рядом с союзником, вступает в него)";
            sb.AppendLine($"- {triggerType}{stealthNote}");

            var inEntities = entity is LivingEntity le ? (_ws.Map.Entities?.IndexOf(le) ?? -1) : -1;
            var inObjects = inEntities < 0 ? (_ws.Map.Objects?.IndexOf(entity) ?? -1) : -1;
            int id = inEntities >= 0 ? inEntities : inObjects;
            string listKey = inEntities >= 0 ? "map.entities" : "map.objects";

            var node = JsonSerializer.SerializeToNode(entity, entity.GetType(), _json)!.AsObject();
            node["id"] = id;
            sb.AppendLine($"  {listKey}: {node.ToJsonString(_json)}");
        }

        foreach (var area in areaTriggers)
        {
            sb.AppendLine($"- onExplored");

            bool isRoom = area is Room && (_ws.Map.Rooms?.Contains((Room)area) == true);
            int id = isRoom
                ? _ws.Map.Rooms!.IndexOf((Room)area)
                : (_ws.Map.Area?.IndexOf(area) ?? -1);
            string listKey = isRoom ? "map.rooms" : "map.area";

            var node = JsonSerializer.SerializeToNode(area, _json)!.AsObject();
            node["id"] = id;
            node.Remove("positions");
            sb.AppendLine($"  {listKey}: {node.ToJsonString(_json)}");
        }

        foreach (var (door, triggerType) in doorTriggers)
        {
            sb.AppendLine($"- {triggerType} (door)");

            int id = _ws.Map.Doors?.IndexOf(door) ?? -1;
            var node = JsonSerializer.SerializeToNode(door, _json)!.AsObject();
            node["id"] = id;
            sb.AppendLine($"  map.doors: {node.ToJsonString(_json)}");
        }

        return MinimalState(sb.ToString());
    }

    // Кто ходит дальше — от текущего (не героя: от следующего за героем) по кругу до героя, с границей раунда:
    // «WL1 → WL2 → [новый раунд: advance_round] WL3 → КАД». Без неё мастер, дойдя до конца списка, отдавал ход
    // герою и пропускал врагов с инициативой выше героя в новом раунде.
    private string? TurnQueue()
    {
        if (_ws.Combat is not { Active: true, Initiative: { Count: > 1 } init } c || _ws.Hero?.Symbol is not { } hero) return null;
        var order = init.Where(i => i.Deleted != true).OrderByDescending(i => i.Score).Select(i => i.Symbol).ToList();
        int at = order.IndexOf(c.CurrentTurn);
        if (at < 0) return null;
        if (order[at] == hero) at = (at + 1) % order.Count;
        var parts = new List<string>();
        for (int k = 0, i = at; k < order.Count; k++, i = (i + 1) % order.Count)
        {
            if (i == 0 && k > 0) parts.Add("[новый раунд: advance_round]");
            parts.Add(order[i]);
            if (order[i] == hero) break;
        }
        return string.Join(" → ", parts);
    }

    public string CombatTurnState()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Наступает ход врагов. Вызови сalculate_movement для нужных сущностей, сформируй полный план всех ходов одним history-массивом.");
        if (_ws.Combat != null)
        {
            var initiative = _ws.Combat.Initiative?
                .Where(e => e.Deleted != true)
                .Select(e => $"{e.Symbol}({e.Score})");
            sb.AppendLine($"initiative: [{string.Join(", ", initiative ?? [])}]");
            if (!string.IsNullOrEmpty(_ws.Combat.CurrentTurn))
                sb.AppendLine($"currentTurn: {_ws.Combat.CurrentTurn}");
        }
        return MinimalState(sb.ToString());
    }

    public string RoundTransitionState(RoundBundle bundle)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Начался раунд {bundle.NewRound}. Обработай всё одним JSON-ответом.");
        sb.AppendLine("Патчи ниже (`hero`/`map`/`scheduledEvents`) — КОРНЕВЫЕ ключи ответа, наравне с `history`, а не вложенные в history[].patch (тот формат только для хода врагов в бою). Объедини изменения от всех событий ниже в один `hero`/`map` объект.");

        if (bundle.OnRoundEffects.Count > 0)
        {
            sb.AppendLine("\n## Периодические эффекты");
            foreach (var r in bundle.OnRoundEffects)
            {
                string until = r.Effect.ExpiresAtRound.HasValue ? $"до раунда {r.Effect.ExpiresAtRound}" : "бессрочно";
                sb.AppendLine($"- {EffectPath(r)} id:{r.EffectId} ({r.OwnerName}) \"{r.Effect.Name}\" ({until}): {r.Effect.OnRound!.Effect}");
            }
        }
        if (bundle.ExpiredEffects.Count > 0)
        {
            sb.AppendLine("\n## Истекли эффекты");
            foreach (var r in bundle.ExpiredEffects)
                sb.AppendLine($"- {EffectPath(r)} id:{r.EffectId} ({r.OwnerName}) \"{r.Effect.Name}\": {r.Effect.OnExpire!.Effect} → удали: {EffectDeletePatch(r)}");
        }
        if (bundle.FiredScheduledEvents.Count > 0)
        {
            sb.AppendLine("\n## Запланированные события");
            foreach (var (id, ev) in bundle.FiredScheduledEvents)
                sb.AppendLine($"- scheduledEvents id:{id} \"{ev.Name}\" (fireAtRound:{ev.FireAtRound}): {ev.Effect} → удали: {{\"id\":{id},\"deleted\":true}}");
        }
        if (bundle.AreaRoundTriggers.Count > 0)
        {
            sb.AppendLine("\n## Зональные эффекты (onRound)");
            foreach (var (areaId, area) in bundle.AreaRoundTriggers)
                sb.AppendLine($"- map.area id:{areaId} \"{area.Name}\": {area.Triggers!.OnRound!.Effect}");
        }
        if (bundle.EntityRoundTriggers.Count > 0)
        {
            sb.AppendLine("\n## Существа (onRound)");
            foreach (var (entityId, entity) in bundle.EntityRoundTriggers)
                sb.AppendLine($"- map.entities id:{entityId} \"{entity.Name}\": {entity.Triggers!.OnRound!.Effect}");
        }

        return MinimalState(sb.ToString());
    }

    private static string EffectPath(EffectRef r) =>
        r.OwnerPath == "hero" ? "hero.effects" : $"map.entities[id:{r.EntityId}].effects";

    private static string EffectDeletePatch(EffectRef r) => r.OwnerPath == "hero"
        ? $"{{\"hero\":{{\"effects\":[{{\"id\":{r.EffectId},\"deleted\":true}}]}}}}"
        : $"{{\"map\":{{\"entities\":[{{\"id\":{r.EntityId},\"effects\":[{{\"id\":{r.EffectId},\"deleted\":true}}]}}]}}}}";

    // Filters by index (id) when filterTerms are integers; returns all if no valid ids provided
    private static JsonArray WithIdsByIndex<T>(List<T>? list, string[]? filterTerms)
    {
        var arr = new JsonArray();
        if (list == null) return arr;

        HashSet<int>? filterIds = null;
        if (filterTerms != null)
        {
            filterIds = [];
            foreach (var t in filterTerms)
                if (int.TryParse(t, out int id)) filterIds.Add(id);
            if (filterIds.Count == 0) filterIds = null;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (filterIds != null && !filterIds.Contains(i)) continue;
            var node = JsonSerializer.SerializeToNode(list[i], _json)!.AsObject();
            node["id"] = i;
            arr.Add(node);
        }
        return arr;
    }

    private static string Join(IEnumerable<string> items) => string.Join("; ", items);

    private static void AppendDoorIds(System.Text.StringBuilder sb, List<Door>? doors, Func<Door, bool> isVisible)
    {
        if (doors is not { Count: > 0 }) return;
        var matching = doors.Select((d, i) => (d, i)).Where(x => isVisible(x.d)).ToList();
        var phys = string.Join("; ", matching.Where(x => x.d.IsDoor == true && x.d.IsWindow != true)
            .Select(x => x.d.Hidden == true ? $"{x.i}(скрыт)" : x.i.ToString()));
        var pass = string.Join("; ", matching.Where(x => x.d.IsDoor != true)
            .Select(x => x.d.Hidden == true ? $"{x.i}(скрыт)" : x.i.ToString()));
        if (phys.Length > 0) sb.AppendLine($"map.doors (двери): {phys}");
        if (pass.Length > 0) sb.AppendLine($"map.doors (проходы): {pass}");
    }

    // ─── Универсальная сериализация ─────────────────────────────────────────

    /// <summary>
    /// Собирает JSON из указанных полей WorldState.
    /// Ключи задаются в формате "hero.hp", "map.rooms", "map.doors", "narrative", "history".
    /// </summary>
    public string Serialize(params string[] fields) => Serialize(fields, null);

    /// <summary>
    /// Собирает JSON из указанных полей WorldState с опциональными фильтрами по имени.
    /// filters: ключ поля → массив подстрок (OR-логика, case-insensitive).
    /// Для WithIds-массивов оригинальные id сохраняются — патчинг остаётся корректным.
    /// </summary>
    public string Serialize(string[] fields, Dictionary<string, string[]>? filters)
    {
        var set = fields.ToHashSet();
        var root = new Dictionary<string, object?>();

        // hero
        if (_ws.Hero != null)
        {
            var hero = new Dictionary<string, object?>();
            if (set.Contains("hero.symbol"))         hero["symbol"]         = _ws.Hero.Symbol;
            if (set.Contains("hero.name"))           hero["name"]           = _ws.Hero.Name;
            if (set.Contains("hero.position"))       hero["position"]       = _ws.Hero.Position;
            if (set.Contains("hero.hp"))             hero["hp"]             = _ws.Hero.Hp;
            if (set.Contains("hero.vision"))         hero["vision"]         = _ws.Hero.VisionFt;
            if (set.Contains("hero.stats"))          hero["stats"]          = WithIds(_ws.Hero.Stats, Filter(filters, "hero.stats"), x => x.Name);
            if (set.Contains("hero.skills"))         hero["skills"]         = WithIds(_ws.Hero.Skills, Filter(filters, "hero.skills"), x => x.Name);
            if (set.Contains("hero.equipmentSlots")) hero["equipmentSlots"] = _ws.Hero.EquipmentSlots;
            if (set.Contains("hero.resources"))      hero["resources"]      = WithIds(_ws.Hero.Resources, Filter(filters, "hero.resources"), x => x.Name);
            if (set.Contains("hero.abilities"))      hero["abilities"]      = WithIds(_ws.Hero.Abilities, Filter(filters, "hero.abilities"), x => x.Name);
            if (set.Contains("hero.spells"))         hero["spells"]         = WithIds(_ws.Hero.Spells, Filter(filters, "hero.spells"), x => x.Name);
            if (set.Contains("hero.inventory"))      hero["inventory"]      = WithIds(_ws.Hero.Inventory, Filter(filters, "hero.inventory"), x => x.Name);
            if (set.Contains("hero.effects"))        hero["effects"]        = WithIds(_ws.Hero.Effects, Filter(filters, "hero.effects"), x => x.Name);
            if (hero.Count > 0) root["hero"] = hero;
        }

        // map
        var map = new Dictionary<string, object?>();
        if (set.Contains("map.cols")) map["cols"] = _ws.Map.Cols;
        if (set.Contains("map.rows")) map["rows"] = _ws.Map.Rows;
        if (set.Contains("map.rooms") && _ws.Map.Rooms != null && _ws.Map.Rooms.Count > 0)
            map["rooms"] = WithIds(_ws.Map.Rooms, Filter(filters, "map.rooms"), r => r.Name);
        if (set.Contains("map.doors") && _ws.Map.Doors != null && _ws.Map.Doors.Count > 0)
            map["doors"] = WithIdsByIndex(_ws.Map.Doors, Filter(filters, "map.doors"));
        if (set.Contains("map.area"))
            map["area"]     = WithIds(_ws.Map.Area, Filter(filters, "map.area"), a => a.Name);
        if (set.Contains("map.entities"))
            map["entities"] = WithIds(_ws.Map.Entities, Filter(filters, "map.entities"), e => e.Name);
        if (set.Contains("map.objects"))
            map["objects"]  = WithIds(_ws.Map.Objects, Filter(filters, "map.objects"), o => o.Name);
        if (set.Contains("map.furniture"))
            map["furniture"] = WithIds(_ws.Map.Furniture, Filter(filters, "map.furniture"), f => FurnitureCatalog.DisplayName(f));
        if (map.Count > 0) root["map"] = map;

        if (set.Contains("narrative") && _ws.Narrative != null)
            root["narrative"] = _ws.Narrative;

        if (_ws.Narrative != null)
        {
            var narrative = new Dictionary<string, object?>();
            if (set.Contains("narrative.plotThreads")) narrative["plotThreads"] = WithIds(_ws.Narrative.PlotThreads, Filter(filters, "narrative.plotThreads"), x => x.Name);
            if (set.Contains("narrative.npcs"))        narrative["npcs"]        = WithIds(_ws.Narrative.Npcs, Filter(filters, "narrative.npcs"), x => x.Name);
            if (narrative.Count > 0) root["narrative"] = narrative;
        }

        if (set.Contains("scheduledEvents"))
            root["scheduledEvents"] = WithIds(_ws.ScheduledEvents, Filter(filters, "scheduledEvents"), x => x.Name);

        if (set.Contains("history"))
            root["history"] = _ws.History?.TakeLast(MaxHistoryForAi);

        return JsonSerializer.Serialize(root, _json);
    }

    // ─── Filter helpers ────────────────────────────────────────────────────

    private static string[]? Filter(Dictionary<string, string[]>? filters, string key) =>
        filters != null && filters.TryGetValue(key, out var terms) ? terms : null;

    private static IEnumerable<T>? FilterList<T>(List<T>? list, string[]? terms, Func<T, string?> getName)
    {
        if (list == null) return null;
        if (terms == null) return list;
        return list.Where(item => terms.Any(t => getName(item)?.Contains(t, StringComparison.OrdinalIgnoreCase) == true));
    }

    /// <summary>
    /// Сериализует список в JsonArray с id = оригинальный индекс.
    /// При фильтрации пропускает не подходящие элементы, но id у оставшихся остаётся оригинальным
    /// — это критично для корректного патчинга.
    /// </summary>
    private static JsonArray WithIds<T>(List<T>? list, string[]? filterTerms, Func<T, string?> getName)
    {
        var arr = new JsonArray();
        if (list == null) return arr;
        for (int i = 0; i < list.Count; i++)
        {
            if (filterTerms != null && !filterTerms.Any(t => getName(list[i])?.Contains(t, StringComparison.OrdinalIgnoreCase) == true))
                continue;
            var node = JsonSerializer.SerializeToNode(list[i], _json)!.AsObject();
            node["id"] = i;
            arr.Add(node);
        }
        return arr;
    }
}
