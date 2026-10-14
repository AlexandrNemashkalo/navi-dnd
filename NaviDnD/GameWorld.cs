using System.Text;
using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD;

// World of a specific game: world file (WorldLibrary) - geometry (terrain, rivers, kingdoms, roads, place
// settings) and chronicle (lore and places of the GM, common to all games in the world); game (GameWorldLink) -
// what its hero knows and where he is. The map and AI see the overlay: capitals - always, other places in the
// chronicle - only known to the hero, everything to the GM.
public static class GameWorld
{
    private static (GameWorldLink link, int version, bool hidden, WorldMap geo, WorldMap result)? _cacheHidden, _cacheVisible;
    private static readonly GameWorldLink _noGame = new();

    // Is there a tactical map (location): no - the hero is on the road or a scene without a map, the “Map” tab shows the world.
    public static bool HasLocation(WorldState ws) =>
        ws.Map.Chunks is { Count: > 0 } || ws.Map.Rooms is { Count: > 0 } || ws.Map.Area is { Count: > 0 } || ws.Map.Doors is { Count: > 0 };

    // A game started before the world map (there is no WorldState.World): attached to the active world, the
    // hero is in the capital of the first kingdom (known to everyone). Without this, the old game does not have
    // a hero on the world map and path.
    public static void EnsureLink(WorldState ws)
    {
        if (ws.World != null || ws.Hero == null) return;
        var geo = WorldLibrary.Current;
        var link = new GameWorldLink { Id = geo.Id };
        if (geo.Kingdoms.Count > 0 && geo.Kingdoms[0].Capital is int cap && cap >= 0 && cap < geo.Places.Count)
        {
            var composed = Compose(geo, link, includeHidden: false);
            var p = composed.Places.FirstOrDefault(x => x.X == geo.Places[cap].X && x.Y == geo.Places[cap].Y);
            link.X = geo.Places[cap].X;
            link.Y = geo.Places[cap].Y;
            link.Place = p?.Name;
            if (p != null) link.Visited.Add(p.Name);
        }
        ws.World = link;
    }

    public static WorldMap? Geo(WorldState ws) => ws.World is { } link ? WorldLibrary.Get(link.Id) : null;

    // World for the hero map (known places only); Old saves without a game world have an active world, only capitals.
    public static WorldMap ForDisplay(WorldState ws) =>
        ws.World is { } link && WorldLibrary.Get(link.Id) is { } geo ? Compose(geo, link, includeHidden: false)
            : Compose(WorldLibrary.Current, _noGame, includeHidden: false);

    // The world for the GM (all places in the chronicle; those unknown to the hero are marked Hidden; secrets dmNotes).
    public static WorldMap? ForMaster(WorldState ws) =>
        ws.World is { } link && WorldLibrary.Get(link.Id) is { } geo ? Compose(geo, link, includeHidden: true) : null;

    // Hero’s cell on the world map: its own position or its place; no – null.
    public static (int x, int y)? HeroTile(WorldState ws)
    {
        if (ws.World is not { } link) return null;
        if (link.X is int x && link.Y is int y) return (x, y);
        if (link.Place is { } p && ForMaster(ws) is { } w && WorldAtlas.FindPlace(w, p) is { } pl) return (pl.X, pl.Y);
        return null;
    }

    public static WorldMap Compose(WorldMap geo, GameWorldLink link, bool includeHidden)
    {
        var cache = includeHidden ? _cacheHidden : _cacheVisible;
        if (cache is { } c && ReferenceEquals(c.link, link) && c.version == link.Version && ReferenceEquals(c.geo, geo)) return c.result;

        var chron = geo.Chronicle;
        var places = new List<WorldPlace>();
        var trailCells = new List<int>();
        foreach (var gp in chron.Places)
        {
            bool known = gp.Type == WorldPlaceTypes.Capital || link.Knows(gp.Name);
            if (!includeHidden && !known) continue;
            places.Add(new WorldPlace
            {
                Name = gp.Name, Type = gp.Type, X = gp.X, Y = gp.Y, Kingdom = geo.OwnerAt(gp.X, gp.Y),
                Description = gp.Description, DmNotes = includeHidden ? gp.DmNotes : null, Hidden = !known,
            });
            if (known && gp.Trail != null) trailCells.AddRange(gp.Trail);
        }
        var kingdoms = new List<WorldKingdom>();
        for (int k = 0; k < geo.Kingdoms.Count; k++)
        {
            var gk = geo.Kingdoms[k];
            var lk = chron.Kingdoms.FirstOrDefault(x => x.Id == k);
            int cap = -1;
            if (gk.Capital >= 0 && gk.Capital < geo.Places.Count)
            {
                // Capital - known to everyone: from the chronicle (name from the GM) or the name of the generator.
                var slot = geo.Places[gk.Capital];
                cap = places.FindIndex(p => p.X == slot.X && p.Y == slot.Y);
                if (cap < 0)
                {
                    places.Add(new WorldPlace { Name = slot.Name, Type = WorldPlaceTypes.Capital, X = slot.X, Y = slot.Y, Kingdom = k });
                    cap = places.Count - 1;
                }
            }
            kingdoms.Add(new WorldKingdom
            {
                Name = lk?.Name is { Length: > 0 } n ? n : gk.Name, Color = gk.Color, Capital = cap,
                Description = lk?.Description, DmNotes = includeHidden ? lk?.DmNotes : null,
            });
        }
        var roads = geo.Roads;
        if (trailCells.Count > 0)
        {
            var rows = geo.Roads.Select(r => r.ToCharArray()).ToArray();
            for (int i = 0; i + 1 < trailCells.Count; i += 2)
                if (geo.InBounds(trailCells[i], trailCells[i + 1]) && rows[trailCells[i + 1]][trailCells[i]] == '0')
                    rows[trailCells[i + 1]][trailCells[i]] = '1';
            roads = rows.Select(r => new string(r)).ToList();
        }
        var result = new WorldMap
        {
            Id = geo.Id, Name = chron.Name is { Length: > 0 } wn ? wn : geo.Name, Seed = geo.Seed, Size = geo.Size,
            Width = geo.Width, Height = geo.Height, Description = chron.Description, DmNotes = includeHidden ? chron.DmNotes : null,
            Biomes = geo.Biomes, Heights = geo.Heights, Rivers = geo.Rivers, Roads = roads, Owners = geo.Owners,
            Places = places, Kingdoms = kingdoms, Features = geo.Features,
        };
        var entry = (link, link.Version, includeHidden, geo, result);
        if (includeHidden) _cacheHidden = entry; else _cacheVisible = entry;
        return result;
    }

    // Patch “world” from the GM: name/description/dmNotes of the world, kingdoms[] (by id; capital - the name
    // of the capital), places[] (by name: new - to the chronicle, there is - to add; hidden:true - the hero
    // does not know, false/new - knows; deleted - remove from chronicles; without x/y - the code will select
    // the cell next to the hero), place - where the hero is now. The chronicle is in the world file (other
    // games of this world also see it), the hero’s knowledge is in the game. To the places that have become
    // famous there is a path to the road.
    public static void Apply(WorldState ws, JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object) return;
        var link = ws.World ??= new GameWorldLink { Id = WorldLibrary.Current.Id };
        var geo = WorldLibrary.Get(link.Id) ?? WorldLibrary.Current;
        var chron = geo.Chronicle;
        bool chronChanged = ApplyChronicle(geo, patch);

        if (Get(patch, "places") is { ValueKind: JsonValueKind.Array } ps)
            foreach (var pe in ps.EnumerateArray())
            {
                if (pe.ValueKind != JsonValueKind.Object || Str(pe, "name") is not { Length: > 0 } name) continue;
                var gp = chron.Places.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (Get(pe, "deleted") is { ValueKind: JsonValueKind.True })
                {
                    if (gp != null) { chron.Places.Remove(gp); chronChanged = true; }
                    link.Known.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                    continue;
                }
                bool isNew = gp == null;
                if (isNew)
                {
                    string type = (Str(pe, "type") ?? WorldPlaceTypes.Village).ToLowerInvariant();
                    gp = new GamePlace { Name = name, Type = type };
                    if (Int(pe, "x") is int x && Int(pe, "y") is int y && geo.InBounds(x, y))
                    {
                        gp.X = x; gp.Y = y;
                        gp.Slot = Int(pe, "slot") ?? -1;
                    }
                    else if (PlaceNear(ws, geo, link, type) is { } spot)
                    {
                        gp.X = spot.x; gp.Y = spot.y; gp.Slot = spot.slot;
                    }
                    else continue;
                    chron.Places.Add(gp);
                }
                else if (Str(pe, "type") is { Length: > 0 } t) gp!.Type = t.ToLowerInvariant();
                if (Str(pe, "description") is { } d) gp!.Description = d;
                if (Str(pe, "dmNotes") is { } dm) gp!.DmNotes = dm;
                chronChanged = true;
                // Does the hero know: hidden:true - no; hidden:false or a new place without hidden - yes.
                bool? hidden = Get(pe, "hidden") is { ValueKind: JsonValueKind.True or JsonValueKind.False } h ? h.GetBoolean() : null;
                if (hidden == true) link.Known.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                else if ((hidden == false || isNew) && !link.Knows(gp!.Name)) Reveal(geo, link, gp!);
            }

        if (Str(patch, "place") is { Length: > 0 } here)
        {
            var all = Compose(geo, link, includeHidden: true);
            if (WorldAtlas.FindPlace(all, here) is { } p)
            {
                link.Place = p.Name;
                link.X = p.X; link.Y = p.Y;
                if (chron.Places.FirstOrDefault(c => c.Name == p.Name) is { } gp && !link.Knows(gp.Name)) Reveal(geo, link, gp);
                if (!link.Visited.Contains(p.Name)) link.Visited.Add(p.Name);
            }
        }
        link.Version++;
        if (chronChanged) WorldLibrary.Save(geo);
    }

    // The concept of the world in the chronicle: name/description/dmNotes of the world, kingdoms[] (by id/K-
    // number; capital - the name of the capital). From the GM’s answer (patch world) and from CreateWorld (new
    // game questionnaire, step “World”). true - the chronicle has changed.
    public static bool ApplyChronicle(WorldMap geo, JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object) return false;
        var chron = geo.Chronicle;
        bool chronChanged = false;
        if (Str(patch, "name") is { Length: > 0 } wn) { chron.Name = wn; chronChanged = true; }
        if (Str(patch, "description") is { } wd) { chron.Description = wd; chronChanged = true; }
        if (Str(patch, "dmNotes") is { } wdm) { chron.DmNotes = wdm; chronChanged = true; }
        if (Get(patch, "kingdoms") is { ValueKind: JsonValueKind.Array } ks)
            foreach (var k in ks.EnumerateArray())
            {
                if (k.ValueKind != JsonValueKind.Object || Get(k, "id") is not { } idEl) continue;
                int id = idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32()
                    : idEl.GetString() is { } sid && int.TryParse(sid.TrimStart('K', 'k'), out int pid) ? pid : -1;
                if (id < 0 || id >= geo.Kingdoms.Count) continue;
                var gk = chron.Kingdoms.FirstOrDefault(x => x.Id == id);
                if (gk == null) { gk = new GameKingdom { Id = id, Name = geo.Kingdoms[id].Name }; chron.Kingdoms.Add(gk); }
                if (Str(k, "name") is { Length: > 0 } kn) gk.Name = kn;
                if (Str(k, "description") is { } kd) gk.Description = kd;
                if (Str(k, "dmNotes") is { } kdm) gk.DmNotes = kdm;
                if (Str(k, "capital") is { Length: > 0 } capName && geo.Kingdoms[id].Capital is int cs && cs >= 0 && cs < geo.Places.Count)
                {
                    var cap = chron.Places.FirstOrDefault(p => p.Slot == cs);
                    if (cap == null)
                        chron.Places.Add(cap = new GamePlace { Slot = cs, Type = WorldPlaceTypes.Capital, X = geo.Places[cs].X, Y = geo.Places[cs].Y });
                    cap.Name = capName;
                    if (Str(k, "capitalDescription") is { } cd) cap.Description = cd;
                }
                chronChanged = true;
            }

        return chronChanged;
    }

    // The hero learned about the place: in the famous, path to the nearest road (one per place - common to all games in the world).
    public static void Reveal(WorldMap geo, GameWorldLink link, GamePlace gp)
    {
        if (!link.Knows(gp.Name)) link.Known.Add(gp.Name);
        link.Version++;
        if (gp.Trail == null && gp.Type != WorldPlaceTypes.Capital
            && WorldAtlas.TrailFrom(Compose(geo, link, includeHidden: false), gp.X, gp.Y) is { Count: > 0 } path)
        {
            gp.Trail = path.SelectMany(c => new[] { c.x, c.y }).ToList();
            link.Version++;
        }
    }

    // Cell for a place without coordinates: next to (≈2 days) the hero.
    private static (int x, int y, int slot)? PlaceNear(WorldState ws, WorldMap geo, GameWorldLink link, string type)
    {
        var all = Compose(geo, link, includeHidden: true);
        WorldPlace? near = link.Place is { } here ? WorldAtlas.FindPlace(all, here) : null;
        if (near == null && link.X is int hx && link.Y is int hy) near = new WorldPlace { Name = "", X = hx, Y = hy };
        return WorldAtlas.FindSpot(geo, new WorldAtlas.SpotRequest(type, near, null, near != null ? 2 : null, null),
            all.Places.Select(p => (p.X, p.Y)).ToList(), geo.Chronicle.Places.Select(p => p.Slot).Where(s => s >= 0).ToHashSet());
    }

    // ── Tactical map on the world map ──

    // World cell → location terrain: theme of open blocks according to the biome, river (if it flows through
    // the cell - the axis is along the neighboring river cells, width in size), sides with the sea/lake (north
    // of the world - top of the location).
    public static LocationGrower.WorldSite SiteAt(WorldMap geo, int x, int y)
    {
        string theme = geo.BiomeAt(x, y) switch
        {
            WorldBiomes.Forest or WorldBiomes.DeepForest => "forest",
            WorldBiomes.SnowForest => "taiga",
            WorldBiomes.Hills => "hills",
            WorldBiomes.Mountains or WorldBiomes.Peaks => "mountain",
            WorldBiomes.Swamp => "swamp",
            WorldBiomes.Desert => "desert",
            WorldBiomes.Tundra or WorldBiomes.Snow => "snow",
            _ => "plains",
        };
        bool? riverH = null;
        int width = 0;
        int level = geo.RiverAt(x, y);
        if (level > 0)
        {
            int ew = (geo.RiverAt(x - 1, y) > 0 ? 1 : 0) + (geo.RiverAt(x + 1, y) > 0 ? 1 : 0);
            int ns = (geo.RiverAt(x, y - 1) > 0 ? 1 : 0) + (geo.RiverAt(x, y + 1) > 0 ? 1 : 0);
            riverH = ew >= ns;
            width = level <= 3 ? 1 : level <= 6 ? 2 : 3;
        }
        var water = new HashSet<ChunkSide>();
        foreach (var (dx, dy, side) in new[] { (0, -1, ChunkSide.Top), (0, 1, ChunkSide.Bottom), (-1, 0, ChunkSide.Left), (1, 0, ChunkSide.Right) })
            if (geo.InBounds(x + dx, y + dy) && WorldBiomes.IsWater(geo.BiomeAt(x + dx, y + dy))) water.Add(side);
        return new LocationGrower.WorldSite(theme, riverH, width, water);
    }

    // ── Locations of places: “everything is as it was” when returning (common for games in the world) ──

    private static readonly JsonSerializerOptions _mapJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // The hero leaves the location (goes to the world map): the location is saved for the place where he is (if
    // he is in the chronicle location), the tactical map is cleared (in place - its object is kept by drawing
    // and movement).
    public static void LeaveLocation(WorldState ws)
    {
        if (!HasLocation(ws)) return;
        // Capital from the generator (not yet in the chronicle) - add it so that the location has someone to save.
        if (Geo(ws) is { } g0 && ws.World?.Place is { } h0 && !g0.Chronicle.Places.Any(p => p.Name.Equals(h0, StringComparison.OrdinalIgnoreCase)))
        {
            int slot = g0.Kingdoms.Select(k => k.Capital).FirstOrDefault(c => c >= 0 && c < g0.Places.Count && g0.Places[c].Name.Equals(h0, StringComparison.OrdinalIgnoreCase), -1);
            if (slot >= 0)
                g0.Chronicle.Places.Add(new GamePlace { Name = g0.Places[slot].Name, Type = WorldPlaceTypes.Capital, X = g0.Places[slot].X, Y = g0.Places[slot].Y, Slot = slot });
        }
        if (Geo(ws) is { } geo && ws.World?.Place is { } here
            && geo.Chronicle.Places.FirstOrDefault(p => p.Name.Equals(here, StringComparison.OrdinalIgnoreCase)) is { } gp)
        {
            try
            {
                string dir = WorldLibrary.LocationsDir(geo.Id);
                Directory.CreateDirectory(dir);
                gp.Location ??= $"{Slug(gp.Name)}-{Guid.NewGuid().ToString("N")[..6]}.json";
                File.WriteAllText(Path.Combine(dir, gp.Location), JsonSerializer.Serialize(ws.Map, _mapJson), Encoding.UTF8);
                WorldLibrary.Save(geo);
            }
            catch { /*was not saved - upon return the GM will build it again*/ }
        }
        ClearMap(ws.Map);
        if (ws.Hero != null) ws.Hero.Position = [1, 1];   // stub: there is no tactical map, rendering is waiting for position
        ws.Combat = null;
    }

    public static void ClearMap(MapConfig m)
    {
        m.Rooms = []; m.Doors = []; m.Area = []; m.Entities = []; m.Objects = [];
        m.Chunks = null; m.Stairs = null; m.Furniture = null; m.TriggerCells = null;
        m.Cols = 20; m.Rows = 15;
    }

    // Saved location location - JSON maps (patch "map" for Storage), if available.
    public static string? SavedLocationJson(WorldMap geo, string placeName)
    {
        var gp = geo.Chronicle.Places.FirstOrDefault(p => p.Name.Equals(placeName, StringComparison.OrdinalIgnoreCase));
        if (gp?.Location is not { } file) return null;
        try
        {
            string path = Path.Combine(WorldLibrary.LocationsDir(geo.Id), file);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch { return null; }
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (char ch in name.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        return sb.ToString().Trim('-');
    }

    // ── JSON ──
    private static JsonElement? Get(JsonElement e, string key)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject())
            if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string? Str(JsonElement e, string key) => Get(e, key) is { ValueKind: JsonValueKind.String } v ? v.GetString()?.Trim() : null;

    private static int? Int(JsonElement e, string key) => Get(e, key) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out int i) ? i : null;
}
