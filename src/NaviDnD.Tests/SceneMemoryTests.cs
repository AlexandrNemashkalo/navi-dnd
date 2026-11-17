using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class SceneMemoryTests
{
    [Fact]
    public void LongEarlierMemoryDoesNotHideTheLatestAgreementAfterReload()
    {
        var ws = new WorldState { Narrative = new() { Npcs = [new()
        {
            Name = "Mira", Memory = [new string('x', 1200), "Mira used to charge a toll.", "Mira granted the hero free passage for life."]
        }] } };
        string saved = System.Text.Json.JsonSerializer.Serialize(ws);
        var storage = new Storage();
        storage.ApplySavedState(saved);
        string narrativeBeforeRecall = System.Text.Json.JsonSerializer.Serialize(storage.WorldState.Narrative);
        string context = new AiContextBuilder(storage.WorldState).MinimalState("Ask Mira about crossing");
        Assert.Contains("Mira granted the hero free passage for life.", context);
        Assert.Contains("Mira used to charge a toll.", context);
        Assert.True(context.IndexOf("free passage", StringComparison.Ordinal) < context.IndexOf("used to charge", StringComparison.Ordinal));
        string population = new AiContextBuilder(storage.WorldState).PopulationState(new MapChunk { Purpose = "Mira's ferry landing" });
        Assert.Contains("Mira granted the hero free passage for life.", population);
        Assert.Equal(narrativeBeforeRecall, System.Text.Json.JsonSerializer.Serialize(storage.WorldState.Narrative));
    }

    [Fact]
    public void QuestionTopicRecallsResolvedOutcomeBeyondTheFirstThreeLocalThreads()
    {
        var ws = new WorldState { World = new() { Place = "Harbor" }, Narrative = new()
        {
            Npcs = [new() { Name = "Mira", Met = true }],
            PlotThreads = [
                new() { Name = "Nets", Status = "completed", Description = "Mira repaired nets in Harbor." },
                new() { Name = "Market", Status = "completed", Description = "Mira delivered fruit in Harbor." },
                new() { Name = "Cart", Status = "completed", Description = "Mira mended a cart in Harbor." },
                new() { Name = "A winter promise", Status = "completed", Description = "Mira restored the silver lantern in Harbor.",
                    DmNotes = "The silver lantern opens the sluice without payment.", Steps = ["The lock was permanently repaired."] }]
        } };
        string context = new AiContextBuilder(ws).MinimalState("Ask Mira whether the silver lantern still opens the sluice");
        Assert.Contains("Thread#3 A winter promise [completed", context);
        Assert.Contains("opens the sluice without payment", context);
        Assert.Contains("The lock was permanently repaired", context);
        Assert.Equal("completed", ws.Narrative.PlotThreads[3].Status);
        Assert.True(context.Length < 8000);
    }

    [Fact]
    public void NullGoalClearsTheChosenArcAndSurvivesReload()
    {
        var storage = new Storage();
        storage.ApplyUpdateWorldState("""{"narrative":{"currentArc":"Repair the ferry"}}""");
        storage.ApplyUpdateWorldState("""{"narrative":{"currentArc":null}}""");
        Assert.Equal("", storage.WorldState.Narrative!.CurrentArc);
        var restored = new Storage();
        restored.ApplySavedState(System.Text.Json.JsonSerializer.Serialize(storage.WorldState));
        Assert.Equal("", restored.WorldState.Narrative!.CurrentArc);
        Assert.DoesNotContain("arc:", new AiContextBuilder(restored.WorldState).MinimalState("Look around"));
    }

    [Fact]
    public void AppliedResolutionPreservesJournalAndReplacesObsoletePlanInRecall()
    {
        var storage = new Storage();
        storage.ApplyUpdateWorldState("""
            {"narrative":{"currentArc":"Ferry repairs","plotThreads":[{"name":"Ferry repairs","status":"active","description":"Mira asked for timber","dmNotes":"Mira will close the ferry tonight","steps":["Accepted Mira's request"]}],"npcs":[{"name":"Mira","met":true,"memory":["Hero promised timber"]}]}}
            """);
        storage.ApplyUpdateWorldState("""
            {"narrative":{"currentArc":"","plotThreads":[{"id":0,"status":"completed","dmNotes":"Mira repaired the ferry; it remains open","steps":["Delivered the timber"]}],"npcs":[{"id":0,"memory":["Mira thanked the hero for the timber"]}]}}
            """);
        var state = storage.WorldState;
        string context = new AiContextBuilder(state).MinimalState("Speak to Mira");
        Assert.NotNull(state.Narrative);
        Assert.Equal("", state.Narrative.CurrentArc);
        Assert.Equal("completed", state.Narrative.PlotThreads[0].Status);
        Assert.Equal(2, state.Narrative.PlotThreads[0].Steps!.Count);
        Assert.Equal(2, state.Narrative.Npcs[0].Memory!.Count);
        Assert.Contains("it remains open", context);
        Assert.Contains("Delivered the timber", context);
        Assert.DoesNotContain("will close the ferry", context);
    }

    [Fact]
    public void ChosenGoalRemainsAvailableAfterLeavingItsPlace()
    {
        var ws = new WorldState { World = new() { Place = "Harbor" }, Narrative = new()
        {
            CurrentArc = "Resolve the Missing convoy",
            PlotThreads = [new() { Name = "Missing convoy", DmNotes = "Mountain pass: the wagon axle broke", Steps = ["Promised to find the driver"] }]
        } };
        string context = new AiContextBuilder(ws).MinimalState("Check my supplies");
        Assert.Contains("Thread#0 Missing convoy", context);
        Assert.Contains("the wagon axle broke", context);
        Assert.Contains("Promised to find the driver", context);
    }

    [Fact]
    public void NamedPersonRecallsRelatedThreadWithoutTacticalMap()
    {
        var ws = new WorldState { Narrative = new()
        {
            Npcs = [new() { Name = "Mira", Met = true }],
            PlotThreads = [new() { Name = "Ferry repairs", DmNotes = "Mira needs timber", Hidden = true }]
        } };
        string context = new AiContextBuilder(ws).MinimalState("Ask Mira about her work");
        Assert.Contains("Thread#0 Ferry repairs", context);
        Assert.True(ws.Narrative.PlotThreads[0].Hidden);
        Assert.Null(ws.Narrative.PlotThreads[0].Description);
    }

    [Fact]
    public void ChosenGoalHasPriorityOverLocalBackgroundAndHiddenArcIsNotPromoted()
    {
        var ws = new WorldState { World = new() { Place = "Harbor" }, Narrative = new()
        {
            CurrentArc = "Missing convoy; Secret council",
            PlotThreads = [new() { Name = "Fishing", DmNotes = "Harbor: nets need repair" },
                new() { Name = "Market", DmNotes = "Harbor: market opens tomorrow" },
                new() { Name = "Storm", DmNotes = "Harbor: sailors watch the clouds" },
                new() { Name = "Missing convoy", DmNotes = "The wagon axle broke" },
                new() { Name = "Secret council", Hidden = true, DmNotes = "unrelated-council-secret" }]
        } };
        string context = new AiContextBuilder(ws).MinimalState("Look around");
        Assert.Contains("Thread#3 Missing convoy", context);
        Assert.DoesNotContain("Thread#2 Storm", context);
        Assert.DoesNotContain("unrelated-council-secret", context);
    }

    [Theory]
    [InlineData("Ann", "Inspect the banner", "Anna guards the distant bridge")]
    [InlineData("Мир", "Осматриваю мирную деревню", "Мира охраняет мост")]
    public void NameFragmentsDoNotRecallUnrelatedSecrets(string name, string action, string plan)
    {
        var ws = new WorldState { World = new() { Place = name }, Narrative = new()
        {
            Npcs = [new() { Name = name, DmNotes = "unrelated-npc-secret" }],
            PlotThreads = [new() { Name = "Distant plan", DmNotes = plan, Hidden = true }]
        } };
        string context = new AiContextBuilder(ws).MinimalState(action);
        Assert.DoesNotContain("unrelated-npc-secret", context);
        Assert.DoesNotContain(plan, context);
    }

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
