using System.Text;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using NaviDnD.Data.Models;

namespace NaviDnD;

public class Storage
{
    public HashSet<(int X, int Y)> ExploredCells = [];

    // Map items that the hero has already seen (“name@number,row”): in the fog they are remembered as a dim
    // symbol. null - after loading it is not yet seeded (the first frame considers objects on the studied cells
    // to be visible).
    public HashSet<string>? SeenObjects;

    public static string ObjectKey(CellEntity o) => $"{o.Name}@{o.Position[0]},{o.Position[1]}";

    // The object (not a creature) is remembered by the hero: it lies on the studied cell and the hero has already seen it.
    public bool IsRemembered(CellEntity o) =>
        o is not LivingEntity && o.Position is { Count: >= 2 } p && ExploredCells.Contains((p[0], p[1]))
        && SeenObjects?.Contains(ObjectKey(o)) == true;
    public HashSet<(int, int)> VisibleCells = [];

    // Whether to save ExploredCells between runs (see AppConfig.SaveExploredCells) - set by the owner of
    // Storage (Program.cs); default true, so that other places (MCP-tools, tests) that create Storage() without
    // this step do not lose the default behavior.
    public bool SaveExploredCells { get; set; } = true;

    public NewGameData NewGameData { get; set; }

    public WorldState WorldState { get; private set; } = new WorldState();
    public int DiagonalUsed { get; private set; } = 0;
    public void ResetDiagonalCount() => DiagonalUsed = 0;

    private Dictionary<string, List<int>> _entityColors = [];
    private bool _wasSpeedExhausted = false;
    private sealed class RoundRuntime { public int? LastRound; }
    private readonly ConditionalWeakTable<Area, RoundRuntime> _areaRuntime = new();
    // Runtime identity survives index shifts and patches; removed effects release their entries.
    private sealed class EffectRuntime
    {
        public bool ExpirationFired;
        public int? LastRound;
    }
    private readonly ConditionalWeakTable<StatusEffect, EffectRuntime> _effectRuntime = new();
    private sealed class EventRuntime { public bool Fired; }
    private readonly ConditionalWeakTable<ScheduledEvent, EventRuntime> _eventRuntime = new();
    private readonly Dictionary<string, int> _oaOnHeroLastRound = [];
    private readonly HashSet<string> _entitiesSeeingHero = [];
    private readonly ConditionalWeakTable<LivingEntity, RoundRuntime> _entityRuntime = new();

    // One attack of opportunity on a creature per round (creature reaction). Returns true when first fired.
    public bool TryMarkOpportunityAttackOnHero(string symbol, int round)
    {
        if (_oaOnHeroLastRound.TryGetValue(symbol, out int last) && last == round) return false;
        _oaOnHeroLastRound[symbol] = round;
        return true;
    }

    // The state lives here, and not in MovementHandler:; it is recreated after each AI- call, and the dedupe
    // did not actually work on it (spotted flew at every step). Returns true only on the “did not see → sees”
    // transition.
    public bool UpdateEntitySeesHero(string symbol, bool sees)
    {
        if (!sees)
        {
            _entitiesSeeingHero.Remove(symbol);
            return false;
        }
        return _entitiesSeeingHero.Add(symbol);
    }

    private static string NormalizeSymbol(string symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return "   ";

        if (symbol.Length > 3)
            return symbol[..3];

        return symbol.Length < 3 ? symbol.PadRight(3, ' ') : symbol;
    }

    public List<int>? GetColorByName(string name)
    {
        return _entityColors.GetValueOrDefault(name);
    }

    public void ResetWorldState()
    {
        // New game - in the language of the selected world (L.World is set by the new game wizard when choosing a world).
        WorldState.Language = L.World;
        WorldState.FormatVersion = StorageFormat.Current;
        WorldState.Hero = null;
        WorldState.Map = new MapConfig();
        WorldState.Narrative = new Narrative();
        WorldState.History = [];
        WorldState.Time = new GameTime();
        WorldState.Combat = null;
        WorldState.Images = null;
        WorldState.ActiveImageKey = null;
        WorldState.ScheduledEvents = [];
        _entityColors.Clear();
        ExploredCells.Clear();
        SeenObjects = null;
        _wasSpeedExhausted = false;
        DiagonalUsed = 0;
        _areaRuntime.Clear();
        _effectRuntime.Clear();
        _eventRuntime.Clear();
        _oaOnHeroLastRound.Clear();
        _entitiesSeeingHero.Clear();
        _entityRuntime.Clear();
    }

    public RoundBundle? TakeRoundBundle()
    {
        int currentRound = WorldState.Time.TotalRounds;

        var expired = new List<EffectRef>();
        var onRound = new List<EffectRef>();

        if (WorldState.Hero != null)
            CollectEntityEffects("hero", null, WorldState.Hero, currentRound, expired, onRound);

        var entities = WorldState.Map.Entities;
        if (entities != null)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                if (entity.Deleted == true) continue;
                CollectEntityEffects("map.entities", i, entity, currentRound, expired, onRound);
            }
        }

        var scheduled = new List<(int, ScheduledEvent)>();
        var events = WorldState.ScheduledEvents;
        if (events != null)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev.Deleted == true) continue;
                var runtime = _eventRuntime.GetOrCreateValue(ev);
                if (currentRound >= ev.FireAtRound && !runtime.Fired)
                {
                    runtime.Fired = true;
                    scheduled.Add((i, ev));
                }
            }
        }

        var areas = CollectAreaRoundTriggers(currentRound);
        var entityTriggers = CollectEntityRoundTriggers(currentRound);

        if (expired.Count == 0 && onRound.Count == 0 && scheduled.Count == 0 && areas.Count == 0
            && entityTriggers.Count == 0)
            return null;

        return new RoundBundle
        {
            NewRound = currentRound,
            ExpiredEffects = expired,
            OnRoundEffects = onRound,
            FiredScheduledEvents = scheduled,
            AreaRoundTriggers = areas,
            EntityRoundTriggers = entityTriggers
        };
    }

    // triggers.onRound for creatures - once per round, only if the creature sees the hero and is not in the
    // initiative (in battle it already acts on its turn).
    private List<(int, LivingEntity)> CollectEntityRoundTriggers(int currentRound)
    {
        var result = new List<(int, LivingEntity)>();
        var entities = WorldState.Map.Entities;
        if (entities == null) return result;

        var inInitiative = WorldState.Combat?.Active == true
            ? WorldState.Combat.Initiative?.Where(e => e.Deleted != true).Select(e => e.Symbol).ToHashSet()
            : null;

        for (int i = 0; i < entities.Count; i++)
        {
            var e = entities[i];
            if (e.Deleted == true || e.Hidden == true || e.Triggers?.OnRound == null) continue;
            if (inInitiative?.Contains(e.Symbol) == true) continue;
            var runtime = _entityRuntime.GetOrCreateValue(e);
            if (runtime.LastRound == currentRound) continue;
            if (!MovementCalculator.EntitySeesHero(WorldState, e)) continue;

            runtime.LastRound = currentRound;
            result.Add((i, e));
        }
        return result;
    }

    private void CollectEntityEffects(
        string ownerPath, int? entityId, CellEntity owner, int currentRound,
        List<EffectRef> expired, List<EffectRef> onRound)
    {
        var effects = owner.Effects;
        if (effects == null) return;

        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e.Deleted == true) continue;
            var runtime = _effectRuntime.GetOrCreateValue(e);

            if (e.ExpiresAtRound.HasValue && currentRound > e.ExpiresAtRound.Value)
            {
                if (e.OnExpire != null)
                {
                    if (!runtime.ExpirationFired)
                    {
                        runtime.ExpirationFired = true;
                        expired.Add(new EffectRef(ownerPath, entityId, owner.Name, i, e));
                    }
                }
                else
                {
                    // Expired, but nothing to report to AI (no onExpire) - do not leave "ghost" in the list of
                    // effects forever with a frozen expired duration.
                    e.Deleted = true;
                }
            }
            else if (e.OnRound != null)
            {
                if (runtime.LastRound != currentRound)
                {
                    runtime.LastRound = currentRound;
                    onRound.Add(new EffectRef(ownerPath, entityId, owner.Name, i, e));
                }
            }
        }
    }

    private List<(int, Area)> CollectAreaRoundTriggers(int currentRound)
    {
        var result = new List<(int, Area)>();
        var heroPos = WorldState.Hero?.Position;
        if (heroPos == null || heroPos.Count < 2) return result;

        int heroCol = heroPos[0], heroRow = heroPos[1];
        var areas = WorldState.Map.Area ?? [];
        for (int i = 0; i < areas.Count; i++)
        {
            var area = areas[i];
            if (area.Deleted == true || area.Triggers?.OnRound == null) continue;
            if (area.Positions?.Any(p => p.Count >= 2 && p[0] == heroCol && p[1] == heroRow) != true) continue;
            var runtime = _areaRuntime.GetOrCreateValue(area);
            if (runtime.LastRound != currentRound)
            {
                runtime.LastRound = currentRound;
                result.Add((i, area));
            }
        }
        return result;
    }

    // Saving is applied (your own, not the wizard’s response): fields that are maintained only by the engine
    // (hero.level/xp), taken from the file. The wizard’s response does not change them - he awards experience
    // with the xpAward. patch
    private bool _fromSave;

    // Save or library hero card - like ApplyUpdateWorldState, but with engine fields.
    public void ApplySavedState(string json)
    {
        _fromSave = true;
        try { ApplyUpdateWorldState(json); }
        finally { _fromSave = false; }
    }

    // Engine messages for history (experience accrued, new level available) - after the GM's response records:
    // GameAiClient picks them up when the response is shown.
    private readonly List<string> _notices = [];

    public List<string> TakeNotices()
    {
        var list = _notices.ToList();
        _notices.Clear();
        return list;
    }

    public void ApplyUpdateWorldState(string jsonUpdate)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonUpdate);
        // LenientStringListConverter: AI sometimes sends a list of strings in one line ("aiInfo": "text") - we
        // accept it as a list of one element, and do not drop the entire response.
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new LenientStringListConverter() } };
        var root = doc.RootElement;
        RebindChangedSymbols(root);

        foreach (var prop in root.EnumerateObject())
        {
            switch (prop.Name.ToLower())
            {
                case "map":
                    ApplyMapUpdate(prop.Value, options);
                    break;

                case "hero":
                    ApplyHero(prop.Value, options);
                    break;

                case "narrative":
                    ApplyNarrativeUpdate(prop.Value, options);
                    break;

                // Entry in the player's notes from the GM: only added to a separate file (PlayerNotes), does
                // not go to WorldState - the AI cannot read the notes.
                case "playernote":
                    foreach (var text in prop.Value.ValueKind == JsonValueKind.Array
                                 ? prop.Value.EnumerateArray().Select(e => e.GetString()) : [prop.Value.GetString()])
                        if (!string.IsNullOrWhiteSpace(text))
                            PlayerNotes.Add(text, WorldState.Time.Day, WorldState.Time.PartOfDay, byMaster: true);
                    break;

                case "time":
                    ApplyTimeUpdate(prop.Value);
                    break;

                // Journey not described by the GM (from save) - will leave with the next action.
                case "pendingtravel":
                    WorldState.PendingTravel = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                    break;

                // Game world: from the save (there is id) - as is; from the GM - patch (concept, kingdoms,
                // places in the chronicle, where the hero is now - GameWorld.Apply).
                case "world":
                    if (prop.Value.ValueKind == JsonValueKind.Object
                        && prop.Value.EnumerateObject().Any(p => p.Name.Equals("id", StringComparison.OrdinalIgnoreCase)))
                        WorldState.World = prop.Value.Deserialize<GameWorldLink>(options);
                    else
                        GameWorld.Apply(WorldState, prop.Value);
                    break;

                case "scheduledevents":
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        WorldState.ScheduledEvents = PatchByIndex(WorldState.ScheduledEvents, prop.Value, options);
                    break;

                case "history":
                    var newHistory = JsonSerializer.Deserialize<List<DialogMessage>>(prop.Value.GetRawText(), options);
                    foreach (var h in newHistory)
                    {
                        h.SpeechText = Helpers.Speech.ValidateSsml(h.Text);
                        h.Text = Helpers.Speech.DisplayText(h.Text);
                        h.Text = h.Text.Replace("\n", "");
                    }

                    WorldState.History.AddRange(newHistory);
                    break;

                case "combat":
                    ApplyCombat(prop.Value, options);
                    break;

                case "images":
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        ApplyImages(prop.Value, options);
                    break;

                case "activeimagkey":
                    WorldState.ActiveImageKey = prop.Value.ValueKind == JsonValueKind.Null
                        ? null
                        : prop.Value.GetString();
                    break;

                // From the save file (the GM does not send them): format and language of the game.
                case "formatversion":
                    if (prop.Value.ValueKind == JsonValueKind.Number) WorldState.FormatVersion = prop.Value.GetInt32();
                    break;

                case "language":
                    if (prop.Value.ValueKind == JsonValueKind.String) WorldState.Language = L.Normalize(prop.Value.GetString());
                    break;
            }
        }
        NormalizeKeys();
        L.SetWorld(WorldState.Language);
        FillMissingStats();
        NpcVoices.Assign(WorldState);
    }

    // Apply aliases once per reference; sequential replacement would corrupt symbol swaps.
    private void RebindChangedSymbols(JsonElement patch)
    {
        string? previousHero = WorldState.Hero?.Symbol, nextHero = previousHero;
        var previousEntities = (WorldState.Map.Entities ?? []).Select(e => e.Symbol).ToArray();
        var nextEntities = previousEntities.ToArray();
        // Rebind inherited references before applying explicit new combat fields, independent of root key order.
        foreach (var property in patch.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            if (property.Name.Equals("hero", StringComparison.OrdinalIgnoreCase)
                && TryPatchProperty(property.Value, "symbol", out var heroSymbol)
                && heroSymbol.ValueKind is JsonValueKind.String or JsonValueKind.Null)
                nextHero = NormalizeSymbol(heroSymbol.GetString() ?? "");
            if (!property.Name.Equals("map", StringComparison.OrdinalIgnoreCase)
                || !TryPatchProperty(property.Value, "entities", out var entities)
                || entities.ValueKind != JsonValueKind.Array) continue;
            foreach (var entity in entities.EnumerateArray())
            {
                if (entity.ValueKind != JsonValueKind.Object || !TryPatchProperty(entity, "id", out var id)
                    || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int index)
                    || index < 0 || index >= nextEntities.Length || !TryPatchProperty(entity, "symbol", out var symbol)
                    || symbol.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) continue;
                nextEntities[index] = NormalizeSymbol(symbol.GetString() ?? "");
            }
        }
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Track(string? oldSymbol, string? newSymbol)
        {
            if (!string.IsNullOrEmpty(oldSymbol) && !string.IsNullOrEmpty(newSymbol) && oldSymbol != newSymbol)
                aliases[oldSymbol] = newSymbol;
        }
        Track(previousHero, nextHero);
        for (int i = 0; i < previousEntities.Length; i++) Track(previousEntities[i], nextEntities[i]);
        if (aliases.Count == 0) return;
        if (WorldState.Combat is { } combat)
        {
            if (combat.CurrentTurn != null && aliases.TryGetValue(combat.CurrentTurn, out var turn)) combat.CurrentTurn = turn;
            foreach (var entry in combat.Initiative ?? [])
                if (entry.Symbol != null && aliases.TryGetValue(entry.Symbol, out var symbol)) entry.Symbol = symbol;
        }
        var reactions = _oaOnHeroLastRound.ToArray();
        _oaOnHeroLastRound.Clear();
        foreach (var (symbol, round) in reactions)
            _oaOnHeroLastRound[aliases.GetValueOrDefault(symbol, symbol)] = round;
        var seeing = _entitiesSeeingHero.ToArray();
        _entitiesSeeingHero.Clear();
        foreach (string symbol in seeing) _entitiesSeeingHero.Add(aliases.GetValueOrDefault(symbol, symbol));
    }

    // GM's words in any language → permanent keys (Data/Models/Keys.cs): part of the day, special hero stats,
    // resource categories. The code compares only the keys.
    private void NormalizeKeys()
    {
        WorldState.Time.PartOfDay = PartsOfDay.Normalize(WorldState.Time.PartOfDay);
        if (WorldState.Hero is not { } hero) return;
        foreach (var stat in hero.Stats ?? []) stat.Key ??= StatKeys.FromName(stat.Name);
        foreach (var resource in hero.Resources ?? []) resource.Category = ResourceCategories.Normalize(resource.Category);
        // “Warrior lvl 3” → “Warrior”: level - hero.level. The level from the stat is taken only from a hero
        // without a level (hero creation); a hero with a GM level will not raise it that way.
        if (hero.Stat(StatKeys.Class) is { } cls && ClassLevel.Split(cls.Value) is (var name, int level))
        {
            cls.Value = name;
            if (hero.Level == null)
            {
                hero.Level = level;
                hero.Xp ??= Helpers.LevelRules.Current.XpFor(level);
            }
        }
    }

    // Experience from the GM: {"monsters":["Goblin",...], "xp":N, "reason":"..."} - monsters in the directory
    // are counted according to the level of danger (LevelRules), xp - for other things (task, find). The result
    // - in the story after the GM's answer.
    private void ApplyXpAward(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object || WorldState.Hero is not { } hero) return;
        var rules = Helpers.LevelRules.Current;
        int total = 0;
        string? reason = null;
        foreach (var p in el.EnumerateObject())
            switch (p.Name.ToLowerInvariant())
            {
                case "monsters" when p.Value.ValueKind == JsonValueKind.Array:
                    foreach (var m in p.Value.EnumerateArray())
                        if (m.ValueKind == JsonValueKind.String) total += rules.MonsterXp(m.GetString());
                    break;
                case "xp" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int xp):
                    total += Math.Max(0, xp);
                    break;
                case "reason" when p.Value.ValueKind == JsonValueKind.String:
                    reason = p.Value.GetString();
                    break;
            }
        if (total <= 0) return;
        bool could = rules.CanLevelUp(hero);
        hero.Xp = (hero.Xp ?? 0) + total;
        _notices.Add(string.IsNullOrWhiteSpace(reason) ? L.WF("[+{0} XP]", total) : L.WF("[+{0} XP: {1}]", total, reason.Trim()));
        if (!could && rules.CanLevelUp(hero))
            _notices.Add(L.W("[Level up available: CHARACTER → “Level up”]"));
    }

    // A dead creature - a body remains on its cell: an object with everything that the GM knew about it (aiInfo
    // - amulet, letter, key) - otherwise the loot and plot clues disappeared along with the enemy; view - in
    // the bestiary of the magazine. Killed - hit points 0 (the GM puts hp "0/N" or writes "→0" in the
    // mechanics): the game itself removes him from the map. Removed by a GM with hit points above 0 - escaped,
    // left, dissipated: no body. Hits 0 with "Unconscious"/"Stable" - stunned, alive, remains. The search and
    // extraction is carried out by the GM (§ End of the battle). The body, which the GM created himself, is not
    // duplicated.
    private static readonly string[] AliveAtZeroHp = ["без сознания", "unconscious", "стабилен", "stable"];

    private static bool AtZeroHp(LivingEntity e) =>
        e.Hp is { Length: > 0 } hp && int.TryParse(hp.Split('/')[0].Trim(), out int current) && current <= 0;

    private static bool KnockedOut(LivingEntity e) =>
        e.Effects?.Any(f => f.Deleted != true && AliveAtZeroHp.Any(n => f.Name?.Contains(n, StringComparison.OrdinalIgnoreCase) == true)) == true;

    // Guard against a missing patch: the GM describes an HP change in an entry's mechanics ("[... damage 4, WL2
    // 11→7]", "HP 12→7") but omits `hp` from the patch, leaving the creature unharmed despite the text. Accept
    // only "SYMBOL (or HP for the hero) before→after" inside [...] and only when current HP exactly matches
    // "before".
    public bool ReconcileMechanics(string text)
    {
        bool changed = false;
        foreach (System.Text.RegularExpressions.Match br in System.Text.RegularExpressions.Regex.Matches(text, @"\[([^\]]*)\]"))
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(br.Groups[1].Value,
                         @"(?<![\w])([\wЁё]{2,3})\s*:?\s*(\d+)\s*(?:→|->)\s*(\d+)"))
            {
                string who = m.Groups[1].Value;
                LivingEntity? target = who.ToUpperInvariant() is "HP" or "ХП"
                    ? WorldState.Hero
                    : string.Equals(who, WorldState.Hero?.Symbol, StringComparison.OrdinalIgnoreCase)
                        ? WorldState.Hero
                        : WorldState.Map.Entities?.FirstOrDefault(e => e.Deleted != true && string.Equals(e.Symbol, who, StringComparison.OrdinalIgnoreCase));
                if (target?.Hp is not { Length: > 0 } hp) continue;
                var parts = hp.Split('/');
                int from = int.Parse(m.Groups[2].Value), to = int.Parse(m.Groups[3].Value);
                if (!int.TryParse(parts[0].Trim(), out int cur) || cur != from || cur == to) continue;
                target.Hp = parts.Length > 1 ? $"{to}/{parts[1].Trim()}" : to.ToString();
                changed = true;
            }
        if (changed) LeaveBodies(LivingEntitySnapshots());
        return changed;
    }

    private List<(LivingEntity Entity, int Index)> LivingEntitySnapshots() => (WorldState.Map.Entities ?? [])
        .Select((entity, index) => (Entity: entity, Index: index)).Where(x => x.Entity.Deleted != true).ToList();

    private void LeaveBodies(List<(LivingEntity Entity, int Index)> before)
    {
        // At 0 HP, the creature is dead: remove it from the map by marking it, without deleting the list entry.
        // Later in the same response the GM references creatures by id (list index), which must remain stable.
        foreach (var fallen in WorldState.Map.Entities?.Where(e => e.Deleted != true && AtZeroHp(e) && !KnockedOut(e)) ?? [])
            fallen.Deleted = true;
        // Entity slots survive death and departure, so metadata changes do not lose identity.
        foreach (var (previous, index) in before)
        {
            var corpse = WorldState.Map.Entities is { } entities && index < entities.Count ? entities[index] : previous;
            if (corpse == WorldState.Hero || corpse.Deleted != true || !AtZeroHp(corpse) || KnockedOut(corpse)
                || corpse.Position is not { Count: >= 2 }) continue;
            // Add the killed species to the journal bestiary (previously populated only by knowledge checks requested by the GM).
            if (corpse.MonsterKey is { Length: > 0 } key)
            {
                var known = (WorldState.Narrative ??= new Narrative()).KnownMonsters ??= [];
                if (!known.Contains(key, StringComparer.OrdinalIgnoreCase)) known.Add(key);
            }
            string name = L.WF("Body: {0}", corpse.Name);
            var objects = WorldState.Map.Objects ??= [];
            if (objects.Any(o => o.Deleted != true && o.Position is { Count: >= 2 } p && p[0] == corpse.Position[0] && p[1] == corpse.Position[1]
                                 && o.Name?.Contains(corpse.Name ?? "\0", StringComparison.OrdinalIgnoreCase) == true)) continue;
            objects.Add(new CellEntity
            {
                Symbol = "(†)",
                Name = name,
                Position = [corpse.Position[0], corpse.Position[1]],
                Color = [150, 150, 150],
                Image = corpse.Image,
                AiInfo = [.. corpse.AiInfo ?? [], L.WF("Killed in battle ({0})", corpse.MonsterKey ?? corpse.Name)],
            });
        }
    }

    // What the GM did not ask, and without this the battle and review lie: - hit points of the creature from
    // the bestiary (monsterKey): average from the reference book (“22 (3d8+9)” → “22/22”) - otherwise the
    // damage to the enemy has nothing to carry between answers, and the wounded enemy in the next challenge is
    // “fresh” again; - dark vision of the hero from his characteristics (“Dark vision 60 ft”) - old heroes were
    // created without darkvisionFt, and the elf saw 10 feet at night. The GM placed the hero beyond the edge of
    // the location, into a rock/tree/furniture or into the void between rooms - the hero is “behind the map” or
    // stuck.
    private void KeepOnWalkableCells()
    {
        var map = WorldState.Map;
        if (map.Chunks is not { Count: > 0 }) return;   // old format maps - as is
        bool OnField(int c, int r) => c >= 1 && r >= 1 && c <= map.Cols && r <= map.Rows && map.ChunkAt(c, r) != null
                                      && (map.TerrainAt(c, r) != null || map.RoomAt(c, r) != null || map.AreasAt(c, r).Count > 0);
        bool Walkable(int c, int r) => OnField(c, r) && map.TerrainAt(c, r) is not { BlocksMove: true };
        bool Occupied(int c, int r, CellEntity self) =>
            (map.Entities ?? []).Any(e => e != self && e.Deleted != true && e.Position is { Count: >= 2 } p && p[0] == c && p[1] == r)
            || (WorldState.Hero != self && WorldState.Hero?.Position is { Count: >= 2 } h && h[0] == c && h[1] == r);
        void Fix(CellEntity who, Func<int, int, bool> ok)
        {
            if (who.Position is not { Count: >= 2 } p || ok(p[0], p[1])) return;
            for (int rad = 1; rad <= 6; rad++)
                for (int dr = -rad; dr <= rad; dr++)
                    for (int dc = -rad; dc <= rad; dc++)
                    {
                        if (Math.Max(Math.Abs(dc), Math.Abs(dr)) != rad) continue;
                        int c = p[0] + dc, r = p[1] + dr;
                        if (!Walkable(c, r) || Occupied(c, r, who)) continue;
                        who.Position = [c, r];
                        return;
                    }
        }
        if (WorldState.Hero != null) Fix(WorldState.Hero, Walkable);
        foreach (var e in map.Entities ?? [])
            if (e.Deleted != true) Fix(e, OnField);
    }

    private void FillMissingStats()
    {
        KeepOnWalkableCells();
        // Those killed/left the map do not remain in the battle queue (except for the hero).
        if (WorldState.Combat?.Initiative is { Count: > 0 } init)
        {
            var onMap = WorldState.Map.Entities?.Where(e => e.Deleted != true).Select(e => e.Symbol).ToHashSet() ?? [];
            init.RemoveAll(i => i.Symbol != WorldState.Hero?.Symbol && !onMap.Contains(i.Symbol));
        }

        foreach (var e in WorldState.Map.Entities ?? [])
        {
            if (e.Deleted == true || Helpers.MonsterDatabase.Find(e.MonsterKey) is not { } m) continue;
            if (string.IsNullOrWhiteSpace(e.Hp) && System.Text.RegularExpressions.Regex.Match(m.Hp, @"^\s*(\d+)") is { Success: true } hp)
                e.Hp = $"{hp.Groups[1].Value}/{hp.Groups[1].Value}";
            // Walking speed is the first number in the reference book (“4 squares, flying 10 squares” is a 5 ft
            // square; or “20 ft.”): otherwise calculate_movement led everyone at 30 ft, and the zombies ran
            // along with the hero. “0 cells, flight 20 cells” (only flies) - the first non-zero.
            if (e.SpeedMax == null && System.Text.RegularExpressions.Regex.Matches(m.Speed, @"(\d+)\s*(\S*)")
                    .FirstOrDefault(x => x.Groups[1].Value != "0") is { } sp)
                e.SpeedMax = int.Parse(sp.Groups[1].Value) * (sp.Groups[2].Value.StartsWith("клет", StringComparison.OrdinalIgnoreCase) ? 5 : 1);
        }

        if (WorldState.Hero is { DarkvisionFt: null } hero && hero.Stat(StatKeys.Darkvision) is { Value: { } dv }
            && System.Text.RegularExpressions.Regex.Match(dv, @"(\d+)") is { Success: true } ft && int.Parse(ft.Groups[1].Value) > 0)
            hero.DarkvisionFt = int.Parse(ft.Groups[1].Value);
    }

    private void ApplyCombat(JsonElement el, JsonSerializerOptions options)
    {
        if (el.ValueKind == JsonValueKind.Null)
        {
            WorldState.Combat = null;
            return;
        }
        if (el.ValueKind != JsonValueKind.Object) return;

        WorldState.Combat ??= new CombatState();

        foreach (var prop in el.EnumerateObject())
        {
            switch (prop.Name.ToLower())
            {
                case "active":
                    WorldState.Combat.Active = prop.Value.GetBoolean();
                    break;
                case "currentturn":
                    string prevTurn = WorldState.Combat.CurrentTurn;
                    WorldState.Combat.CurrentTurn = prop.Value.GetString() ?? "";
                    // The turn was transferred to the hero from another participant - his actions, reactions
                    // and speed are restored at the beginning of his turn (and not at the end of the round: the
                    // reaction spent after the start of the round on someone else’s turn would otherwise be
                    // lost for his entire turn). Loading a save (prevTurn is empty) is not a move.
                    if (prevTurn.Length > 0 && prevTurn != WorldState.Combat.CurrentTurn && WorldState.Combat.CurrentTurn == WorldState.Hero?.Symbol)
                    {
                        ResetActionsForNewRound();
                        if (WorldState.Hero.SpeedMax is int max) WorldState.Hero.SpeedLeft = max;
                    }
                    break;
                case "initiative":
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        WorldState.Combat.Initiative = PatchByIndex(WorldState.Combat.Initiative, prop.Value, options);
                        // Deduplicate by symbol (no-id entries are appended by PatchByIndex, keep latest)
                        if (WorldState.Combat.Initiative?.Count > 1)
                            WorldState.Combat.Initiative = WorldState.Combat.Initiative
                                .GroupBy(e => e.Symbol)
                                .Select(g => g.Last())
                                .OrderByDescending(e => e.Score)
                                .ToList();
                    }
                    break;
            }
        }
    }

    private void ApplyMapUpdate(JsonElement el, JsonSerializerOptions options)
    {
        if (el.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in el.EnumerateObject())
        {
            switch (prop.Name.ToLower())
            {
                case "cols":
                    WorldState.Map.Cols = prop.Value.GetInt32();
                    break;

                case "rows":
                    WorldState.Map.Rows = prop.Value.GetInt32();
                    break;

                case "colors":
                    ApplyColors(prop.Value);
                    break;

                case "rooms":
                    WorldState.Map.Rooms = PatchByIndex(WorldState.Map.Rooms, prop.Value, options);
                    break;

                case "doors":
                    WorldState.Map.Doors = PatchByIndex(WorldState.Map.Doors, prop.Value, options, allowDelete: false);
                    break;

                case "area":
                    WorldState.Map.Area = PatchByIndex(WorldState.Map.Area, prop.Value, options);
                    break;

                case "entities":
                    var entitiesBefore = LivingEntitySnapshots();
                    WorldState.Map.Entities = PatchByIndex(WorldState.Map.Entities, prop.Value, options, retainDeleted: true);
                    LeaveBodies(entitiesBefore);
                    foreach (var entity in WorldState.Map.Entities ?? [])
                    {
                        entity.Symbol = NormalizeSymbol(entity.Symbol ?? "");
                        if (entity.Color != null) _entityColors[entity.Name] = entity.Color;
                    }
                    break;

                case "objects":
                    WorldState.Map.Objects = PatchByIndex(WorldState.Map.Objects, prop.Value, options);
                    foreach (var obj in WorldState.Map.Objects ?? [])
                        obj.Symbol = NormalizeSymbol(obj.Symbol ?? "");
                    break;

                case "triggercells":
                    WorldState.Map.TriggerCells = JsonSerializer.Deserialize<List<TriggerCell>>(prop.Value.GetRawText(), options);
                    break;

                // Location blocks are engine service data (LocationGrower), AI does not patch them; entirely.
                case "chunks":
                    WorldState.Map.Chunks = prop.Value.ValueKind == JsonValueKind.Null
                        ? null : JsonSerializer.Deserialize<List<MapChunk>>(prop.Value.GetRawText(), options);
                    break;

                // Furniture: as objects - by id (break deleted, turn over state, move positions, new - without id).
                case "furniture":
                    WorldState.Map.Furniture = PatchByIndex(WorldState.Map.Furniture, prop.Value, options);
                    break;

                // Stairs between floors are also engine service data; entirely.
                case "stairs":
                    WorldState.Map.Stairs = prop.Value.ValueKind == JsonValueKind.Null
                        ? null : JsonSerializer.Deserialize<List<StairLink>>(prop.Value.GetRawText(), options);
                    break;

                case "defaultimage":
                    WorldState.Map.DefaultImage = prop.Value.ValueKind == JsonValueKind.Null
                        ? null : prop.Value.GetString();
                    break;
            }
        }
    }

    private void ApplyColors(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return;
        WorldState.Map.Colors ??= new ColorSetting();

        foreach (var prop in el.EnumerateObject())
        {
            switch (prop.Name.ToLower())
            {
                case "mapbackground":
                    WorldState.Map.Colors.MapBackground = DeserializeIntList(prop.Value);
                    break;
                case "mapforeground":
                    WorldState.Map.Colors.MapForeground = DeserializeIntList(prop.Value);
                    break;
            }
        }
    }

    private void ApplyHero(JsonElement el, JsonSerializerOptions options)
    {
        if (el.ValueKind != JsonValueKind.Object) return;

        WorldState.Hero ??= new Hero();

        ValidateKeys<Hero>(el, "hero", "xpaward");

        var oldPosition = WorldState.Hero.Position?.ToList();
        bool speedLeftUpdated = false;

        foreach (var prop in el.EnumerateObject())
        {
            switch (prop.Name.ToLower())
            {
                case "symbol":
                    WorldState.Hero.Symbol = NormalizeSymbol(prop.Value.GetString() ?? "");
                    break;
                case "name":
                    WorldState.Hero.Name = prop.Value.GetString()!;
                    break;
                case "hp":
                    WorldState.Hero.Hp = prop.Value.GetString()!;
                    break;
                case "vision":
                case "visionft":
                    if (prop.Value.ValueKind != JsonValueKind.Null)
                        WorldState.Hero.VisionFt = prop.Value.GetInt32();
                    break;
                case "inspiration":
                    WorldState.Hero.Inspiration = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetBoolean();
                    break;
                case "stealth":
                    WorldState.Hero.Stealth = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetInt32();
                    break;
                case "dead":
                    WorldState.Hero.Dead = prop.Value.ValueKind == JsonValueKind.True ? true : null;
                    break;
                // Drives the engine: only from saving; GM - via xpAward.
                case "level" when _fromSave:
                    WorldState.Hero.Level = prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetInt32() : null;
                    break;
                case "xp" when _fromSave:
                    WorldState.Hero.Xp = prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetInt32() : null;
                    break;
                case "level":
                case "xp":
                    Console.Error.WriteLine($"[AI ERROR] hero.{prop.Name} is engine-owned, ignoring (use xpAward).");
                    break;
                case "xpaward":
                    ApplyXpAward(prop.Value);
                    break;
                case "levelupdraft" when _fromSave:
                    WorldState.Hero.LevelUpDraft = prop.Value.ValueKind == JsonValueKind.Object
                        ? JsonSerializer.Deserialize<LevelUpDraft>(prop.Value.GetRawText(), options) : null;
                    break;
                case "noopportunityattacksround":
                    WorldState.Hero.NoOpportunityAttacksRound = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetInt32();
                    break;
                case "light":
                    WorldState.Hero.Light = prop.Value.ValueKind == JsonValueKind.Null
                        ? null
                        : JsonSerializer.Deserialize<LightSource>(prop.Value.GetRawText(), options);
                    break;
                case "position":
                    WorldState.Hero.Position = DeserializeIntList(prop.Value);
                    break;
                case "color":
                    WorldState.Hero.Color = DeserializeIntList(prop.Value);
                    break;
                case "stats":
                    WorldState.Hero.Stats = PatchByIndex(WorldState.Hero.Stats, prop.Value, options);
                    break;
                case "skills":
                    WorldState.Hero.Skills = PatchByIndex(WorldState.Hero.Skills, prop.Value, options);
                    break;
                case "inventory":
                    WorldState.Hero.Inventory = PatchByIndex(WorldState.Hero.Inventory, prop.Value, options);
                    break;
                case "equipmentslots":
                    WorldState.Hero.EquipmentSlots = JsonSerializer.Deserialize<List<string>>(prop.Value.GetRawText(), options)!;
                    break;
                case "abilities":
                    WorldState.Hero.Abilities = PatchByIndex(WorldState.Hero.Abilities, prop.Value, options);
                    break;
                case "spells":
                    WorldState.Hero.Spells = PatchByIndex(WorldState.Hero.Spells, prop.Value, options);
                    break;
                case "resources":
                    WorldState.Hero.Resources = PatchByIndex(WorldState.Hero.Resources, prop.Value, options);
                    break;
                case "speedmax":
                    WorldState.Hero.SpeedMax = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetInt32();
                    break;
                case "speedleft":
                    WorldState.Hero.SpeedLeft = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetInt32();
                    speedLeftUpdated = true;
                    break;
                case "effects":
                    WorldState.Hero.Effects = PatchByIndex(WorldState.Hero.Effects, prop.Value, options);
                    break;
                case "actions":
                    WorldState.Hero.Actions = PatchByIndex(WorldState.Hero.Actions, prop.Value, options);
                    break;
                case "image":
                    WorldState.Hero.Image = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetString();
                    break;
            }
        }

        bool positionChanged = oldPosition != null
            && WorldState.Hero.Position != null
            && !oldPosition.SequenceEqual(WorldState.Hero.Position);

        bool inCombat = WorldState.Combat?.Active == true;
        if (positionChanged && _wasSpeedExhausted && !inCombat)
        {
            WorldState.Time.TotalRounds++;
            _wasSpeedExhausted = false;
            ResetActionsForNewRound();
        }

        if (speedLeftUpdated && WorldState.Hero.SpeedLeft.HasValue)
        {
            _wasSpeedExhausted = !inCombat && WorldState.Hero.SpeedLeft.Value == 0;
            if (inCombat && WorldState.Hero.SpeedLeft.Value >= (WorldState.Hero.SpeedMax ?? 0) && WorldState.Hero.SpeedMax > 0)
                DiagonalUsed = 0;
        }
    }

    public void ApplyClientMovement(int distanceFeet, bool isDiagonal = false, int? newRoundCostFt = null)
    {
        var hero = WorldState.Hero;
        if (hero == null || !hero.SpeedMax.HasValue) return;

        int speedMax  = hero.SpeedMax.Value;
        int speedLeft = hero.SpeedLeft ?? speedMax;
        bool inCombat = WorldState.Combat?.Active == true;

        if (!inCombat && speedLeft < distanceFeet)
        {
            // The first diagonal in the new round must not retain the previous round's surcharge.
            distanceFeet = newRoundCostFt ?? distanceFeet;
            WorldState.Time.TotalRounds++;
            speedLeft = speedMax;
            _wasSpeedExhausted = false;
            ResetActionsForNewRound();
        }

        if (isDiagonal) DiagonalUsed++;
        speedLeft = Math.Max(0, speedLeft - distanceFeet);
        hero.SpeedLeft = speedLeft;
        _wasSpeedExhausted = !inCombat && speedLeft == 0;
    }

    private void ResetActionsForNewRound()
    {
        DiagonalUsed = 0;
        var actions = WorldState.Hero?.Actions;
        if (actions == null) return;
        foreach (var a in actions)
            if (a.Deleted != true)
                a.Value = a.MaxValue;
    }

    private void ApplyTimeUpdate(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in el.EnumerateObject())
        {
            switch (prop.Name.ToLower())
            {
                case "totalrounds":
                    int nextRound = prop.Value.GetInt32();
                    if (nextRound < WorldState.Time.TotalRounds)
                    {
                        _effectRuntime.Clear();
                        _areaRuntime.Clear();
                        _entityRuntime.Clear();
                        _oaOnHeroLastRound.Clear();
                    }
                    WorldState.Time.TotalRounds = nextRound;
                    break;
                case "partofday":
                    WorldState.Time.PartOfDay = prop.Value.GetString()!;
                    break;
                case "day":
                    WorldState.Time.Day = prop.Value.GetInt32();
                    break;
            }
        }
    }


    private static bool TryPatchProperty(JsonElement patch, string name, out JsonElement value)
    {
        value = default;
        bool found = false;
        foreach (var property in patch.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                found = true;
            }
        return found;
    }

    private List<T> PatchByIndex<T>(List<T>? existing, JsonElement patches, JsonSerializerOptions options, bool allowDelete = true, bool retainDeleted = false)
    {
        var result = (existing ?? []).ToList();
        if (patches.ValueKind != JsonValueKind.Array) return result;
        var toDelete = new List<int>();

        foreach (var patchEl in patches.EnumerateArray() )
        {
            ValidateKeys<T>(patchEl, "patch");

            if (!TryPatchProperty(patchEl, "id", out var idEl))
            {
                // no id → new element, add to the end
                result.Add(JsonSerializer.Deserialize<T>(patchEl.GetRawText(), options)!);
                continue;
            }

            if (idEl.ValueKind != JsonValueKind.Number || !idEl.TryGetInt32(out int id) || id < 0)
            {
                Console.Error.WriteLine($"[AI ERROR] Invalid patch index in {typeof(T).Name}, ignoring.");
                continue;
            }
            bool deleted = TryPatchProperty(patchEl, "deleted", out var d) && d.ValueKind == JsonValueKind.True;

            if (deleted && retainDeleted)
            {
                if (id >= 0 && id < result.Count) result[id] = MergeInto(result[id], patchEl, options);
            }
            else if (deleted && allowDelete)
                toDelete.Add(id);
            else if (!deleted && id < result.Count)
                result[id] = MergeInto(result[id], patchEl, options);
            else if (!deleted)
                result.Add(JsonSerializer.Deserialize<T>(patchEl.GetRawText(), options)!);
        }

        foreach (int id in toDelete.OrderByDescending(x => x))
            if (id < result.Count) result.RemoveAt(id);

        return result;
    }

    private void ApplyImages(JsonElement el, JsonSerializerOptions options)
    {
        WorldState.Images ??= [];
        foreach (var itemEl in el.EnumerateArray())
        {
            var patch = JsonSerializer.Deserialize<WorldImage>(itemEl.GetRawText(), options);
            if (patch == null || string.IsNullOrEmpty(patch.Key)) continue;

            int idx = WorldState.Images.FindIndex(img => img.Key == patch.Key);
            if (idx < 0)
            {
                if (patch.Deleted != true)
                    WorldState.Images.Add(patch);
            }
            else if (patch.Deleted == true)
            {
                WorldState.Images.RemoveAt(idx);
            }
            else
            {
                WorldState.Images[idx] = MergeInto(WorldState.Images[idx], itemEl, options);
            }
        }
    }

    private void ApplyNarrativeUpdate(JsonElement el, JsonSerializerOptions options)
    {
        if (el.ValueKind != JsonValueKind.Object) return;
        WorldState.Narrative ??= new Narrative();
        foreach (var prop in el.EnumerateObject())
        {
            switch (prop.Name.ToLower())
            {
                case "world":
                    WorldState.Narrative.World = prop.Value.GetString()!;
                    break;
                case "currentarc":
                    WorldState.Narrative.CurrentArc = prop.Value.GetString()!;
                    break;
                case "style":
                    WorldState.Narrative.Style = prop.Value.GetString() ?? "";
                    break;
                case "plotthreads":
                    WorldState.Narrative.PlotThreads = PatchByIndex(WorldState.Narrative.PlotThreads, prop.Value, options);
                    break;
                case "npcs":
                    var npcs = JsonNode.Parse(prop.Value.GetRawText())!.AsArray();
                    if (!_fromSave)
                        foreach (var npc in npcs.OfType<JsonObject>())
                            foreach (var key in npc.Select(p => p.Key).Where(k => k.Equals("speechVoice", StringComparison.OrdinalIgnoreCase)).ToList()) npc.Remove(key);
                    WorldState.Narrative.Npcs = PatchByIndex(WorldState.Narrative.Npcs, JsonSerializer.SerializeToElement(npcs), options);
                    break;
                // The bestiary is only growing: the list from the GM is an addition to the known (species
                // entered by the engine after the murder, otherwise the “entire list” would be erased by the
                // next one). Knowledge is not forgotten - null does not clean.
                case "knownmonsters":
                    if (prop.Value.ValueKind == JsonValueKind.Array
                        && JsonSerializer.Deserialize<List<string>>(prop.Value.GetRawText(), options) is { } incoming)
                    {
                        var known = WorldState.Narrative.KnownMonsters ??= [];
                        foreach (var m in incoming)
                            if (!string.IsNullOrWhiteSpace(m) && !known.Contains(m, StringComparer.OrdinalIgnoreCase)) known.Add(m);
                    }
                    break;
            }
        }
    }

    private T MergeInto<T>(T existing, JsonElement patch, JsonSerializerOptions options)
    {
        var baseNode = JsonSerializer.SerializeToNode(existing, options)!.AsObject();
        List<StatusEffect>? effects = (existing as CellEntity)?.Effects;
        foreach (var prop in patch.EnumerateObject())
        {
            if ((prop.Name.Equals("aiInfo", StringComparison.OrdinalIgnoreCase)
                 || prop.Name.Equals("memory", StringComparison.OrdinalIgnoreCase)
                 || prop.Name.Equals("steps", StringComparison.OrdinalIgnoreCase))
                && prop.Value.ValueKind is JsonValueKind.Array or JsonValueKind.String)
            {
                // One line instead of an array (a common AI error) - appended in the same way as an array from it.
                var items = prop.Value.ValueKind == JsonValueKind.Array
                    ? prop.Value
                    : JsonDocument.Parse($"[{prop.Value.GetRawText()}]").RootElement;
                AppendList(baseNode, prop.Name, items);
            }
            else if (prop.Name.Equals("effects", StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == JsonValueKind.Array)
            {
                effects = PatchByIndex(effects, prop.Value, options);
                baseNode["effects"] = JsonSerializer.SerializeToNode(effects, options);
            }
            else if (prop.Name.Equals("triggers", StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == JsonValueKind.Object)
                MergeTriggers(baseNode, prop.Value);
            else
            {
                baseNode[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
                if (prop.Name.Equals("effects", StringComparison.OrdinalIgnoreCase)) effects = null;
            }
        }
        var merged = baseNode.Deserialize<T>(options)!;
        // Serializing the owner must not discard the identities of its unmodified effects.
        if (merged is CellEntity cell) cell.Effects = effects;
        if (existing is StatusEffect oldEffect && merged is StatusEffect newEffect
            && _effectRuntime.TryGetValue(oldEffect, out var runtime))
        {
            _effectRuntime.Add(newEffect, new EffectRuntime { LastRound = runtime.LastRound,
                ExpirationFired = runtime.ExpirationFired && oldEffect.ExpiresAtRound == newEffect.ExpiresAtRound });
        }
        if (existing is ScheduledEvent oldEvent && merged is ScheduledEvent newEvent
            && oldEvent.FireAtRound == newEvent.FireAtRound && _eventRuntime.TryGetValue(oldEvent, out var eventRuntime))
            _eventRuntime.Add(newEvent, new EventRuntime { Fired = eventRuntime.Fired });
        if (existing is Area oldArea && merged is Area newArea && _areaRuntime.TryGetValue(oldArea, out var areaRuntime))
            _areaRuntime.Add(newArea, areaRuntime);
        if (existing is LivingEntity oldEntity && merged is LivingEntity newEntity && _entityRuntime.TryGetValue(oldEntity, out var entityRuntime))
            _entityRuntime.Add(newEntity, entityRuntime);
        return merged;
    }

    // Patch triggers saves other owner triggers across connections.
    private static void MergeTriggers(JsonObject baseNode, JsonElement patch)
    {
        var existing = baseNode["triggers"] as JsonObject;
        bool isNew = existing == null;
        existing ??= new JsonObject(baseNode.Options);

        foreach (var t in patch.EnumerateObject())
            existing[t.Name] = t.Value.ValueKind == JsonValueKind.Null ? null : JsonNode.Parse(t.Value.GetRawText());

        if (!existing.Any(kv => kv.Value != null))
            baseNode["triggers"] = null;
        else if (isNew)
            baseNode["triggers"] = existing;
    }

    // “Journal” lists (aiInfo, npcs[i].memory, plotThreads[i].steps): the patch only adds lines, and does not
    // replace the list. Duplicates (AI repeated what was already written) are not added.
    private static void AppendList(JsonObject baseNode, string patchKey, JsonElement newItems)
    {
        string? existingKey = null;
        JsonArray existingArr = [];
        foreach (var kv in baseNode)
        {
            if (kv.Key.Equals(patchKey, StringComparison.OrdinalIgnoreCase))
            {
                existingKey = kv.Key;
                existingArr = kv.Value?.AsArray() ?? [];
                break;
            }
        }

        var merged = new JsonArray();
        var seen = new HashSet<string>();
        foreach (var item in existingArr)
        {
            merged.Add(item?.DeepClone());
            seen.Add(item?.ToJsonString() ?? "");
        }
        foreach (var item in newItems.EnumerateArray())
            if (seen.Add(item.GetRawText()))
                merged.Add(JsonNode.Parse(item.GetRawText()));

        if (existingKey != null)
            baseNode.Remove(existingKey);
        baseNode[existingKey ?? char.ToLowerInvariant(patchKey[0]) + patchKey[1..]] = merged;
    }

    private static List<int> DeserializeIntList(JsonElement el) =>
        JsonSerializer.Deserialize<List<int>>(el.GetRawText())!;

    private static readonly Dictionary<Type, HashSet<string>> _knownKeys = new();

    private static void ValidateKeys<T>(JsonElement el, string context, params string[] extraKeys)
    {
        if (el.ValueKind != JsonValueKind.Object) return;

        if (!_knownKeys.TryGetValue(typeof(T), out var keys))
        {
            keys = typeof(T).GetProperties()
                .Select(p => p.Name.ToLower())
                .ToHashSet();
            _knownKeys[typeof(T)] = keys;
        }

        foreach (var prop in el.EnumerateObject())
        {
            string name = prop.Name.ToLower();
            if (name is "id" or "deleted" || extraKeys.Contains(name)) continue; // patch-protocol fields, not model properties
            if (!keys.Contains(name))
                Console.Error.WriteLine($"[AI ERROR] Unknown field '{prop.Name}' in {context} ({typeof(T).Name}), ignoring.");
        }
    }

    // ── Multiple games (MY GAMES screen) Every game - Storage/game{N}.json (+ game{N}.explored.json), N =
    // 1..MaxGames; in the same folder as before - the request files MCP (roll_request.json, etc.) are located
    // next to the save, as they were. Active game - Storage/activeGame.txt. UI-tests (NAVIDND_TEST_WORLDSTATE)
    // is still written in worldState.json - his GameSession backups and restores.
    public const int MaxGames = 6;
    private static readonly string StorageDir = Path.Combine(AppConfig.DataRoot, "Storage");
    private static readonly string LegacySavePath = Path.Combine(StorageDir, "worldState.json");
    private static readonly string ActiveGamePath = Path.Combine(StorageDir, "activeGame.txt");
    private static bool TestMode => Environment.GetEnvironmentVariable("NAVIDND_TEST_WORLDSTATE") != null;

    public static int ActiveSlot { get; private set; } = 1;

    public static string SavePath => TestMode ? LegacySavePath : SlotPath(ActiveSlot);
    public static string SlotPath(int slot) => Path.Combine(StorageDir, $"game{slot}.json");
    public static bool SlotExists(int slot) => File.Exists(SlotPath(slot));
    // In UI tests, the game writes to its own file (SavePath), it does not take up the space of games - a new game is always possible.
    public static int? FreeSlot() => TestMode ? 1 : Enumerable.Range(1, MaxGames).Cast<int?>().FirstOrDefault(s => !SlotExists(s!.Value));

    // When starting the game: the previous only save (worldState.json) → game 1 (copy, the original remains),
    // the active game is from activeGame.txt; if it is not there, the last one in time.
    public static void InitGames()
    {
        Directory.CreateDirectory(StorageDir);
        if (TestMode) return;
        try
        {
            if (!Enumerable.Range(1, MaxGames).Any(SlotExists) && File.Exists(LegacySavePath))
            {
                File.Copy(LegacySavePath, SlotPath(1));
                string legacyExplored = ExploredCellsPath(LegacySavePath);
                if (File.Exists(legacyExplored)) File.Copy(legacyExplored, ExploredCellsPath(SlotPath(1)), true);
            }
            if (File.Exists(ActiveGamePath) && int.TryParse(File.ReadAllText(ActiveGamePath).Trim(), out int saved)
                && saved is >= 1 and <= MaxGames)
                ActiveSlot = saved;
            if (!SlotExists(ActiveSlot))
            {
                var latest = Enumerable.Range(1, MaxGames).Where(SlotExists)
                    .OrderByDescending(s => File.GetLastWriteTime(SlotPath(s))).Cast<int?>().FirstOrDefault();
                if (latest is int l) ActiveSlot = l;
            }
            if (!File.Exists(ActiveGamePath)) File.WriteAllText(ActiveGamePath, ActiveSlot.ToString());
        }
        catch { /*not critical - play game 1*/ }
    }

    public static void SetActiveSlot(int slot)
    {
        ActiveSlot = Math.Clamp(slot, 1, MaxGames);
        if (TestMode) return;
        try { File.WriteAllText(ActiveGamePath, ActiveSlot.ToString()); } catch { }
    }

    public static void DeleteSlot(int slot)
    {
        try
        {
            if (File.Exists(SlotPath(slot))) File.Delete(SlotPath(slot));
            string explored = ExploredCellsPath(SlotPath(slot));
            if (File.Exists(explored)) File.Delete(explored);
            string notes = PlayerNotes.PathFor(SlotPath(slot));
            if (File.Exists(notes)) File.Delete(notes);
        }
        catch { }
    }

    // Draft for CreateNewGame/FixHeroForNewGame/StartNewGame - MCP-tools read/write here until a new game is
    // created successfully. We do not touch combat SavePath until the final Save() in StartNewGame.
    public static readonly string DraftSavePath = Path.Combine(AppConfig.DataRoot, "Storage", "newGameState.json");

    private static readonly JsonSerializerOptions _saveOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static bool HasSave() => File.Exists(SavePath);

    public void Save() => SaveTo(SavePath);

    public void SaveDraft() => SaveTo(DraftSavePath);

    public static void DeleteDraft()
    {
        if (File.Exists(DraftSavePath)) File.Delete(DraftSavePath);
    }

    private void SaveTo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(WorldState, _saveOptions);
        RetryOnIo(() =>
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(json);
        });

        SaveExploredCellsTo(path);
    }

    // A separate file next to worldState.json/newGameState.json - ExploredCells is not part of WorldState (AI
    // never sees it and does not patch it), so we do not mix it with the JSON-diff protocol.
    private static string ExploredCellsPath(string worldStatePath) =>
        Path.Combine(Path.GetDirectoryName(worldStatePath)!, Path.GetFileNameWithoutExtension(worldStatePath) + ".explored.json");

    private void SaveExploredCellsTo(string path)
    {
        string exploredPath = ExploredCellsPath(path);
        if (!SaveExploredCells)
        {
            try { if (File.Exists(exploredPath)) File.Delete(exploredPath); } catch { }
            return;
        }

        var cells = ExploredCells.Select(c => new[] { c.X, c.Y }).ToList();
        string exploredJson = JsonSerializer.Serialize(cells);
        RetryOnIo(() => File.WriteAllText(exploredPath, exploredJson, Encoding.UTF8));
    }

    private void LoadExploredCellsFrom(string path)
    {
        ExploredCells.Clear();
        SeenObjects = null;
        if (!SaveExploredCells) return;

        string exploredPath = ExploredCellsPath(path);
        if (!File.Exists(exploredPath)) return;
        try
        {
            var cells = JsonSerializer.Deserialize<List<int[]>>(File.ReadAllText(exploredPath, Encoding.UTF8));
            if (cells == null) return;
            foreach (var cell in cells)
                if (cell.Length == 2) ExploredCells.Add((cell[0], cell[1]));
        }
        catch { /*damaged/incomplete - we start with an empty fog of war, do not drop the load*/ }
    }

    private static void RetryOnIo(Action action, int attempts = 5, int delayMs = 30)
    {
        for (int i = 0; i < attempts; i++)
        {
            try { action(); return; }
            catch (IOException) when (i < attempts - 1) { Thread.Sleep(delayMs); }
        }
    }

    public void LoadSave() => LoadFrom(SavePath);

    public void LoadDraft() => LoadFrom(DraftSavePath);

    // Public - used by tests (NAVIDND_TEST_WORLDSTATE) to substitute a test state instead of a combat one
    // worldState.json. The general loading path does not change. Label MCP plan_location (next to the save):
    // the GM built a location in the middle of the game - the map and place of the hero must be re-read from
    // the save before applying his answer (GameAiClient.ReloadPlannedLocation).
    public const string LocationPlannedFlag = "location_planned.flag";

    // Reread only the map and hero's position from the save (after plan_location in the MCP- process).
    public void ReloadMapFrom(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
        if (root == null) return;
        var map = root.FirstOrDefault(kv => kv.Key.Equals("map", StringComparison.OrdinalIgnoreCase)).Value;
        var hero = root.FirstOrDefault(kv => kv.Key.Equals("hero", StringComparison.OrdinalIgnoreCase)).Value as JsonObject;
        var pos = hero?.FirstOrDefault(kv => kv.Key.Equals("position", StringComparison.OrdinalIgnoreCase)).Value;
        if (map == null) return;
        GameWorld.ClearMap(WorldState.Map);
        var patch = new JsonObject { ["map"] = map.DeepClone() };
        if (pos != null) patch["hero"] = new JsonObject { ["position"] = pos.DeepClone() };
        ApplyUpdateWorldState(patch.ToJsonString());
        _entityColors.Clear();
    }

    public void LoadFrom(string path)
    {
        string json = File.ReadAllText(path, Encoding.UTF8);
        WorldState.Hero = null;
        WorldState.Narrative = null;
        WorldState.Map.Rooms = null;
        WorldState.Map.Doors = null;
        WorldState.Map.Area = null;
        WorldState.Map.Entities = null;
        WorldState.Map.Objects = null;
        WorldState.Map.TriggerCells = null;
        WorldState.Map.Chunks = null;
        WorldState.Map.Stairs = null;
        WorldState.Map.Furniture = null;
        WorldState.Images = null;
        WorldState.ActiveImageKey = null;
        WorldState.World = null;
        WorldState.PendingTravel = null;
        // The save is applied as a patch, and its elements do not have id - PatchByIndex would add them to
        // those already loaded: re-loading (Menu → Continue) doubled scheduledEvents.
        WorldState.ScheduledEvents = null;
        WorldState.Combat = null;
        WorldState?.History?.Clear();
        _entityColors.Clear();
        ApplySavedState(json);
        LoadExploredCellsFrom(path);
    }
}
