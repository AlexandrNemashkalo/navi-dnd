using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class WorldRevisionPromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SmallToneRevisionPreservesNamesAndPrivateWorldTruth()
    {
        var world = WorldGenerator.Generate(new WorldGenerator.Options(5, WorldSizes.Large));
        Assert.True(world.Kingdoms.Count > 1);
        world.Chronicle.Name = "Low Ember";
        world.Chronicle.Description = "After the Long Winter, communities rebuild through practical engineering. Hearth engines store heat from burned fuel; they cannot create fuel or heal injuries.";
        world.Chronicle.DmNotes = "The hidden cause of the Long Winter was the CINDER-71 sabotage. Only the archivists know this. Hearth engines require fuel and never heal injuries.";
        for (int id = 0; id < world.Kingdoms.Count; id++)
        {
            world.Chronicle.Kingdoms.Add(new() { Id = id, Name = $"Ember Union {id}",
                Description = "An elected council organizes repairs and trade with its neighbors.",
                DmNotes = "The council seeks reliable trade but cannot spare its winter fuel." });
            world.Chronicle.Places.Add(new() { Slot = world.Kingdoms[id].Capital, Type = WorldPlaceTypes.Capital,
                Name = $"Hearthport {id}", X = world.Places[world.Kingdoms[id].Capital].X, Y = world.Places[world.Kingdoms[id].Capital].Y });
        }
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        string request = "lang:en\nspeechEnabled:false\n" + AiContextBuilder.WorldConcept(world,
            "Make only the public tone more hopeful and welcoming for exploration. Keep the existing world rules, history, names, governments and private truth unchanged.", "Low Ember", world.Chronicle.Description);
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "CreateWorld", storage, request);
        output.WriteLine(reply.RawResponse);
        using var response = JsonDocument.Parse(reply.RawResponse);
        Assert.True(GameWorld.ApplyChronicle(world, response.RootElement));
        Assert.Equal("Low Ember", world.Chronicle.Name);
        Assert.Contains("CINDER-71", world.Chronicle.DmNotes);
        Assert.DoesNotContain("CINDER-71", world.Chronicle.Description);
        for (int id = 0; id < world.Kingdoms.Count; id++)
        {
            Assert.Equal($"Ember Union {id}", world.Chronicle.Kingdoms.Single(k => k.Id == id).Name);
            Assert.Equal($"Hearthport {id}", world.Chronicle.Places.Single(p => p.Slot == world.Kingdoms[id].Capital).Name);
            Assert.DoesNotContain("CINDER-71", world.Chronicle.Kingdoms.Single(k => k.Id == id).Description);
        }
    }
}
