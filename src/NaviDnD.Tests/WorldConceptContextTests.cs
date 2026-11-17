using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class WorldConceptContextTests
{
    [Fact]
    public void RevisionReceivesExistingRulesAndKingdomIdentityWithoutEveryCampaignPlace()
    {
        var world = WorldGenerator.Generate(new WorldGenerator.Options(5, WorldSizes.Small));
        world.Chronicle.Name = "Low Ember";
        world.Chronicle.Description = "Heat cannot be created from nothing.";
        world.Chronicle.DmNotes = "The old furnace was sabotaged by the regent.";
        world.Chronicle.Kingdoms = [new() { Id = 0, Name = "Reed Union", Description = "Trades grain for mountain iron.", DmNotes = "The council seeks peace." }];
        world.Chronicle.Places = [new() { Slot = world.Kingdoms[0].Capital, Type = WorldPlaceTypes.Capital, Name = "Reedhaven" },
            new() { Name = "Unrelated hidden cellar", Type = "ruins" }];
        string request = AiContextBuilder.WorldConcept(world, "Make the tone more hopeful", "Chosen Name", "Player-written premise");
        foreach (string fact in new[] { "Low Ember", "Heat cannot be created", "sabotaged", "Reed Union", "mountain iron", "council seeks peace", "Reedhaven", "Chosen Name", "Player-written premise" })
            Assert.Contains(fact, request);
        Assert.DoesNotContain("Unrelated hidden cellar", request);
        Assert.Contains("dmNotes are GM secrets", request);
        Assert.Equal("Low Ember", world.Chronicle.Name);
    }
}
