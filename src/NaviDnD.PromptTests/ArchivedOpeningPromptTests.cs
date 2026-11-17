using System.Reflection;
using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class ArchivedOpeningPromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task OpeningAtAnArchivedPlaceDoesNotReplaceItsMap()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var ws = storage.WorldState;
        string id = "archived-opening-" + Guid.NewGuid().ToString("N");
        string directory = Path.Combine(Path.GetDirectoryName(Storage.SavePath)!, "Worlds", id);
        string locations = Path.Combine(directory, "locations");
        string file = Path.Combine(directory, "world.json"), snapshot = Path.Combine(locations, "harbor.json");
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var world = new WorldMap { Id = id, Width = 3, Height = 1, Biomes = ["..."], Roads = ["111"], Rivers = ["000"], Owners = ["..."],
            Chronicle = new() { Name = "Quiet Coast", Description = "A peaceful region of small working settlements.",
                Places = [new() { Name = "Old Harbor", Type = "village", X = 1, Y = 0, Location = "harbor.json", Description = "An established working harbor." }] } };
        const string archive = """{"objects":[{"name":"Opened cache","aiInfo":["Its unique token was taken long ago."]}]}""";
        Directory.CreateDirectory(locations);
        File.WriteAllText(file, JsonSerializer.Serialize(world));
        File.WriteAllText(snapshot, archive);
        worlds[id] = world;
        try
        {
            ws.World = new() { Id = id, Place = "Old Harbor", X = 1, Y = 0, Known = ["Old Harbor"] };
            ws.Map = new(); ws.Narrative = new(); ws.History = []; ws.Combat = null;
            string request = "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(ws).MinimalState(
                "Begin my quiet exploration at Old Harbor in the existing Quiet Coast world. I have come to look around and meet people, without accepting a job.");
            var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "StartNewGame", storage, request);
            output.WriteLine(reply.RawResponse);
            Assert.Contains("already has an archived location", reply.ToolTrace, StringComparison.OrdinalIgnoreCase);
            var draft = new Storage();
            draft.LoadDraft();
            Assert.False(GameWorld.HasLocation(draft.WorldState));
            var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse, retainHistory: true);
            Assert.NotEmpty(parsed.HistoryTexts);
            Assert.Equal(archive, File.ReadAllText(snapshot));
            Assert.False(GameWorld.HasLocation(ws));
            Assert.Equal("Old Harbor", ws.World.Place);
            Assert.True(string.IsNullOrEmpty(ws.Narrative.CurrentArc));
            Assert.False(ws.Combat?.Active == true);
        }
        finally
        {
            worlds.Remove(id);
            File.Delete(snapshot); File.Delete(file);
            Directory.Delete(locations); Directory.Delete(directory);
        }
    }
}
