using System.Text.Json.Nodes;
using NaviDnD.Migrations;

namespace NaviDnD.Tests;

public sealed class PlotStatusMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "navidnd-plot-migration-" + Guid.NewGuid().ToString("N"));

    public PlotStatusMigrationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Theory]
    [InlineData("narrative", "plotThreads", "status")]
    [InlineData("Narrative", "PlotThreads", "Status")]
    public void VersionSevenNormalizesKnownStatusesWithoutLosingStoryOrCustomLabels(string narrativeKey, string threadsKey, string statusKey)
    {
        var threads = new JsonArray();
        string?[] statuses = ["активна", "завершена", "rumor", "offered", "revealed", "failed", "  Выполнить позже — после дождя  ", null];
        string?[] expectedStatuses = ["active", "completed", "rumor", "offered", "revealed", "failed", statuses[6], null];
        foreach (string? status in statuses)
            threads.Add(new JsonObject
            {
                [statusKey] = status,
                ["name"] = "The ferry",
                ["steps"] = new JsonArray("Found a clue", "Accepted the request"),
                ["dmNotes"] = "The keeper has the key",
                ["hidden"] = true,
                ["deleted"] = false,
                ["custom"] = new JsonObject { ["value"] = 12 },
            });
        threads.Add(new JsonObject { ["name"] = "No status was recorded" });
        var originalNode = new JsonObject
        {
            ["formatVersion"] = 7,
            [narrativeKey] = new JsonObject { ["currentArc"] = "Player's goal", [threadsKey] = threads },
            ["time"] = new JsonObject { ["totalRounds"] = 19, ["day"] = 3 },
        };
        string original = originalNode.ToJsonString();
        string path = Path.Combine(_root, "game1.json");
        File.WriteAllText(path, original);
        var expected = originalNode.DeepClone().AsObject();
        expected["formatVersion"] = StorageFormat.Current;
        for (int i = 0; i < expectedStatuses.Length; i++)
            expected[narrativeKey]![threadsKey]![i]![statusKey] = expectedStatuses[i];

        var result = StorageMigration.Run(_root);

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(1, result.Migrated);
        Assert.Equal(original, File.ReadAllText(Path.Combine(result.BackupDir!, "game1.json")));
        string after = File.ReadAllText(path);
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(after)));

        var second = StorageMigration.Run(_root);

        Assert.True(second.Ok);
        Assert.Equal(0, second.Migrated);
        Assert.Null(second.BackupDir);
        Assert.Equal(after, File.ReadAllText(path));
        Assert.False(StorageMigration.Needed(_root));
    }
}
