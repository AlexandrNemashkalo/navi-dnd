using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.McpServer;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class RiverPlanRecoveryPromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ActualBankPlacementErrorLeadsToAChangedValidTemplePlan()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        string id = "river-recovery-" + Guid.NewGuid().ToString("N");
        string folder = Path.Combine(Path.GetDirectoryName(Storage.SavePath)!, "Worlds", id);
        string worldFile = Path.Combine(folder, "world.json");
        string flag = Path.Combine(Path.GetDirectoryName(Storage.SavePath)!, Storage.LocationPlannedFlag);
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var world = new WorldMap { Id = id, Width = 3, Height = 3,
            Biomes = ["...", "...", "..."], Rivers = ["000", "999", "000"],
            Roads = ["000", "000", "000"], Owners = ["...", "...", "..."],
            Chronicle = new() { Places = [new() { Name = "Riverside temple", Type = "ruins", X = 1, Y = 1,
                Description = "A peaceful intact temple beside the river, without enemies or an assigned quest." }] } };
        Directory.CreateDirectory(folder);
        File.WriteAllText(worldFile, JsonSerializer.Serialize(world));
        worlds[id] = world;
        try
        {
            var state = storage.WorldState;
            state.World = new() { Id = id, X = 1, Y = 1, Place = "Riverside temple", Known = ["Riverside temple"] };
            state.Map = new(); state.History = []; state.Combat = null; state.Narrative = new();
            state.Hero!.Position = [1,1]; state.Hero.Inventory = [];
            storage.Save();
            string original = File.ReadAllText(Storage.SavePath);
            string? actualError = null;
            var tool = new MapGenTools(new McpServerConfig(Storage.SavePath, LogPath: null));
            const string firstPlan = """{"blocks":[{"x":0,"y":0,"terrain":"building","building":"temple","purpose":"Peaceful intact riverside temple"}]}""";
            for (int attempt = 0; attempt < 10; attempt++)
            {
                // Restore only this test process's fixture save between stochastic local candidates.
                File.WriteAllText(Storage.SavePath, original);
                if (File.Exists(flag)) File.Delete(flag);
                string answer = tool.PlanLocation(firstPlan);
                if (answer.StartsWith('{'))
                {
                    using var json = JsonDocument.Parse(answer);
                    if (json.RootElement.TryGetProperty("error", out var error)
                        && error.GetString()!.Contains("dry riverbank"))
                    {
                        actualError = error.GetString();
                        Assert.Equal(original, File.ReadAllText(Storage.SavePath));
                        Assert.False(File.Exists(flag));
                        break;
                    }
                }
            }
            Assert.NotNull(actualError);
            output.WriteLine("Initial direct tool plan (not model-generated): " + firstPlan);
            output.WriteLine("Actual tool rejection: " + actualError);
            string action = "Continue the unfinished location setup for the Riverside temple. The previous plan_location call was "
                + firstPlan + " and its actual error was: " + actualError
                + " Correct the layout and finish generating this location, preserving a temple-style building and the site's river. "
                + "I only want to look around after arrival, not accept a quest or start a fight.";
            var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
                "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(state).MinimalState(action));
            output.WriteLine(reply.ToolTrace); output.WriteLine(reply.RawResponse);
            Assert.False(reply.RollDiceTriggered);
            int plans = Regex.Matches(reply.ToolTrace, @"→ PlanLocation:").Count;
            Assert.InRange(plans, 1, 3);
            Assert.True(File.Exists(flag), "The recovery must actually generate a location through the tool.");
            // Same ordering as the UI: tool-generated geometry first, then the model's population patch.
            File.Delete(flag);
            storage.ReloadMapFrom(Storage.SavePath);
            if (storage.WorldState.Map.Chunks?.FirstOrDefault(c => c.Exits.Any(e => e.External)) is { } start) start.Populated = true;
            var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse, retainHistory: true);
            Assert.NotEmpty(parsed.HistoryTexts);
            var after = storage.WorldState;
            Assert.Contains(after.Map.Chunks!, c => c.Z == 0 && c.Theme == "building" && c.Style == "temple");
            Assert.Contains(after.Map.Chunks!, c => c.Z == 0 && (MapChunk.IsOutdoorTheme(c.Theme) || c.Theme == "village"));
            Assert.Contains(after.Map.Chunks!.SelectMany(c => c.Terrain ?? []), row => row.Contains('W'));
            Assert.NotEmpty(after.Map.Rooms!);
            Assert.Equal(1, after.World!.X); Assert.Equal(1, after.World.Y);
            Assert.Equal("Riverside temple", after.World.Place);
            Assert.False(after.Combat?.Active == true);
            Assert.Empty(after.Narrative!.CurrentArc);
            Assert.DoesNotContain(after.Narrative.PlotThreads, t => t.Deleted != true && t.Status == PlotThreadStatuses.Active);
            Assert.Empty(after.Hero!.Inventory);
        }
        finally
        {
            worlds.Remove(id);
            if (File.Exists(flag)) File.Delete(flag);
            if (File.Exists(worldFile)) File.Delete(worldFile);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }
}
