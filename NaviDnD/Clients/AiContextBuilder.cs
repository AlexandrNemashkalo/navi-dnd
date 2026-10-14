using NaviDnD.Data.Models;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace NaviDnD.Clients;

/// <summary>
///Slices WorldState into cache blocks for Claude API.
///Each block is cached independently - blocks higher in the list invalidate blocks below.
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

    // ─── System blocks (cached) ──────────────────── ────────────────────
    public string Instructions(string promptText) => promptText;

    // ─── User message (not cached) ──────────────────────────────────────

    /// <summary>
    ///Step 1: minimal context (~400-500 tokens).
    ///Only hp/state/position + short narrative + index of names + recent events.
    /// </summary>
    public string MinimalState(string playerAction)
    {
        var sb = new System.Text.StringBuilder();

        // ── Hero ───────────────────────────── ─────────────────────────────
        sb.AppendLine("Location [col,row]: north/up=+row, south/down=-row, east/right=+col, west/left=-col; world [x,y]: north=-y, south=+y.");
        sb.AppendLine($"round:{_ws.Time.TotalRounds} day:{_ws.Time.Day} timeOfDay:{_ws.Time.PartOfDay}");

        if (_ws.Hero != null)
        {
            var pos = _ws.Hero.Position is { } p ? $"[{string.Join(",", p)}]" : "?";
            var speedStr = _ws.Hero.SpeedMax.HasValue
                ? $" speed:{_ws.Hero.SpeedLeft ?? _ws.Hero.SpeedMax}/{_ws.Hero.SpeedMax}"
                : "";
            var acStat = _ws.Hero.Stat(StatKeys.ArmorClass);
            var acStr = acStat?.Value != null ? $" ac:{acStat.Value}" : "";
            var inspStr = _ws.Hero.Inspiration == true ? " inspiration:true" : "";
            var stealthStr = _ws.Hero.Stealth is int st ? $" stealth:{st}" : "";
            var noOaStr = (_ws.Hero.NoOpportunityAttacksRound >= _ws.Time.TotalRounds ? " disengaged:true" : "")
                          + (_ws.Hero.Dead == true ? " dead:true" : "");
            var lightStr = _ws.Hero.Light is { On: true } l ? $" light:{l.Ft}ft" : "";
            // Who is the hero - name, race, class and a note about him: address him in the correct gender and character.
            string who = string.Join(", ", new[] { _ws.Hero.Stat(StatKeys.Race)?.Value, _ws.Hero.Stat(StatKeys.Class)?.Value, LevelText(_ws.Hero) }
                .Where(x => !string.IsNullOrEmpty(x)));
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

            // Characteristics and skills - immediately in context (#id - for the patch): for them the GM most
            // often went to get_world_state, and this is an extra circle of the model (~10 s and the full
            // context again) for each check. Race, class and AC are already in the hero/HP lines; alignment and
            // background are not needed for the rolls.
            string[] known = [StatKeys.Race, StatKeys.Class, StatKeys.ArmorClass, StatKeys.Alignment, StatKeys.Background];
            if (Indexed(_ws.Hero.Stats, s => s.Deleted == true || known.Contains(StatKeys.FromName(s.Name)),
                    (s, i) => $"{s.Name}#{i} {s.Value}") is { } stats)
                sb.AppendLine($"hero.stats: {stats}");
            if (Indexed(_ws.Hero.Skills, s => s.Deleted == true, (s, i) => $"{s.Name}#{i} {s.Value}") is { } skills)
                sb.AppendLine($"hero.skills: {skills}");
        }

        // ── Narrative ─────────────────────────── ────────────────────────────
        if (_ws.Narrative != null)
        {
            if (!string.IsNullOrEmpty(_ws.Narrative.CurrentArc))
                sb.AppendLine($"arc: {_ws.Narrative.CurrentArc}");
            if (!string.IsNullOrEmpty(_ws.Narrative.World))
                sb.AppendLine($"world: {_ws.Narrative.World}");
        }

        // ── Name index (for searching by name via MCP); for the hero - with #id for a patch without a request ──────
        if (Indexed(_ws.Hero?.Inventory, x => x.Deleted == true,
                (x, i) => x.Quantity is > 1 ? $"{x.Name}#{i} ×{x.Quantity}" : $"{x.Name}#{i}") is { } inv)
            sb.AppendLine($"hero.inventory: {inv}");
        if (Indexed(_ws.Hero?.Abilities, x => x.Deleted == true, (x, i) => $"{x.Name}#{i}") is { } abl)
            sb.AppendLine($"hero.abilities: {abl}");
        if (Indexed(_ws.Hero?.Spells, x => x.Deleted == true,
                (x, i) => $"{x.Name}#{i} (lvl {x.Level}{(x.Prepared == true && x.Level > 0 ? ", prepared" : "")})") is { } spl)
            sb.AppendLine($"hero.spells: {spl}");
        if (Indexed(_ws.Hero?.Resources, x => x.Deleted == true, (x, i) => $"{x.Name}#{i} {x.Value}") is { } res)
            sb.AppendLine($"hero.resources: {res}");
        // Action every round (onRound) - too: it determines whether you can rest, and there are no other effect fields here.
        if (Indexed(_ws.Hero?.Effects, x => x.Deleted == true, (x, i) =>
                $"{x.Name}#{i} ({x.ExpiresAtRound})"
                + (x.Description is { Length: > 0 } d ? $": {d}" : "")
                + (x.OnRound?.Effect is { Length: > 0 } tick ? $" [each round: {tick}]" : "")) is { } eff)
            sb.AppendLine($"hero.effects: {eff}");
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
            if (physIds.Length > 0) sb.AppendLine($"map.doors (doors): {physIds}");
            if (passIds.Length > 0) sb.AppendLine($"map.doors (passages): {passIds}");
        }
        if (_ws.Narrative?.PlotThreads is { Count: > 0 } threads)
            sb.AppendLine($"narrative.plotThreads: {Join(threads.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.Narrative?.Npcs is { Count: > 0 } npcs)
            sb.AppendLine($"narrative.npcs: {Join(npcs.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.ScheduledEvents is { Count: > 0 } sched)
            sb.AppendLine($"scheduledEvents: {Join(sched.Where(x => x.Deleted != true).Select(x => $"{x.Name} ({x.FireAtRound})"))}");


        // ── Fight ────────────────────────────── ──────────────────────────────
        if (_ws.Combat?.Active == true)
        {
            var initiative = _ws.Combat.Initiative?
                .Where(e => e.Deleted != true)
                .Select(e => $"{e.Symbol}({e.Score})");
            // Which of the combatants can see the hero now (distance, line of sight, light/dark vision) is a
            // fact for the “pursue/search/throw” decisions and the end of the battle when fleeing.
            var seesHero = _ws.Combat.Initiative?
                .Where(e => e.Deleted != true && e.Symbol != _ws.Hero?.Symbol)
                .Select(e => _ws.Map.Entities?.FirstOrDefault(x => x.Deleted != true && x.Symbol == e.Symbol))
                .Where(x => x != null && MovementCalculator.EntitySeesHero(_ws, x))
                .Select(x => x!.Symbol);
            sb.AppendLine($"combat: currentTurn={_ws.Combat.CurrentTurn} initiative=[{string.Join(", ", initiative ?? [])}] seesHero=[{string.Join(", ", seesHero ?? [])}]");
            if (TurnQueue() is { } queue) sb.AppendLine($"  turn order until hero: {queue}");
            // Participants in the battle: hit points (what has already been dealt), AC from the bestiary, cell,
            // conditions - without this, the GM dealt damage to enemies from memory, and the wounded enemy
            // turned out to be unharmed in the next call.
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
                    pos += $" to hero {TargetGeometry.DistanceFt(wp[0], wp[1], hp2[0], hp2[1])} ft";
                if (e.Ally == true) pos += " ally";
                sb.AppendLine($"  {who.Symbol} map.entities id:{idx} {who.Name}{(who.MonsterKey != null ? $" ({who.MonsterKey})" : "")} hp:{(string.IsNullOrEmpty(who.Hp) ? "?" : who.Hp)}{ac}{pos}{fx}");
            }
        }

        // ── History ──────────────────────────── ──────────────────────────── The tail of the last remarks -
        // without it, the AI does not remember what it has already said/what the player answered within the
        // same conversation with NPC (only narrative.npcs.notes worries between calls). skipLast: the player's
        // current action has already been added to _ws.History BEFORE calling SendAction (see
        // MouseUiHelper.HandleGameScreen) - do not show it immediately a second time.
        var all = _ws.History ?? [];
        var skipLast = all.LastOrDefault()?.Text == playerAction ? 1 : 0;
        var history = all.SkipLast(skipLast).TakeLast(MaxHistoryForAi);

        foreach (var msg in history)
        {
            sb.AppendLine(msg.Author is { } a
                ? $"• [{a}]: {msg.Text}"
                : $"• {msg.Text}");
        }

        // ── Nearest hero cells ──────────────────── ─────────────────────
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

        // ── Visible ──────────────────────────── ────────────────────────────
        var visSb = new System.Text.StringBuilder();
        if (_visibleCells != null)
        {
            // Visible - with #id and a cell (creatures still have hit points): no request is needed to attack, talk and patch.
            if (Indexed(_ws.Map.Objects, o => o.Deleted == true || o.Position is not { Count: >= 2 } || !_visibleCells.Contains((o.Position[0], o.Position[1])),
                    (o, i) => $"{o.Name}#{i} [{o.Position![0]},{o.Position[1]}]{(o.Hidden == true ? "(hidden)" : "")}") is { } visObj)
                visSb.AppendLine($"map.objects: {visObj}");

            if (Indexed(_ws.Map.Entities, e => e.Deleted == true || e.Position is not { Count: >= 2 } || !_visibleCells.Contains((e.Position[0], e.Position[1])),
                    (e, i) => $"{e.Name}#{i} [{e.Position![0]},{e.Position[1]}]{(string.IsNullOrEmpty(e.Hp) ? "" : $" hp:{e.Hp}")}{(e.Hidden == true ? "(hidden)" : "")}") is { } visEnt)
                visSb.AppendLine($"map.entities: {visEnt}");

            AppendDoorIds(visSb, _ws.Map.Doors, withState: true, isVisible: d =>
                d.From is { Count: >= 2 } && d.To is { Count: >= 2 } &&
                (_visibleCells.Contains((d.From[0], d.From[1])) || _visibleCells.Contains((d.To[0], d.To[1]))));

            // Terrain in the field of view (water, bushes, debris) - otherwise the GM added the environment
            // according to the location plan: the “forest approach by the river” turned into a river at the
            // entrance, which is not on the map.
            var ground = _visibleCells.Select(c => _ws.Map.GroundAt(c.Item1, c.Item2)?.Name).Where(n => !string.IsNullOrEmpty(n))
                .GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key!).ToList();
            if (ground.Count > 0) visSb.AppendLine($"terrain: {Join(ground)}");
        }

        // ── Nearby ───────────────────────────── ─────────────────────────────
        var nearSb = new System.Text.StringBuilder();
        if (_visibleCells != null && nearbyCells != null)
        {
            var currentRoom = _ws.Map.Rooms?.FirstOrDefault(r =>
                r.Deleted != true &&
                r.Positions?.Any(p => p.Count >= 2 && p[0] == heroCol && p[1] == heroRow) == true);
            if (currentRoom != null)
                nearSb.AppendLine($"map.rooms: {currentRoom.Name}");

            var nearObj = _ws.Map.Objects?
                .Select((o, i) => (o, i))
                .Where(x => x.o.Deleted != true && x.o.Position is { Count: >= 2 }
                    && _visibleCells.Contains((x.o.Position[0], x.o.Position[1]))
                    && nearbyCells.Contains((x.o.Position[0], x.o.Position[1])))
                .Select(x => new { id = x.i, x.o.Name, x.o.Symbol, x.o.Position, x.o.Hidden,
                    x.o.AiInfo, x.o.Effects, x.o.Triggers, x.o.Light }).ToList();
            if (nearObj is { Count: > 0 })
                nearSb.AppendLine($"map.objects: {JsonSerializer.Serialize(nearObj, _json)}");

            var nearEnt = _ws.Map.Entities?
                .Where(e => e.Deleted != true && e.Position is { Count: >= 2 } &&
                            _visibleCells.Contains((e.Position[0], e.Position[1])) &&
                            nearbyCells.Contains((e.Position[0], e.Position[1])))
                .Select(e => e.Hidden == true ? $"{e.Name}(hidden)" : e.Name);
            if (nearEnt?.Any() == true)
                nearSb.AppendLine($"map.entities: {Join(nearEnt)}");

            AppendDoorIds(nearSb, _ws.Map.Doors, isVisible: d =>
                d.From is { Count: >= 2 } && d.To is { Count: >= 2 } &&
                (_visibleCells.Contains((d.From[0], d.From[1])) || _visibleCells.Contains((d.To[0], d.To[1]))) &&
                (nearbyCells.Contains((d.From[0], d.From[1])) || nearbyCells.Contains((d.To[0], d.To[1]))));
        }

        var visSection  = visSb.Length  > 0 ? $"\n## Visible\n{visSb}"  : "";
        var nearSection = nearSb.Length > 0 ? $"\n## Nearby\n{nearSb}"   : "";
        return $"## Current state\n{sb}{visSection}{nearSection}{NpcMemorySection()}\n## Player action\n{playerAction}";
    }

    // Memory of NPC, whom the hero now sees: the tail of the story is short, and the conversation with NPC
    // should survive it - facts from npcs[i].memory go into context immediately, without a separate MCP-
    // request.
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
        return sb.Length > 0 ? $"\n## NPC memory\n{sb}" : "";
    }

    /// <summary>
    ///Minimal context for editing a hero before the game starts.
    ///Only hp/state/name + name index - AI receives data via MCP.
    /// </summary>
    // Location block for filling with a AI (StartNewGame, PopulateChunk): the geometry was generated by the
    // code, the AI - rooms with id and cells (collapsed by rows into ranges), doors/passages and exits with id.
    // Location plan (plan_location): all blocks with a purpose - so that the filling of the block is linked
    // with the rest. Where hero: location block (type, purpose, floor), room or outside, neighboring blocks -
    // so that the AI takes into account the environment. Line about the world: “world: World “X”; location: Y -
    // city, kingdom Z, environment" (more details - MCP world_query). No game world (old saves) - no line.
    public string? WorldLine()
    {
        if (_ws.World is not { } link || GameWorld.ForMaster(_ws) is not { } world) return null;
        string line = $"world: «{world.Name}»" + (GameWorld.HasLocation(_ws) ? "" : " — no tactical map (text scene)");
        if (link.Place is { Length: > 0 } name && MapGen.Generators.WorldAtlas.FindPlace(world, name) is { } p)
            line += $"; place: {p.Name} — {WorldPlaceTypes.Label(p.Type)}, {MapGen.Generators.WorldAtlas.KingdomName(world, p)}, " +
                    MapGen.Generators.WorldAtlas.Surroundings(world, p.X, p.Y, named: true);
        else if (link.X is int x && link.Y is int y)
        {
            int own = world.OwnerAt(x, y);
            line += $"; traveling: [{x},{y}] {(own >= 0 && own < world.Kingdoms.Count ? "kingdom " + world.Kingdoms[own].Name : "no man's land")}, "
                    + MapGen.Generators.WorldAtlas.Surroundings(world, x, y, named: true);
        }
        return line;
    }

    public string? HeroWhereabouts()
    {
        if (_ws.Hero?.Position is not { Count: >= 2 } hp || _ws.Map.ChunkAt(hp[0], hp[1]) is not { } chunk) return null;
        var room = (_ws.Map.Rooms ?? []).FirstOrDefault(r => r.Deleted != true && r.Positions?.Any(p => p.Count >= 2 && p[0] == hp[0] && p[1] == hp[1]) == true);
        string place = room != null ? $"in room «{room.Name}»" : "outside, under the open sky";
        string floor = _ws.Map.HasFloors ? $", {MapConfig.FloorName(chunk.Z)}" : "";
        var line = $"where: {place}; chunk {ChunkLabel(chunk)}{floor}: {chunk.Purpose ?? "—"}";
        var furniture = _ws.Map.Furniture ?? [];
        var nearFurniture = furniture.Select((f, i) => (f, i)).Where(x => x.f.Deleted != true
                && x.f.Positions?.Any(p => p.Count >= 2 && Math.Max(Math.Abs(p[0] - hp[0]), Math.Abs(p[1] - hp[1])) <= 1) == true)
            .Select(x => $"{FurnitureCatalog.DisplayName(x.f)}(furniture id:{x.i})").ToList();
        if (nearFurniture.Count > 0) line += $"; nearby furniture: {string.Join(", ", nearFurniture)}";
        var near = Neighbors(chunk).Select(n => $"{n.side} — {ChunkLabel(n.chunk)}: {n.chunk.Purpose ?? "—"}").ToList();
        return near.Count > 0 ? $"{line}; nearby: {string.Join("; ", near)}" : line;
    }

    private static string ChunkLabel(MapChunk c) => c.Style != null ? $"{c.Theme}/{c.Style}" : c.Theme;

    // Adjacent blocks of the same floor on the sides (floors are spaced apart on the field - neighbor on the field of the same Z = neighbor according to the plan).
    private IEnumerable<(string side, MapChunk chunk)> Neighbors(MapChunk chunk)
    {
        foreach (var (side, dx, dy) in new[] { ("north", 0, 1), ("south", 0, -1), ("east", 1, 0), ("west", -1, 0) })
            if ((_ws.Map.Chunks ?? []).FirstOrDefault(c => c.X == chunk.X + dx && c.Y == chunk.Y + dy && c.Z == chunk.Z) is { } n)
                yield return (side, n);
    }

    // Filling the block: the GM's plan, not the hero sheet. Plot threads and NPC - with dmNotes: without them,
    // the block was filled in the way intended at the start (the “light and steel” puzzle turned into “water
    // and fire”). From the hero - level for the strength of enemies and passive Perception for stealth of
    // traps.
    public string PopulationState()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"round:{_ws.Time.TotalRounds} day:{_ws.Time.Day} timeOfDay:{_ws.Time.PartOfDay}");
        if (_ws.Hero is { } hero)
        {
            string who = string.Join(", ", new[] { hero.Stat(StatKeys.Race)?.Value, hero.Stat(StatKeys.Class)?.Value, LevelText(hero) }
                .Where(x => !string.IsNullOrEmpty(x)));
            var perception = hero.Skills?.FirstOrDefault(s => s.Deleted != true && SkillNames.IsPerception(s.Name));
            int passive = 10 + System.Text.RegularExpressions.Regex.Matches(perception?.Value ?? "", @"[+\-−]?\d+")
                .Sum(m => int.TryParse(m.Value.Replace('−', '-'), out int v) ? v : 0);
            sb.AppendLine($"hero: {hero.Name}{(who.Length > 0 ? $" ({who})" : "")} symbol:{hero.Symbol} position:{(hero.Position is { Count: >= 2 } hp ? $"[{hp[0]},{hp[1]}]" : "?")} passive Perception:{passive}");
        }
        if (WorldLine() is { } worldLine) sb.AppendLine(worldLine);
        if (_ws.Narrative is { } n)
        {
            if (n.Style is { Length: > 0 } style) sb.AppendLine($"style: {style}");
            if (n.CurrentArc is { Length: > 0 } arc) sb.AppendLine($"arc: {arc}");
            if (n.World is { Length: > 0 } world) sb.AppendLine($"world: {world}");
            foreach (var t in n.PlotThreads?.Where(x => x.Deleted != true) ?? [])
                sb.AppendLine($"thread «{t.Name}» ({t.Status}): {t.Description}{(t.DmNotes is { Length: > 0 } dm ? $" | GM: {dm}" : "")}");
            foreach (var npc in n.Npcs?.Where(x => x.Deleted != true) ?? [])
                sb.AppendLine($"npc «{npc.Name}» ({npc.Attitude}): {npc.Notes}{(npc.DmNotes is { Length: > 0 } dm ? $" | GM: {dm}" : "")}");
        }
        // Unique things that are already on the map, so as not to repeat NPC and story items.
        if (_ws.Map.Entities is { Count: > 0 } ent)
            sb.AppendLine($"map.entities: {Join(ent.Where(x => x.Deleted != true).Select(x => x.Name).Distinct())}");
        if (_ws.Map.Objects is { Count: > 0 } obj)
            sb.AppendLine($"map.objects: {Join(obj.Where(x => x.Deleted != true).Select(x => x.Name).Distinct())}");
        return sb.ToString();
    }

    public string LocationPlan(MapChunk? current = null)
    {
        if (_ws.Map.Chunks is not { Count: > 0 } chunks) return "";
        var sb = new System.Text.StringBuilder("## Location plan (20×15 chunks, x east, y north)\n");
        foreach (var c in chunks)
        {
            string state = c == current ? "THIS CHUNK" : c.Populated ? "populated" : "unpopulated";
            string floor = _ws.Map.HasFloors ? $", {MapConfig.FloorName(c.Z)}" : "";
            sb.AppendLine($"- chunk x:{c.X} y:{c.Y} {ChunkLabel(c)}{floor} [{state}]: {c.Purpose ?? "—"}");
        }
        return sb.ToString();
    }

    public string ChunkState(MapChunk chunk)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Location chunk ({ChunkLabel(chunk)}): cells {chunk.OriginCol + 1}–{chunk.OriginCol + MapChunk.Cols} × {chunk.OriginRow + 1}–{chunk.OriginRow + MapChunk.Rows}");
        if (!string.IsNullOrEmpty(chunk.Purpose)) sb.AppendLine($"Planned purpose: {chunk.Purpose}");
        if (_ws.Map.HasFloors) sb.AppendLine($"Floor: {MapConfig.FloorName(chunk.Z)}");
        var neighbors = Neighbors(chunk).Select(n => $"{n.side} — {ChunkLabel(n.chunk)}: {n.chunk.Purpose ?? "—"}").ToList();
        if (neighbors.Count > 0) sb.AppendLine($"Neighboring chunks (account for these when populating): {string.Join("; ", neighbors)}");

        if (MapChunk.IsOutdoorTheme(chunk.Theme) && chunk.Terrain is { } terrain)
        {
            // Open area: there are no rooms, you can place it on any passable terrain square.
            var walkable = new List<List<int>>();
            var kinds = new Dictionary<string, int>();
            for (int r = 0; r < terrain.Count; r++)
                for (int c = 0; c < terrain[r].Length; c++)
                {
                    if (TerrainCatalog.Get(terrain[r][c]) is not { } kind) continue;
                    kinds[kind.Name] = kinds.GetValueOrDefault(kind.Name) + 1;
                    if (!kind.BlocksMove) walkable.Add([chunk.OriginCol + c + 1, chunk.OriginRow + r + 1]);
                }
            sb.AppendLine($"Open terrain, no rooms. Terrain (cells): {string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
            sb.AppendLine($"Passable cells (creature/object positions must use these): {CompactCells(walkable)}");
        }
        else
        {
            var rooms = _ws.Map.Rooms ?? [];
            sb.AppendLine("Rooms (creature/object positions must use these cells):");
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room.Deleted == true || room.Positions is not { Count: > 0 } || !chunk.Contains(room.Positions[0][0], room.Positions[0][1])) continue;
                // Columns/blockages (floor topography) are occupied - we do not offer their cells.
                var free = room.Positions.Where(p => _ws.Map.TerrainAt(p[0], p[1]) is not { BlocksMove: true }).ToList();
                var features = room.Positions.Select(p => _ws.Map.TerrainAt(p[0], p[1]))
                    .Where(k => k != null && TerrainCatalog.InLegend(k)).Select(k => k!.Name).Distinct().ToList();
                string extra = features.Count > 0 ? $" [{string.Join(", ", features)}]" : "";
                sb.AppendLine($"- rooms id:{i} \"{room.Name}\"{extra} ({free.Count} cells): {CompactCells(free)}");
            }
            // Building courtyard (ground floor) / settlement streets: passable cells outside the rooms.
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
                if (yard.Count > 0) sb.AppendLine($"{(chunk.Theme == MapChunk.Village ? "Streets and courtyards" : "Building courtyard")} ({yard.Count} cells): {CompactCells(yard)}");
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
            sb.AppendLine($"Furniture (map.furniture, already on the map; do not duplicate as objects, cells occupied): {string.Join("; ", pieces)}");

        var external = chunk.Exits.Where(e => e.External).Select(e => e.AnchorCell(chunk)).ToHashSet();
        var doors = _ws.Map.Doors ?? [];
        sb.AppendLine("Doors and passages:");
        var windows = new List<int>();
        for (int i = 0; i < doors.Count; i++)
        {
            var d = doors[i];
            if (d.From is not { Count: >= 2 } f || d.To is not { Count: >= 2 } t || !chunk.Contains(f[0], f[1])) continue;
            if (d.IsWindow == true) { windows.Add(i); continue; }
            string kind = d.IsEntry == true
                ? external.Contains((f[0], f[1])) ? "outside entrance (location exit)" : "transition to a neighboring chunk"
                : (d.IsDoor ?? d.Color?.Count == 3) ? (d.IsDoorOpen == true ? "door (open)" : "door") : "passage";
            sb.AppendLine($"- doors id:{i} [{f[0]},{f[1]}]→[{t[0]},{t[1]}] {kind}");
        }

        foreach (var s in _ws.Map.Stairs ?? [])
            foreach (var (here, there) in new[] { (s.A, s.B), (s.B, s.A) })
                if (here is { Count: >= 2 } && there is { Count: >= 2 } && chunk.Contains(here[0], here[1]))
                    sb.AppendLine($"Stairs [{here[0]},{here[1]}] → {MapConfig.FloorName(_ws.Map.FloorAt(there[0], there[1]))} (keep cell clear)");

        if (windows.Count > 0) sb.AppendLine($"Windows (doors id; transparent but impassable): {string.Join(" ", windows)}");

        if (_ws.Hero?.Position is { Count: >= 2 } hp && chunk.Contains(hp[0], hp[1]))
            sb.AppendLine($"Hero: [{hp[0]},{hp[1]}], {HeroWhereabouts()?["where: ".Length..].Split(';')[0]} — describe the opening at this exact position");

        var usedSymbols = (_ws.Map.Entities ?? []).Where(e => e.Deleted != true).Select(e => e.Symbol)
            .Concat((_ws.Map.Objects ?? []).Where(o => o.Deleted != true).Select(o => o.Symbol))
            .Append(_ws.Hero?.Symbol ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (usedSymbols.Count > 0)
            sb.AppendLine($"Occupied creature/object symbols (do not reuse): {string.Join(", ", usedSymbols)}");
        return sb.ToString();
    }

    // [[41,46],[42,46],[43,46],[47,46],...] → "row 46: 41–43, 47; row 45: ..." (lines from top to bottom).
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
            return $"row {g.Key}: {string.Join(", ", ranges)}";
        }));

    // “lvl 3, xp 1200/2700” - level and experience of the hero (driven by the engine).
    public static string LevelText(Hero hero)
    {
        var rules = Helpers.LevelRules.Current;
        int level = Helpers.LevelRules.LevelOf(hero);
        return $"lvl {level}, xp {hero.Xp ?? 0}" + (rules.NextXp(level) is int next ? $"/{next}" : "");
    }

    public string HeroMinimalState(string request)
    {
        var sb = new System.Text.StringBuilder();

        if (_ws.Hero != null)
        {
            if (!string.IsNullOrEmpty(_ws.Hero.Name))
                sb.AppendLine($"name: {_ws.Hero.Name} symbol: {_ws.Hero.Symbol}");
            sb.AppendLine($"HP:{_ws.Hero.Hp} {LevelText(_ws.Hero)}");
        }

        if (_ws.Hero?.Inventory is { Count: > 0 } inv)
            sb.AppendLine($"hero.inventory: {Join(inv.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.Hero?.Abilities is { Count: > 0 } abl)
            sb.AppendLine($"hero.abilities: {Join(abl.Where(x => x.Deleted != true).Select(x => x.Name))}");
        if (_ws.Hero?.Spells is { Count: > 0 } spl)
            sb.AppendLine($"hero.spells: {Join(spl.Where(x => x.Deleted != true).Select(x => $"{x.Name} (lvl {x.Level})"))}");

        return $"## Hero\n{sb}\n## Player request\n{request}";
    }

    public string TriggerState(List<(CellEntity entity, string triggerType)> triggers, List<Area> areaTriggers, List<(Door door, string triggerType)> doorTriggers)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Triggers fired. Full object data is included; do not request it again through MCP:");

        var sent = new HashSet<CellEntity>();
        foreach (var (source, triggerType) in triggers)
        {
            int inEntities = source is LivingEntity
                ? _ws.Map.Entities?.FindIndex(e => ReferenceEquals(e, source)
                    || !string.IsNullOrEmpty(source.Symbol) && string.Equals(e.Symbol, source.Symbol, StringComparison.OrdinalIgnoreCase)) ?? -1
                : -1;
            CellEntity entity = inEntities >= 0 ? _ws.Map.Entities![inEntities] : source;
            sent.Add(entity);
            string stealthNote = triggerType == "spotted" && _ws.Hero?.Stealth is int st
                ? $" (hero is hidden: Stealth {st})"
                : "";
            if (triggerType == "spotted" && _ws.Combat?.Active == true && entity is LivingEntity watcher
                && !MovementCalculator.EntitySeesHero(_ws, watcher))
                stealthNote += " (cannot see the hero; noticed ongoing combat near an ally and is joining)";
            sb.AppendLine($"- {triggerType}{stealthNote}");

            var inObjects = inEntities < 0 ? (_ws.Map.Objects?.IndexOf(entity) ?? -1) : -1;
            int id = inEntities >= 0 ? inEntities : inObjects;
            string listKey = inEntities >= 0 ? "map.entities" : "map.objects";

            var node = JsonSerializer.SerializeToNode(entity, entity.GetType(), _json)!.AsObject();
            node["id"] = id;
            sb.AppendLine($"  {listKey}: {node.ToJsonString(_json)}");
        }

        // Data from possible participants, not just the creature that first saw the hero.
        if (triggers.Any(t => t.triggerType == "spotted"))
        {
            foreach (var (entity, id) in (_ws.Map.Entities ?? []).Select((e, i) => (e, i)))
            {
                if (entity.Deleted == true || sent.Contains(entity)
                    || (entity.Triggers?.Spotted == null && _ws.Combat?.Initiative?.Any(i => i.Symbol == entity.Symbol) != true)) continue;
                var node = JsonSerializer.SerializeToNode(entity, entity.GetType(), _json)!.AsObject();
                node["id"] = id;
                sb.AppendLine($"  Potential participant (not a separate trigger), map.entities: {node.ToJsonString(_json)}");
            }
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

    // Who goes next - from the current (not the hero: from the next after the hero) in a circle to the hero,
    // with the round boundary: “WL1 → WL2 → [new round: advance_round] WL3 → KAD.” Without it, the GM, having
    // reached the end of the list, gave the turn to the hero and let enemies with initiative higher than the
    // hero pass in the new round.
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
            if (i == 0 && k > 0) parts.Add("[new round: advance_round]");
            parts.Add(order[i]);
            if (order[i] == hero) break;
        }
        return string.Join(" → ", parts);
    }

    public string CombatTurnState()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Enemies' turn begins. Call calculate_movement for creatures that need movement; return every turn in one history array.");
        if (_ws.Combat != null)
        {
            var initiative = _ws.Combat.Initiative?
                .Where(e => e.Deleted != true)
                .Select(e => $"{e.Symbol}({e.Score})");
            sb.AppendLine($"initiative: [{string.Join(", ", initiative ?? [])}]");
            if (!string.IsNullOrEmpty(_ws.Combat.CurrentTurn))
                sb.AppendLine($"currentTurn: {_ws.Combat.CurrentTurn}");
        }
        var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _ws.Combat?.Initiative?.Where(e => e.Deleted != true && e.Symbol != _ws.Hero?.Symbol) ?? [])
        {
            var enemy = _ws.Map.Entities?.FirstOrDefault(e => e.Deleted != true && e.Symbol == entry.Symbol);
            if (enemy == null) continue;
            sb.AppendLine($"{enemy.Symbol} speedFt:{enemy.SpeedMax ?? 30} notes:{string.Join("; ", enemy.AiInfo ?? [])}");
            var monster = Helpers.MonsterDatabase.Find(enemy.MonsterKey);
            if (monster == null || !sent.Add(monster.Name)) continue;
            sb.AppendLine($"## Abilities of {monster.Name} (only these abilities; respect limits and recharge)");
            if (!string.IsNullOrWhiteSpace(monster.Spells)) sb.AppendLine($"spells: {monster.Spells}");
            foreach (var ability in monster.Traits.Concat(monster.Actions).Concat(monster.Reactions))
                sb.AppendLine($"{ability.Name}: {ability.Text}");
        }
        return MinimalState(sb.ToString());
    }

    public string RoundTransitionState(RoundBundle bundle)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Round {bundle.NewRound} begins. Process everything in one JSON response.");
        sb.AppendLine("The patches below (`hero`/`map`/`scheduledEvents`) are ROOT response keys alongside `history`, not nested in history[].patch (that format is only for enemy combat turns). Merge all events below into one `hero`/`map` object.");

        if (bundle.OnRoundEffects.Count > 0)
        {
            sb.AppendLine("\n## Recurring effects");
            foreach (var r in bundle.OnRoundEffects)
            {
                string until = r.Effect.ExpiresAtRound.HasValue ? $"until round {r.Effect.ExpiresAtRound}" : "indefinite";
                sb.AppendLine($"- {EffectPath(r)} id:{r.EffectId} ({r.OwnerName}) \"{r.Effect.Name}\" ({until}): {r.Effect.OnRound!.Effect}");
            }
        }
        if (bundle.ExpiredEffects.Count > 0)
        {
            sb.AppendLine("\n## Expired effects");
            foreach (var r in bundle.ExpiredEffects)
                sb.AppendLine($"- {EffectPath(r)} id:{r.EffectId} ({r.OwnerName}) \"{r.Effect.Name}\": {r.Effect.OnExpire!.Effect} → delete: {EffectDeletePatch(r)}");
        }
        if (bundle.FiredScheduledEvents.Count > 0)
        {
            sb.AppendLine("\n## Scheduled events");
            foreach (var (id, ev) in bundle.FiredScheduledEvents)
                sb.AppendLine($"- scheduledEvents id:{id} \"{ev.Name}\" (fireAtRound:{ev.FireAtRound}): {ev.Effect} → delete: {{\"id\":{id},\"deleted\":true}}");
        }
        if (bundle.AreaRoundTriggers.Count > 0)
        {
            sb.AppendLine("\n## Area effects (onRound)");
            foreach (var (areaId, area) in bundle.AreaRoundTriggers)
                sb.AppendLine($"- map.area id:{areaId} \"{area.Name}\": {area.Triggers!.OnRound!.Effect}");
        }
        if (bundle.EntityRoundTriggers.Count > 0)
        {
            sb.AppendLine("\n## Creatures (onRound)");
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

    // List elements with their index (aka id for the patch), without deleted ones; null - nothing to show.
    private static string? Indexed<T>(List<T>? items, Func<T, bool> skip, Func<T, int, string> format)
    {
        if (items is not { Count: > 0 }) return null;
        var parts = items.Select((x, i) => (x, i)).Where(p => !skip(p.x)).Select(p => format(p.x, p.i)).ToList();
        return parts.Count > 0 ? Join(parts) : null;
    }

    // withState - the doors are still “open/closed” (what the hero sees; action with the door - door_action to id).
    private static void AppendDoorIds(System.Text.StringBuilder sb, List<Door>? doors, Func<Door, bool> isVisible, bool withState = false)
    {
        if (doors is not { Count: > 0 }) return;
        var matching = doors.Select((d, i) => (d, i)).Where(x => isVisible(x.d)).ToList();
        string State(Door d) => withState ? (d.IsDoorOpen == true ? " open" : " closed") : "";
        var phys = string.Join("; ", matching.Where(x => x.d.IsDoor == true && x.d.IsWindow != true)
            .Select(x => x.d.Hidden == true ? $"{x.i}(hidden)" : $"{x.i}{State(x.d)}"));
        var pass = string.Join("; ", matching.Where(x => x.d.IsDoor != true)
            .Select(x => x.d.Hidden == true ? $"{x.i}(hidden)" : x.i.ToString()));
        if (phys.Length > 0) sb.AppendLine($"map.doors (doors): {phys}");
        if (pass.Length > 0) sb.AppendLine($"map.doors (passages): {pass}");
    }

    // ─── Universal serialization ──────────────────── ─────────────────────

    /// <summary>
    ///Collects JSON from the specified fields WorldState.
    ///Keys are specified in the format "hero.hp", "map.rooms", "map.doors", "narrative", "history".
    /// </summary>
    public string Serialize(params string[] fields) => Serialize(fields, null);

    /// <summary>
    ///Collects JSON from the specified WorldState fields with optional filters by name.
    ///filters: field key → array of substrings (OR-logic, case-insensitive).
    ///For WithIds- arrays, the original id are preserved - patching remains correct.
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
            root["history"] = _ws.History?.TakeLast(MaxHistoryForAi).Select(m => new { m.Author, m.Text });

        var serialized = JsonSerializer.SerializeToNode(root, _json)!;
        if (serialized["narrative"]?["npcs"] is JsonArray npcs)
            foreach (var npc in npcs.OfType<JsonObject>()) npc.Remove("speechVoice");
        return serialized.ToJsonString(_json);
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
    ///Serializes the list to JsonArray with id = original index.
    ///When filtering, it skips unsuitable elements, but id for the remaining ones remains original
    ///- this is critical for correct patching.
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
