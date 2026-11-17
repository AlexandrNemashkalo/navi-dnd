using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class SettingPopulationPromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task QuietOrbitalStationUsesItsEstablishedTechnologyAndDoesNotAssignAQuest()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var state = storage.WorldState;
        var chunk = new MapChunk { X = 1, Theme = "building",
            Purpose = "Routine shift in the maintenance bay. Add one engineer named Mina and one spare air-filter cartridge awaiting installation. Describe their ordinary work and the cartridge's practical purpose. No other new creatures or objects." };
        state.World = null;
        state.Map = new() { Cols = 40, Rows = 15, Chunks = [new() { Populated = true }, chunk],
            Rooms = [new() { Name = "Maintenance bay", Positions = Enumerable.Range(22,6)
                .SelectMany(c => Enumerable.Range(3,4).Select(r => new List<int> {c,r})).ToList() }],
            Doors = [new() { From = [22,4], To = [21,4], IsDoor = true }],
            Furniture = [new() { Kind = "table", Name = "Service bench", Positions = [[23,5],[24,5]] }],
            Objects = [], Entities = [] };
        state.Narrative = new()
        {
            World = "The orbital research station Lumen is a grounded science-fiction setting. Air is recycled by physical filters; oxygen cannot be created by magic. All inhabitants are ordinary humans using maintainable technology.",
            Style = "Peaceful science fiction, slow observational exploration, everyday life and competent cooperation. No supernatural elements or imposed missions."
        };
        state.Hero!.Position = [3,3];
        state.History = [];
        state.Combat = null;
        var context = new AiContextBuilder(state);
        string before = JsonSerializer.Serialize(new { state.Map.Rooms, state.Map.Doors, state.Map.Furniture });
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "PopulateChunk", storage,
            "lang:en\nspeechEnabled:false\n" + context.PopulationState(chunk) + "\n" + context.LocationPlan(chunk) + "\n" + context.ChunkState(chunk));
        output.WriteLine(reply.RawResponse);
        string patch = ChunkPopulationPatch.Prepare(state,new(state.Map,chunk,reply.RawResponse))!;
        PromptScenario.ApplyAndParse(storage,patch);
        var engineer = Assert.Single(state.Map.Entities!);
        var filter = Assert.Single(state.Map.Objects!);
        Assert.Equal("Mina", engineer.Name);
        Assert.Contains("filter", filter.Name, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(engineer.AiInfo!);
        Assert.NotEmpty(filter.AiInfo!);
        foreach (var entity in new CellEntity[] {engineer,filter})
        {
            Assert.NotNull(state.Map.RoomAt(entity.Position[0],entity.Position[1]));
            Assert.Null(state.Map.FurnitureAt(entity.Position[0],entity.Position[1]));
            Assert.NotEqual(new[] {22,4},entity.Position);
        }
        Assert.NotEqual(engineer.Position,filter.Position);
        Assert.Empty(state.History);
        Assert.Empty(state.Narrative.CurrentArc);
        Assert.Empty(state.Narrative.PlotThreads);
        Assert.Null(state.Combat);
        // Room descriptions/names may change; their cells, doors and furniture may not.
        using var original = JsonDocument.Parse(before);
        Assert.Equal(original.RootElement.GetProperty("Doors").GetRawText(),JsonSerializer.Serialize(state.Map.Doors));
        Assert.Equal(original.RootElement.GetProperty("Furniture").GetRawText(),JsonSerializer.Serialize(state.Map.Furniture));
    }
}
