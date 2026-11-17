using System.Reflection;
using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class RenamedPlaceRecallTests
{
    [Fact]
    public void CurrentPlaceRecallsItsFormerNamesWithoutRewritingRecordsOrRevealingOtherPlaces()
    {
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        string id = "rename-recall-" + Guid.NewGuid().ToString("N");
        var world = new WorldMap { Id = id, Width = 3, Height = 1, Biomes = ["..."], Roads = ["000"], Rivers = ["000"], Owners = ["aaa"],
            Kingdoms = [new() { Name = "Coast", Capital = 0 }], Places = [new() { Name = "Generator capital", Type = WorldPlaceTypes.Capital, X = 1, Y = 0 }],
            Chronicle = new() { Places = [
                new() { Name = "New Harbor", FormerNames = ["Old Harbor", "Older Harbor"], Type = WorldPlaceTypes.Capital, Slot = 0, X = 1, Y = 0,
                    Description = "The quay has been repaired.", DmNotes = "The keeper hides a ledger." },
                new() { Name = "Hidden cellar", Type = WorldPlaceTypes.Ruins, X = 1, Y = 0, DmNotes = "Unrelated private cellar marker" },
            ] } };
        worlds[id] = world;
        var state = new WorldState { World = new() { Id = id, Place = "New Harbor", X = 1, Y = 0, Known = ["New Harbor"], Visited = ["New Harbor"] },
            Narrative = new()
            {
                Npcs = Enumerable.Range(0, 1000).Select(i => new NpcRecord { Name = $"Resident {i}", Notes = "Ordinary life" }).ToList(),
                PlotThreads = Enumerable.Range(0, 1000).Select(i => new PlotThread { Name = $"Errand {i}", Status = "completed", Description = "Unrelated delivery" }).ToList(),
            } };
        state.Narrative.Npcs[7].Memory = ["Old Harbor: the ferryman promised free passage after the rescue."];
        state.Narrative.PlotThreads[3].Steps = ["Older Harbor: the sluice was repaired and stays open."];
        state.Narrative.PlotThreads[3].Hidden = true;
        state.Narrative.PlotThreads[3].DmNotes = "Local private sluice marker";
        state.Narrative.PlotThreads[4].Description = "Old Harborview: unrelated word-boundary marker";
        state.Narrative.PlotThreads[9].Name = "Old Harbor repairs";
        string before = JsonSerializer.Serialize(state.Narrative);
        try
        {
            var builder = new AiContextBuilder(state);
            string scene = builder.MinimalState("Look around");
            string population = builder.PopulationState(new() { Purpose = "An ordinary quay" });
            Assert.Contains("Resident 7#7", scene);
            Assert.Contains("Errand 3#3", scene);
            Assert.Contains("Thread#3 Errand 3", scene);
            Assert.Contains("Thread#9 Old Harbor repairs", scene);
            Assert.Contains("Older Harbor: the sluice was repaired", scene);
            Assert.Contains("NPC#7 Resident 7", population);
            Assert.Contains("Old Harbor: the ferryman promised", population);
            Assert.Contains("Thread#3 Errand 3", population);
            Assert.Contains("Thread#9 Old Harbor repairs", population);
            Assert.Contains("hidden:True", population);
            Assert.Contains("GM only; not hero knowledge", population);
            foreach (string context in new[] { scene, population })
            {
                Assert.DoesNotContain("Unrelated private cellar marker", context);
                Assert.DoesNotContain("unrelated word-boundary marker", context);
            }
            Assert.Equal(before, JsonSerializer.Serialize(state.Narrative));
            Assert.Equal(new[] { "New Harbor" }, state.World.Known);
            Assert.Equal(new[] { "New Harbor" }, state.World.Visited);
            Assert.Single(GameWorld.ForDisplay(state).Places);
        }
        finally { worlds.Remove(id); }
    }
}
