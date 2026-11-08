using System.Text;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD;

// Traveling on the world map (D&D 5e, “Journey”): route on foot through the cells of the world (cell ≈ a
// quarter of a day on the road, WorldAtlas.CellDays), pace (fast - further, but you notice worse; slow -
// shorter, but you can go secretly), rest stops with a long rest, random encounters in the danger of the region
// (the road in the kingdom is calm, wilderness - more dangerous; hostile - less peaceful and finds), off-road
// navigation (path, time, encounter rolls) is considered by the code; the GM describes the road with one call
// and plays out the encounter (GameAiClient.Travel).).
public static class TravelService
{
    public enum Pace { Slow, Normal, Fast }

    public static string PaceName(Pace p) => p switch { Pace.Fast => "fast", Pace.Slow => "slow", _ => "regular" };

    // Travel time for the screen (legend): “2 days”, “1.5 days” - in the interface language; report to the GM - in English.
    public static string DaysLabel(double d) =>
        d % 1 != 0 ? L.F("{0:0.#} days", d) : $"{(int)d} {L.Plural((int)d, "day", "days")}";

    // How much travel in the same time: fast - 30 miles a day instead of 24, slow - 18.
    private static double Speed(Pace p) => p switch { Pace.Fast => 1.25, Pace.Slow => 0.75, _ => 1.0 };

    // Food on the way: what food is in the inventory (id, quantity) - the GM writes it off as a patch; not
    // enough - fodder obtained in excess of what is needed is added to the inventory (otherwise the supply of
    // obtained food was lost).
    private static readonly System.Text.RegularExpressions.Regex FoodName = new(
        "рацион|паёк|паек|провизи|еда|пищ|вялен|сухар|хлеб|сыр|мяс|рыб|ягод|орех|фрукт|яблок|похлёбк|похлебк|дичь|\\b(?:rations?|provisions?|food|jerky|bread|cheese|meat|fish|berries|nuts?|fruit|apples?|stew|game meat)\\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string FoodLine(WorldState ws, int days)
    {
        var inv = ws.Hero?.Inventory ?? [];
        var food = inv.Select((it, i) => (it, i))
            .Where(t => t.it.Deleted != true && t.it.Name is { } n && FoodName.IsMatch(n))
            .Select(t => $"{t.it.Name} (id {t.i}) ×{t.it.Quantity ?? 1}").ToList();
        // The food included in the set (“Traveller’s Set: ... rations 10 ...”) is also food: it is taken out as a separate item.
        var packed = inv.Select((it, i) => (it, i))
            .Where(t => t.it.Deleted != true && !FoodName.IsMatch(t.it.Name ?? "") && t.it.Description is { } d && FoodName.IsMatch(d))
            .Select(t => $"in «{t.it.Name}» (id {t.i}): {t.it.Description}").ToList();
        string have = food.Count > 0 ? string.Join(", ", food) : "no separate food items";
        string inKits = packed.Count > 0 ? $" Food included in packs — {string.Join("; ", packed)}: extract it into separate items with quantity (remove it from pack descriptions), then subtract what was eaten." : "";
        return $"Food: traveling for {days} {DayWord(days)} — one daily ration per day. Inventory: {have}.{inKits} "
               + "Deduct consumed food with a hero.inventory patch ({\"id\":N,\"quantity\":remaining}; deleted when depleted). If short, forage: ONE roll_dice Survival DC 10 for the entire shortage "
               + "(success covers the shortage; failure leaves half the missing days underfed, with exhaustion by the rules). Add surplus foraged food to inventory with quantity and a localized name; do not lose it. "
               + "Do not claim existing food is absent.";
    }

    public sealed record Plan((int x, int y) From, (int x, int y) To, string? TargetPlace, WorldAtlas.RouteInfo Route)
    {
        public double Days(Pace pace) => DaysAt(Route.Days, pace);
    }

    public static double DaysAt(double normalDays, Pace pace) => Math.Max(0.5, Math.Round(normalDays / Speed(pace) * 2) / 2);

    // The hero’s route to the world square (or a place on it).
    public static Plan? PlanTo(WorldState ws, (int x, int y) target)
    {
        if (GameWorld.HeroTile(ws) is not { } from || GameWorld.ForMaster(ws) is not { } world) return null;
        if (from == target) return null;
        var route = WorldAtlas.Route(world, from, target);
        if (route == null) return null;
        var place = world.Places.FirstOrDefault(p => p.X == target.x && p.Y == target.y && (!p.Hidden));
        return new Plan(from, target, place?.Name, route);
    }

    // Complete the route: the hero walks by day, every day is an encounter roll; fell out - the path is
    // interrupted there. Moves the hero and time, takes him away from the location; returns a summary for the
    // GM.
    public static string Go(WorldState ws, Plan plan, Pace pace, Random rng)
    {
        var world = GameWorld.ForMaster(ws)!;
        var link = ws.World!;
        string fromName = link.Place ?? WorldAtlas.TileName(world, plan.From.x, plan.From.y);
        GameWorld.LeaveLocation(ws);
        // Events scheduled for the round of the scene that the hero left: the count of rounds on the way is
        // reset to zero, days have passed - without cancellation they would then “work” out of place.
        var dropped = (ws.ScheduledEvents ?? []).Where(e => e.Deleted != true).Select(e => e.Name).ToList();
        ws.ScheduledEvents = [];

        // Cells of the journey by day (a quarter of a day per cell on the road, longer off the road; the pace changes with the daily transition).
        var path = plan.Route.Path;
        double perDay = 1.0 * Speed(pace);
        // Day of the cell - according to the accumulated travel time (as in the route assessment): how many
        // days were shown when choosing a goal, the hero walks for as long.
        var days = new List<List<(int x, int y)>>();
        double acc = 0;
        for (int i = 1; i < path.Count; i++)
        {
            double step = Math.Min(WorldAtlas.StepDays(world, path[i].x, path[i].y), 6 * WorldAtlas.CellDays);
            if (i == path.Count - 1) step = Math.Min(step, 1.5 * WorldAtlas.CellDays);   // as in WorldAtlas.Route
            int day = Math.Max(0, (int)Math.Ceiling((acc + step) / perDay - 1e-9) - 1);
            acc += step;
            while (days.Count <= day) days.Add([]);
            days[day].Add(path[i]);
        }
        days.RemoveAll(d => d.Count == 0);

        // Meetings: chance per day according to the danger of the region, type - more often a peaceful meeting or a discovery, less often enemies.
        int stopDay = -1;
        string? encounter = null;
        for (int d = 0; d < days.Count && stopDay < 0; d++)
        {
            double chance = days[d].Max(t => Danger(world, t.x, t.y));
            if (pace == Pace.Slow) chance *= 0.8;
            if (rng.NextDouble() >= chance) continue;
            double r = rng.NextDouble();
            double hostile = pace == Pace.Slow ? 0.18 : 0.25;
            encounter = r < hostile ? "hostile (ambush, predator, bandits appropriate to hero strength; can be avoided or negotiated)"
                : r < hostile + 0.45 ? "peaceful (travelers, merchant, patrol, hermit; conversation, rumors, request)"
                : "discovery (trail, ruins, cache, sign, unusual place; a hook that may add a map location)";
            stopDay = d;
        }

        int walked = stopDay >= 0 ? stopDay + 1 : days.Count;
        var walkedTiles = days.Take(walked).SelectMany(x => x).ToList();
        var end = walkedTiles.Count > 0 ? walkedTiles[^1] : plan.From;
        bool arrived = stopDay < 0;

        // Time: on the road - in the morning (in the evening/night - from the next morning), day of travel - until the evening, halt, night.
        int startDay = ws.Time.Day + (ws.Time.PartOfDay is PartsOfDay.Evening or PartsOfDay.Night ? 1 : 0);
        ws.Time.Day = startDay + walked - 1;
        ws.Time.PartOfDay = PartsOfDay.Evening;
        ws.Time.TotalRounds = 1;

        link.X = end.x; link.Y = end.y;
        var arrivedPlace = arrived && plan.TargetPlace != null ? WorldAtlas.FindPlace(world, plan.TargetPlace) : null;
        link.Place = arrivedPlace?.Name;
        if (arrivedPlace != null && !link.Visited.Contains(arrivedPlace.Name)) link.Visited.Add(arrivedPlace.Name);
        link.Version++;

        // Summary for the GM.
        var sb = new StringBuilder();
        sb.AppendLine("## TRAVEL (the engine already moved the hero on the world map and advanced time; do not change time or world.place)");
        string toName = plan.TargetPlace ?? WorldAtlas.TileName(world, plan.To.x, plan.To.y);
        sb.AppendLine($"From: {fromName} → to: {toName}, {WorldAtlas.Direction(plan.To.x - plan.From.x, plan.To.y - plan.From.y)} (this is the travel direction). Pace: {PaceName(pace)}{(pace == Pace.Fast ? " (−5 passive Perception while traveling)" : pace == Pace.Slow ? " (stealth is possible)" : "")}.");
        double roadShare = walkedTiles.Count == 0 ? 1 : walkedTiles.Count(t => world.RoadAt(t.x, t.y) > 0) / (double)walkedTiles.Count;
        sb.AppendLine($"Traveled: {walked} {DayWord(walked)} of {days.Count}, road share {roadShare:P0}; terrain: {Terrains(world, walkedTiles)}.");
        var rivers = Rivers(world, walkedTiles);
        if (rivers.Length > 0) sb.AppendLine($"Rivers: {rivers}.");
        var kingdoms = walkedTiles.Select(t => world.OwnerAt(t.x, t.y)).Distinct().Select(o => o >= 0 && o < world.Kingdoms.Count ? world.Kingdoms[o].Name : "unclaimed lands").ToList();
        sb.AppendLine($"Lands: {string.Join(" → ", kingdoms.Distinct())}.");
        var near = world.Places.Where(p => walkedTiles.Any(t => Math.Abs(t.x - p.X) <= 2 && Math.Abs(t.y - p.Y) <= 2)
                && p.Name != plan.TargetPlace && p.Name != fromName)
            .Select(p => $"{p.Name} ({WorldPlaceTypes.Label(p.Type)}{(p.Hidden ? ", unknown to the hero" : "")})").ToList();
        if (near.Count > 0) sb.AppendLine($"Near the road: {string.Join(", ", near.Take(6))}.");
        int nights = Math.Max(walked - 1, 0);
        if (dropped.Count > 0)
            sb.AppendLine($"The engine cancelled delayed events from the previous scene (the hero left and days passed): {string.Join("; ", dropped)} — if relevant, account for their outcome in the narrative.");
        sb.AppendLine($"Rest stops: nights on the road — {nights}; on arrival/at camp, take a long_rest unless interrupted.");
        sb.AppendLine(FoodLine(ws, walked));
        if (roadShare < 0.5) sb.AppendLine("More than half the route is off road: navigation check via roll_dice Survival DC 10 (forest/hills) to 15 (mountains/swamp/desert). Failure: lost, +1 day (time.day+1); narrate the detour.");
        if (encounter != null)
            sb.AppendLine($"ENCOUNTER on day {walked}, {WorldBiomes.Get(world.BiomeAt(end.x, end.y)).Name.ToLowerInvariant()} ({WorldAtlas.Surroundings(world, end.x, end.y, named: true)}): {encounter}. Travel stops here; resolve the encounter according to this place and kingdom. If tactics are needed (combat, searching ruins), call plan_location (the engine supplies the cell terrain); otherwise narrate it. The player may then resume travel.");
        else if (arrivedPlace != null)
        {
            bool saved = GameWorld.Geo(ws) is { } geo && GameWorld.SavedLocationJson(geo, arrivedPlace.Name) != null;
            sb.AppendLine($"Arrival: {arrivedPlace.Name} — {WorldPlaceTypes.Label(arrivedPlace.Type)}.{(arrivedPlace.Description is { Length: > 0 } ad ? " " + ad : "")}"
                          + (saved ? " This place was visited before: the engine restored its saved location and placed the hero outside the entrance. Narrate entering or finding lodging, and move the hero with a hero.position patch." : " No tactical map: narrate the scene; if needed for combat, a dungeon, or exploration, call plan_location."));
        }
        else sb.AppendLine($"Arrival: the hero is at the destination — {WorldBiomes.Get(world.BiomeAt(end.x, end.y)).Name.ToLowerInvariant()}, {WorldAtlas.Surroundings(world, end.x, end.y, named: true)}. Where no map location exists, discover something through add_place or simply make camp.");
        sb.AppendLine("Respond with a brief travel account (2–4 history entries, not a day-by-day recap); patch food and rest.");
        return sb.ToString();
    }

    // Danger of the edge: chance of encounter per day of travel.
    private static double Danger(WorldMap w, int x, int y)
    {
        bool road = w.RoadAt(x, y) > 0, owned = w.OwnerAt(x, y) >= 0;
        double d = road && owned ? 0.05 : owned ? 0.09 : 0.14;
        if (w.BiomeAt(x, y) is WorldBiomes.Swamp or WorldBiomes.Mountains or WorldBiomes.DeepForest or WorldBiomes.Peaks) d += 0.03;
        return d;
    }

    private static string Terrains(WorldMap w, List<(int x, int y)> tiles) =>
        tiles.Count == 0 ? "—" : string.Join(", ", tiles.GroupBy(t => WorldBiomes.Get(w.BiomeAt(t.x, t.y)).Name.ToLowerInvariant())
            .OrderByDescending(g => g.Count()).Take(4).Select(g => g.Key));

    private static string Rivers(WorldMap w, List<(int x, int y)> tiles)
    {
        int bridges = tiles.Count(t => w.RiverAt(t.x, t.y) > 0 && w.RoadAt(t.x, t.y) > 0);
        int fords = tiles.Count(t => w.RiverAt(t.x, t.y) > 0 && w.RoadAt(t.x, t.y) == 0);
        var parts = new List<string>();
        if (bridges > 0) parts.Add($"bridges: {bridges}");
        if (fords > 0) parts.Add($"fords: {fords} (cross with Athletics DC 10 or detour)");
        return string.Join(", ", parts);
    }

    private static string DayWord(int d) =>
        d == 1 ? "day" : "days";
}
