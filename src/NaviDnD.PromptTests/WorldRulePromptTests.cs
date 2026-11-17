using System.Reflection;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class WorldRulePromptTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HearthRuleCannotConjureFuelOrHealingAndDoesNotRevealHistoricalSecret(bool archivedRule)
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        var ws = storage.WorldState;
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary)
            .GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        string id = "world-rule-" + Guid.NewGuid().ToString("N");
        var world = new WorldMap { Id = id, Width = 2, Height = 1, Biomes = [".."],
            Roads = ["00"], Rivers = ["00"], Owners = [".."] };
        world.Chronicle.Description = "Hearth runes preserve existing warmth while a host honors promised shelter. They need ordinary fuel and cannot heal injuries.";
        world.Chronicle.DmNotes = "The Ash Summer was caused by the secret Cinder Loom network. This historical secret is unknown to the hero and unrelated to this cold hearth. Runes never generate heat without fuel.";
        if (archivedRule)
        {
            world.Chronicle.Description = "Hearth runes respond to petitions according to the world's established rules, costs and limits.";
            world.Chronicle.DmNotes = string.Concat(Enumerable.Repeat("The local archives preserve many accounts of hearth-keeping traditions. ", 35))
                + "When petitioned without fuel, a hearth rune displays the word ASHWARD on the stone, remaining visible until fuel is added. Record this visible state in the hearth object's aiInfo. It creates no warmth and cannot heal. The secret Cinder Loom caused the ancient catastrophe; this unrelated historical truth remains unknown to the hero.";
        }
        world.Chronicle.Places = [new() { Name = "Cold shelter", Type = "ruins", X = 0, Y = 0, Description = "An abandoned shelter." }];
        worlds[id] = world;
        string folder = Path.Combine(Path.GetDirectoryName(Storage.SavePath)!, "Worlds", id);
        string file = Path.Combine(folder, "world.json");
        try
        {
            ws.World = new() { Id = id, X = 0, Y = 0, Place = "Cold shelter", Known = ["Cold shelter"] };
            ws.Map = new() { Chunks = [new() { Theme = "building", Populated = true,
                    Terrain = Enumerable.Repeat(new string('w', 20), 15).ToList() }],
                Objects = [new() { Name = "Cold hearth", Symbol = "[H]", Position = [10, 8],
                    AiInfo = ["A stone hearth with a valid warmth-preserving rune, completely cold and empty. There is no fuel here."] }],
                Entities = [], Rooms = [], Doors = [], Furniture = [] };
            ws.Hero!.Position = [9, 8];
            ws.Hero.Hp = "5/20";
            ws.Hero.Inventory = [];
            ws.Hero.Effects = [];
            ws.History = [];
            ws.Combat = null;
            ws.Narrative = new();
            var visible = Enumerable.Range(1, 20).SelectMany(c => Enumerable.Range(1, 15).Select(r => (c, r))).ToHashSet();
            string request = "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(ws, visible).MinimalState(
                "Put my hands on the cold hearth rune and ask it to create warmth and heal my wounds immediately, without adding any fuel or using my abilities.");
            if (archivedRule)
            {
                Assert.DoesNotContain("ASHWARD", request);
                Assert.Contains("[truncated; world_query world]", request);
                Directory.CreateDirectory(folder);
                File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(world));
            }
            var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage, request);
            output.WriteLine(reply.ToolTrace);
            output.WriteLine(reply.RawResponse);
            var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse);
            Assert.NotEmpty(parsed.HistoryTexts);
            Assert.DoesNotContain(parsed.HistoryTexts, text => text.Contains("Cinder Loom", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("5/20", ws.Hero.Hp);
            Assert.Empty(ws.Hero.Inventory.Where(i => i.Deleted != true));
            Assert.Empty(ws.Hero.Effects.Where(e => e.Deleted != true));
            Assert.Empty(ws.Narrative.CurrentArc);
            Assert.False(ws.Combat?.Active == true);
            if (archivedRule)
            {
                Assert.Contains("MCP world_query → \"world\"", reply.ToolTrace);
                Assert.Contains(ws.Map.Objects![0].AiInfo!, info => info.Contains("ASHWARD", StringComparison.Ordinal));
            }
        }
        finally
        {
            worlds.Remove(id);
            if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }
}
