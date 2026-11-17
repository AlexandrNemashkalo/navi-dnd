using System.Reflection;
using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public sealed class PendingTravelRecoveryTests
{
    [Fact]
    public async Task FailedTravelAndFailedRepairKeepReportUntilNextAcceptedAction()
    {
        string root = Path.Combine(Path.GetTempPath(), "navidnd-travel-recovery-" + Guid.NewGuid().ToString("N"));
        string ruleSet = "TravelRecovery_" + Guid.NewGuid().ToString("N");
        string rules = Path.Combine(AppConfig.ProjectRoot, "Prompts", ruleSet);
        string promptDirectory = Path.Combine(rules, "SendAction");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(promptDirectory);
        File.WriteAllText(Path.Combine(promptDirectory, "systemPrompt.md"), "Return JSON.");
        string save = Path.Combine(root, "game.json");
        string originalSave = Storage.SavePath;
        const string report = "Travel already completed: reached the ferry on day 3; account for one ration.";
        try
        {
            using var scope = (IDisposable)typeof(Storage).GetMethod("UseSavePath", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [save])!;
            var storage = new Storage { SaveExploredCells = false };
            storage.WorldState.Hero = new Hero
            {
                Name = "Traveller", Symbol = "HRO", Hp = "10/10", Position = [3, 4],
                Inventory = [new() { Name = "Rations", Quantity = 5 }],
            };
            storage.WorldState.Time = new() { Day = 3, PartOfDay = PartsOfDay.Evening, TotalRounds = 23 };
            storage.WorldState.World = new() { Id = "absent-test-world-" + Guid.NewGuid().ToString("N"), X = 7, Y = 9 };
            var provider = new RecoveryProvider(save, report);
            var client = new GameAiClient(storage.WorldState, storage, provider, new AppConfig { RuleSet = ruleSet });

            await client.Travel(report);

            Assert.Equal(2, provider.Messages.Count); // Original request and syntax repair both failed.
            Assert.Equal("RepairJson", Path.GetFileName(provider.Paths[1]));
            Assert.All(provider.SavedReports, text => Assert.Equal(report, text));
            Assert.Equal(report, storage.WorldState.PendingTravel);
            Assert.Equal(report, ReadSave(save).PendingTravel);
            AssertTravelState(storage.WorldState, 5);
            AssertTravelState(ReadSave(save), 5);

            // Simulate restarting the game: recovery must come from disk, not the old client or state.
            storage = new Storage { SaveExploredCells = false };
            storage.LoadFrom(save);
            Assert.Equal(report, storage.WorldState.PendingTravel);
            AssertTravelState(storage.WorldState, 5);
            client = new GameAiClient(storage.WorldState, storage, provider, new AppConfig { RuleSet = ruleSet });

            await client.SendAction("Look at the ferry");

            Assert.Equal(3, provider.Messages.Count);
            Assert.Contains(report, provider.Messages[2]);
            Assert.Contains("Look at the ferry", provider.Messages[2]);
            Assert.Equal(report, provider.SavedReports[2]);
            Assert.Null(storage.WorldState.PendingTravel);
            Assert.Null(ReadSave(save).PendingTravel);
            AssertTravelState(storage.WorldState, 4);
            AssertTravelState(ReadSave(save), 4);

            await client.SendAction("Wait here");

            Assert.Equal(4, provider.Messages.Count);
            Assert.DoesNotContain(report, provider.Messages[3]);
            Assert.Null(provider.SavedReports[3]);
            AssertTravelState(storage.WorldState, 4);
            AssertTravelState(ReadSave(save), 4);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(rules, true);
        }
        Assert.Equal(originalSave, Storage.SavePath);
    }

    private static WorldState ReadSave(string path) => JsonSerializer.Deserialize<WorldState>(File.ReadAllText(path),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static void AssertTravelState(WorldState state, int rations)
    {
        Assert.Equal(rations, Assert.Single(state.Hero!.Inventory).Quantity);
        Assert.Equal(new[] { 3, 4 }, state.Hero.Position);
        Assert.Equal(3, state.Time.Day);
        Assert.Equal(PartsOfDay.Evening, state.Time.PartOfDay);
        Assert.Equal(23, state.Time.TotalRounds);
        Assert.Equal(7, state.World!.X);
        Assert.Equal(9, state.World.Y);
    }

    private sealed class RecoveryProvider(string save, string report) : IAiProvider
    {
        public List<string> Messages { get; } = [];
        public List<string> Paths { get; } = [];
        public List<string?> SavedReports { get; } = [];

        public Task<string> Complete(IReadOnlyList<string> blocks, string message, string path, string model)
        {
            Messages.Add(message);
            Paths.Add(path);
            SavedReports.Add(ReadSave(save).PendingTravel);
            if (Messages.Count <= 2) return Task.FromResult("{\"hero\": BROKEN}");
            if (Messages.Count == 3)
            {
                Assert.Contains(report, message);
                return Task.FromResult("""{"hero":{"inventory":[{"id":0,"quantity":4}]}}""");
            }
            return Task.FromResult("{}");
        }

        public Task<string> CompleteWithStreaming(IReadOnlyList<string> blocks, string message, string path, string model, Action<string>? onChunk)
            => Complete(blocks, message, path, model);
    }
}
