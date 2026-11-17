using System.Text.Json;
using System.Text.RegularExpressions;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class PuzzlePersistencePromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LearnedSequenceSurvivesReloadAndOnlyCorrectAttemptOpensPassage()
    {
        var storage = PromptScenario.LoadLocalFixture("door_interaction_game.json");
        var ws = storage.WorldState;
        ws.World = new() { Place = "Old garden" };
        ws.History = [];
        ws.Combat = null;
        ws.Narrative = new();
        ws.Hero!.Position = [10, 8];
        ws.Hero.Inventory = [];
        string hp = ws.Hero.Hp;
        ws.Map = new()
        {
            Rooms = [new() { Name = "Garden approach", Positions = Cells(8, 10) },
                new() { Name = "Inner garden", Positions = Cells(11, 13) }],
            Chunks = [], Entities = [], Furniture = [],
            Objects = [new() { Name = "Gardener's plate", Symbol = "PLT", Position = [10, 9],
                AiInfo = ["An ordinary legible instruction plate beside the gate, with the exact sequence LEAF -> SUN engraved on it. Reading is certain, requires no roll and operates nothing. The hero has not yet read it."] }],
            Doors = [new() { From = [10, 8], To = [11, 8], IsDoor = true, IsDoorOpen = false,
                AiInfo = ["A harmless mechanical garden gate with two buttons labelled LEAF and SUN. Only pressing LEAF then SUN opens it. Pressing SUN then LEAF resets the buttons and leaves the gate closed, without damage, traps or any check. The matching sequence opens it certainly, without extra prerequisites. Operating the gate does not move the hero through it."] }]
        };
        Check(ws, false);
        await Act(storage, "Read the gardener's plate and record its exact engraved sequence in my journal. I do not press either button or move.");
        Check(ws, false);
        CheckClue(ws);

        storage = Reload(ws);
        CheckClue(storage.WorldState);
        await Act(storage, "I deliberately try the reverse order: press SUN, then LEAF, once. Do not try any other sequence for me. I stay on this side of the gate.");
        Check(storage.WorldState, false);
        CheckClue(storage.WorldState);

        storage = Reload(storage.WorldState);
        CheckClue(storage.WorldState);
        await Act(storage, "Now I press the buttons in the correct order recorded in my saved journal note, opening the gate. I do not walk through it.");
        Check(storage.WorldState, true);
        CheckClue(storage.WorldState);
        Check(Reload(storage.WorldState).WorldState, true);

        static List<List<int>> Cells(int first, int last) => (from c in Enumerable.Range(first, last-first+1)
            from r in Enumerable.Range(7, 3) select new List<int> { c, r }).ToList();
        static Storage Reload(WorldState state)
        {
            state.History = [];
            var result = new Storage();
            result.ApplySavedState(JsonSerializer.Serialize(state));
            return result;
        }
        static void CheckClue(WorldState state)
        {
            var known = state.Narrative!.PlotThreads.Where(t => t.Deleted != true && t.Hidden != true)
                .Select(t => (t.Description ?? "") + " " + string.Join(" ", t.Steps ?? []));
            Assert.Contains(known, text => Regex.IsMatch(text, @"\bLEAF\s*(?:->|→)\s*SUN\b", RegexOptions.IgnoreCase));
        }
        void Check(WorldState state, bool open)
        {
            var door = Assert.Single(state.Map.Doors!);
            Assert.Equal(new[] { 10, 8 }, door.From);
            Assert.Equal(new[] { 11, 8 }, door.To);
            Assert.True(door.IsDoor);
            Assert.NotEqual(true, door.Hidden);
            Assert.Equal(open, door.IsDoorOpen);
            Assert.Equal(open, MovementCalculator.CanMoveTo(state, 10, 8, 11, 8, -1));
            Assert.Equal(2, state.Map.Rooms!.Count);
            Assert.Equal(new[] { 10, 8 }, state.Hero!.Position);
            Assert.Equal(hp, state.Hero.Hp);
            Assert.Empty(state.Hero.Inventory);
            Assert.False(state.Combat?.Active == true);
            Assert.Empty(state.Narrative!.CurrentArc);
            Assert.DoesNotContain(state.Narrative.PlotThreads, t => t.Deleted != true && t.Status == PlotThreadStatuses.Active);
        }
    }

    private async Task Act(Storage storage, string action)
    {
        var visible = Enumerable.Range(1,20).SelectMany(c => Enumerable.Range(1,15).Select(r => (c,r))).ToHashSet();
        var reply = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
            "lang:en\nspeechEnabled:false\n" + new AiContextBuilder(storage.WorldState, visible).MinimalState(action));
        output.WriteLine(action + "\n" + reply.ToolTrace + "\n" + reply.RawResponse);
        Assert.False(reply.RollDiceTriggered);
        var parsed = PromptScenario.ApplyAndParse(storage, reply.RawResponse, retainHistory: true);
        Assert.NotEmpty(parsed.HistoryTexts);
    }
}
