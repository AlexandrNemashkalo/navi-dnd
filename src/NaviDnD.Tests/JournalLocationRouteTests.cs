using System.Collections;
using System.Reflection;
using System.Text.Json;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class JournalLocationRouteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocationRoutesAreLazyCachedPerEntryAndRebuiltAfterMovement(bool isolated)
    {
        string id = "journal-routes-" + Guid.NewGuid().ToString("N");
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary)
            .GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var world = new WorldMap { Id = id, Width = 8, Height = 4,
            Biomes = Enumerable.Repeat("........", 4).ToList(),
            Roads = Enumerable.Repeat("11111111", 4).ToList(),
            Rivers = Enumerable.Repeat("00000000", 4).ToList(),
            Owners = Enumerable.Repeat("aaaabbbb", 4).ToList(),
            Kingdoms = [new() { Name = "West" }, new() { Name = "East" }],
            Chronicle = new() { Places = [
                new() { Name = "Mill", Type = WorldPlaceTypes.Village, X = 2, Y = 1 },
                new() { Name = "Tower", Type = WorldPlaceTypes.Ruins, X = 4, Y = 2 },
                new() { Name = "Harbor", Type = WorldPlaceTypes.Village, X = 6, Y = 1 }] } };
        worlds[id] = world;
        if (isolated)
        {
            world.Biomes = Enumerable.Repeat("." + WorldBiomes.Ocean + "......", 4).ToList();
            world.Roads = Enumerable.Repeat("00000000", 4).ToList();
        }
        try
        {
            var state = new WorldState { World = new() { Id = id, X = 0, Y = 0,
                Known = ["Mill", "Tower", "Harbor"], Visited = [] }, Hero = new() { Position = [1, 1] } };
            GameWorld.ForDisplay(state); // Resolve the shared-world link before checking read-only rendering.
            var display = new DisplayConfig();
            var journal = new JournalDisplay(state, display);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var buildEntries = typeof(JournalDisplay).GetMethod("BuildEntries", flags)!;
            var buildDetails = typeof(JournalDisplay).GetMethod("BuildDetailLines", flags)!;
            var entriesField = typeof(JournalDisplay).GetField("_entries", flags)!;

            List<object> Rebuild()
            {
                object entries = buildEntries.Invoke(journal, [JournalDisplay.Section.Locations])!;
                entriesField.SetValue(journal, entries);
                return ((IEnumerable)entries).Cast<object>().ToList();
            }
            static Lazy<List<(string text, List<int> color)>> Route(object entry) =>
                Assert.IsType<Lazy<List<(string text, List<int> color)>>>(entry.GetType().GetProperty("RouteDetails")!.GetValue(entry));
            string before = JsonSerializer.Serialize(state);
            string worldBefore = JsonSerializer.Serialize(world);
            var entries = Rebuild();
            Assert.Equal(3, entries.Count);
            var routes = entries.Select(Route).ToList();
            Assert.All(routes, route => Assert.False(route.IsValueCreated));
            buildDetails.Invoke(journal, [1, 60]);
            Assert.True(routes[1].IsValueCreated);
            Assert.False(routes[0].IsValueCreated);
            Assert.False(routes[2].IsValueCreated);
            var cached = routes[1].Value;
            Assert.NotEmpty(cached);
            Assert.Equal(isolated, cached.Any(line => line.text == L.T("No walking route from your current location.")));
            buildDetails.Invoke(journal, [1, 40]);
            Assert.Same(cached, routes[1].Value);
            Assert.False(routes[0].IsValueCreated);
            Assert.False(routes[2].IsValueCreated);
            Assert.Equal(before, JsonSerializer.Serialize(state));
            Assert.Equal(worldBefore, JsonSerializer.Serialize(world));

            state.World!.X = 7;
            state.World.Y = 3;
            string moved = JsonSerializer.Serialize(state);
            var rebuilt = Rebuild().Select(Route).ToList();
            Assert.Equal(routes.Count, rebuilt.Count);
            for (int i = 0; i < routes.Count; i++)
            {
                Assert.NotSame(routes[i], rebuilt[i]);
                Assert.False(rebuilt[i].IsValueCreated);
            }
            buildDetails.Invoke(journal, [1, 60]);
            Assert.True(rebuilt[1].IsValueCreated);
            Assert.NotSame(cached, rebuilt[1].Value);
            Assert.DoesNotContain(rebuilt[1].Value, line => line.text == L.T("No walking route from your current location."));
            Assert.False(rebuilt[0].IsValueCreated);
            Assert.False(rebuilt[2].IsValueCreated);
            Assert.Equal(moved, JsonSerializer.Serialize(state));
            Assert.Equal(worldBefore, JsonSerializer.Serialize(world));

            foreach (var pace in Enum.GetValues<TravelService.Pace>())
            {
                display.TravelPace = (int)pace;
                var pacedEntries = Rebuild();
                var mill = pacedEntries.Single(e => (string)e.GetType().GetProperty("Title")!.GetValue(e)! == "Mill");
                var route = NaviDnD.MapGen.Generators.WorldAtlas.Route(world, (7, 3), (2, 1))!;
                var details = Route(mill).Value;
                Assert.Contains(details, line => line.text == L.F("From here: {0} — {1}, by road {2:P0}",
                    L.T(NaviDnD.MapGen.Generators.WorldAtlas.Direction(-5, -2)),
                    TravelService.DaysLabel(TravelService.DaysAt(route.Days, pace)), route.RoadShare));
                Assert.Contains(details, line => line.text == L.F("Through: {0}", "East → West"));
                Assert.DoesNotContain(details, line => line.text.Contains("travel,", StringComparison.Ordinal));
            }

            // Arriving at the cell is not another day of travel, even before entering its local scene.
            state.World.X = 2;
            state.World.Y = 1;
            var local = Rebuild().Single(e => (string)e.GetType().GetProperty("Title")!.GetValue(e)! == "Mill");
            Assert.Equal(L.F("Here: {0} — no travel", "Mill"), Route(local).Value.Single(line => line.text.Length > 0).text);
        }
        finally { worlds.Remove(id); }
    }
}
