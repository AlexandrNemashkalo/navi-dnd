using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class SceneMemoryTests
{
    [Fact]
    public void AddressedNpcHasPriorityOverOtherVisibleBystanders()
    {
        var ws = new WorldState { Narrative = new() { Npcs =
        [new() { Name = "Innkeeper", Memory = ["Room available"] },
         new() { Name = "Driver", Memory = ["Cart broken"] },
         new() { Name = "Dunya", Memory = ["Knows the postman's sign"] }] } };
        ws.Map.Entities = [new() { Name = "Innkeeper", Position = [1, 1] },
            new() { Name = "Driver", Position = [2, 1] }, new() { Name = "Dunya", Position = [3, 1] }];
        string context = new AiContextBuilder(ws, [(1, 1), (2, 1), (3, 1)]).MinimalState("Dunya, what happened?");
        Assert.Contains("NPC#2 Dunya", context);
        Assert.Contains("Knows the postman's sign", context);
        Assert.DoesNotContain("NPC#1 Driver", context);
    }

    [Fact]
    public void LeavingPlaceDoesNotCarryItsUnrelatedPlansIntoScene()
    {
        var ws = new WorldState { World = new() { Place = "Переправа" }, Narrative = new()
        {
            PlotThreads = [new() { Name = "Пошлина", Hidden = true, DmNotes = "Переправа: закрыть вечером" },
                new() { Name = "Урожай", DmNotes = "Ферма: собрать утром" }]
        } };
        var builder = new AiContextBuilder(ws);
        Assert.Contains("закрыть вечером", builder.MinimalState("Жду"));
        Assert.DoesNotContain("собрать утром", builder.MinimalState("Жду"));
        ws.World.Place = "Ферма";
        Assert.DoesNotContain("закрыть вечером", builder.MinimalState("Жду"));
        Assert.Contains("собрать утром", builder.MinimalState("Жду"));
    }

    [Fact]
    public void SelectsPresentPeopleAndRelatedPlansWithOriginalIds()
    {
        var ws = new WorldState();
        ws.Map.Entities = [new() { Name = "Mira", Position = [2, 2] }];
        ws.Narrative = new()
        {
            Npcs = [new() { Name = "Deleted", Deleted = true },
                new() { Name = "Mira", DmNotes = "Keep the ferry open", Memory = ["Hero promised help"] },
                new() { Name = "Distant", DmNotes = "unrelated-secret" }],
            PlotThreads = [new() { Name = "Old", Deleted = true },
                new() { Name = "Ferry", Hidden = true, DmNotes = "Mira wants to reopen it" },
                new() { Name = "Other", DmNotes = "faraway-secret" }]
        };
        string memory = new AiContextBuilder(ws, [(2, 2)]).MinimalState("Hello");
        Assert.Contains("NPC#1 Mira", memory);
        Assert.Contains("Hero promised help", memory);
        Assert.Contains("Thread#1 Ferry", memory);
        Assert.DoesNotContain("unrelated-secret", memory);
        Assert.DoesNotContain("faraway-secret", memory);
        Assert.True(ws.Narrative.PlotThreads[1].Hidden); // Context must never reveal a journal entry.
    }

    [Fact]
    public void TextSceneCanRecallNamedNpcAndMemoryIsBounded()
    {
        var ws = new WorldState { Narrative = new() { Npcs = [new()
        {
            Name = "Мира", Notes = new string('n', 10000), DmNotes = new string('d', 10000),
            Memory = ["old-fact", "recent-one", "recent-two", "recent-three"]
        }] } };
        var builder = new AiContextBuilder(ws);
        Assert.DoesNotContain("## Scene memory", builder.MinimalState("Осматриваюсь"));
        string memory = builder.MinimalState("Мира, расскажи о переправе");
        Assert.Contains("recent-three", memory);
        Assert.DoesNotContain("old-fact", memory);
        int start = memory.IndexOf("## Scene memory", StringComparison.Ordinal);
        int end = memory.IndexOf("## Player action", start, StringComparison.Ordinal);
        Assert.InRange(end - start, 1, 2440);
    }
}
