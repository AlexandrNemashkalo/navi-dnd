using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

public class WorldQueryMemoryTests
{
    [Fact]
    public void WorldQueryRestoresFullRulesWithoutReturningHundredsOfPlaces()
    {
        string root = Path.Combine(Path.GetTempPath(), "navi-world-query-" + Guid.NewGuid().ToString("N"));
        string worlds = Path.Combine(root, "Worlds"), folder = Path.Combine(worlds, "test");
        Directory.CreateDirectory(folder);
        string statePath = Path.Combine(root, "state.json"), worldPath = Path.Combine(folder, "world.json");
        try
        {
            var world = new WorldMap { Id = "test", Width = 12, Height = 8,
                Biomes = Enumerable.Repeat(".....~......", 8).ToList(),
                Roads = Enumerable.Repeat("000000000000", 8).ToList(),
                Rivers = Enumerable.Repeat("000000000000", 8).ToList(),
                Owners = Enumerable.Repeat("aaaaaabbbbbb", 8).ToList(),
                Kingdoms = [new() { Name = "North" }, new() { Name = "North Coast" }] };
            world.Chronicle.Description = new string('d', 3000) + " Premise ending.";
            world.Chronicle.DmNotes = new string('r', 4000) + " Runes require fuel.";
            world.Chronicle.Places = Enumerable.Range(0, 200).Select(i => new GamePlace {
                Name = $"Place {i}", Type = "ruins", X = i % 4, Y = 0,
                Description = "Unrelated location description", DmNotes = "Unrelated secret" }).ToList();
            File.WriteAllText(worldPath, JsonSerializer.Serialize(world));
            File.WriteAllText(statePath, """{"world":{"id":"test","x":0,"y":0}}""");
            var tools = new WorldQueryTools(new McpServerConfig(statePath, LogPath: null));
            string result = tools.WorldQuery("world");
            using var json = JsonDocument.Parse(result);
            Assert.Equal(world.Chronicle.Description, json.RootElement.GetProperty("description").GetString());
            Assert.Equal(world.Chronicle.DmNotes, json.RootElement.GetProperty("dmNotes").GetString());
            Assert.DoesNotContain("Unrelated", result);
            Assert.False(json.RootElement.TryGetProperty("places", out _));
            Assert.Contains("Unrelated secret", tools.WorldQuery("place:Place 199"));
            Assert.Contains("Ambiguous place", tools.WorldQuery("place:Place"));
            Assert.Contains("Place 0", tools.WorldQuery("near:Place"));
            Assert.Contains("nonempty", tools.WorldQuery("place:"));
            string rejected = tools.AddPlace("New village", "village", "A new settlement", near: "Place");
            Assert.Contains("Ambiguous place", rejected);
            Assert.DoesNotContain("Patch for the response", rejected);
            foreach (string invalid in new[] { "Nor", "Unknown", "K9", "K-1", "   " })
                Assert.DoesNotContain("Patch for the response", tools.AddPlace("New ruin", "ruins", "A ruin", kingdom: invalid));
            Assert.Contains("Ambiguous kingdom", tools.AddPlace("New ruin", "ruins", "A ruin", kingdom: "Nor"));
            foreach (var (name, owner) in new[] { ("North", 0), ("Coast", 1), ("K1", 1), ("k1", 1) })
            {
                string added = tools.AddPlace("New ruin", "ruins", "A ruin", kingdom: name);
                Assert.Contains("Patch for the response:", added);
                using var placePatch = JsonDocument.Parse(added.Split("Patch for the response:")[1]);
                var place = placePatch.RootElement.GetProperty("world").GetProperty("places")[0];
                Assert.Equal(owner, world.OwnerAt(place.GetProperty("x").GetInt32(), place.GetProperty("y").GetInt32()));
                Assert.Equal(owner == 1, added.Contains("No walking route"));
            }
        }
        finally
        {
            File.Delete(statePath);
            File.Delete(worldPath);
            Directory.Delete(folder);
            Directory.Delete(worlds);
            Directory.Delete(root);
        }
    }
}
