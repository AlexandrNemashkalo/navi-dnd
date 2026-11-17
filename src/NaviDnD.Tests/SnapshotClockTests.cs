using System.Text.Json;
using System.Text.Json.Nodes;
using NaviDnD;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class SnapshotClockTests
{
    private static GameTime Clock(int day, string part, int round) =>
        new() { Day = day, PartOfDay = part, TotalRounds = round };

    private static MapConfig Scene(int deadline = 120) => new()
    {
        Cols = 4, Rows = 4,
        Rooms = [new() { Name = "Court", Positions = [[1, 1], [2, 1], [3, 1]] }],
        Entities = [new() { Name = "Watcher", Position = [2, 1], Hp = "3/10",
            Effects = [new() { Name = "Ward", ExpiresAtRound = deadline }] }],
        Objects = [new() { Name = "Seal", Position = [3, 1],
            Effects = [new() { Name = "Ward", ExpiresAtRound = deadline }] }]
    };

    private static Storage At(GameTime time) => new()
    {
        WorldState = { Time = time, Hero = new() { Name = "Traveler", Position = [4, 4], Hp = "4/10" } }
    };

    [Theory]
    [InlineData(110, false)]
    [InlineData(120, false)]
    [InlineData(121, true)]
    public void SamePartPreservesDeadlineIncludingExactLastActiveRound(int round, bool expired)
    {
        var storage = At(Clock(2, "morning", round));
        string snapshot = LocationRestoration.SerializeSnapshot(Scene(), Clock(2, "morning", 100));
        Assert.True(LocationRestoration.TryRestore(storage, snapshot));
        foreach (var owner in storage.WorldState.Map.Entities!.Cast<CellEntity>().Concat(storage.WorldState.Map.Objects!))
        {
            if (expired) Assert.DoesNotContain(owner.Effects!, e => e.Deleted != true);
            else Assert.Equal(120, Assert.Single(owner.Effects!.Where(e => e.Deleted != true)).ExpiresAtRound);
        }
        Assert.Equal("3/10", storage.WorldState.Map.Entities[0].Hp);
        Assert.Equal("4/10", storage.WorldState.Hero!.Hp);
        Assert.Equal(round, storage.WorldState.Time.TotalRounds);
    }

    [Theory]
    [InlineData(2, "morning", 3500, 3750, 2, "day", 100, 150, false)]
    [InlineData(2, "morning", 3500, 3750, 2, "day", 151, 150, true)]
    [InlineData(2, "evening", 100, 50000, 5, "evening", 1, 6800, false)]
    [InlineData(2, "evening", 100, 120, 5, "evening", 1, 0, true)]
    [InlineData(5, "day", 100, 120, 2, "night", 10, 30, false)]
    [InlineData(2, "morning", 3700, 3800, 2, "day", 150, 200, false)]
    public void CalendarRebasesRemainingDuration(int savedDay, string savedPart, int savedRound, int deadline,
        int currentDay, string currentPart, int currentRound, int expectedDeadline, bool expired)
    {
        var storage = At(Clock(currentDay, currentPart, currentRound));
        Assert.True(LocationRestoration.TryRestore(storage,
            LocationRestoration.SerializeSnapshot(Scene(deadline), Clock(savedDay, savedPart, savedRound))));
        foreach (var owner in storage.WorldState.Map.Entities!.Cast<CellEntity>().Concat(storage.WorldState.Map.Objects!))
        {
            if (expired) Assert.DoesNotContain(owner.Effects!, e => e.Deleted != true);
            else Assert.Equal(expectedDeadline, Assert.Single(owner.Effects!.Where(e => e.Deleted != true)).ExpiresAtRound);
        }
    }

    [Fact]
    public void LegacyNullTimestampPreservesUnknowableDeadline()
    {
        var snapshot = JsonNode.Parse(LocationRestoration.SerializeSnapshot(Scene(), Clock(2, "morning", 100)))!;
        snapshot["snapshotTime"] = null;
        var storage = At(Clock(8, "evening", 1));
        Assert.True(LocationRestoration.TryRestore(storage, snapshot.ToJsonString()));
        Assert.Equal(120, Assert.Single(storage.WorldState.Map.Entities![0].Effects!).ExpiresAtRound);
        Assert.Equal(120, Assert.Single(storage.WorldState.Map.Objects![0].Effects!).ExpiresAtRound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{\"day\":1,\"partOfDay\":\"morning\"}")]
    [InlineData("{\"day\":1,\"totalRounds\":1}")]
    [InlineData("{\"partOfDay\":\"morning\",\"totalRounds\":1}")]
    [InlineData("{\"day\":0,\"partOfDay\":\"morning\",\"totalRounds\":1}")]
    [InlineData("{\"day\":1,\"partOfDay\":\"invalid\",\"totalRounds\":1}")]
    [InlineData("{\"day\":1,\"partOfDay\":\"morning\",\"totalRounds\":0}")]
    [InlineData("[]")]
    [InlineData("42")]
    public void MissingOrInvalidTimestampDoesNotMutateCurrentState(string? timestamp)
    {
        var snapshot = JsonNode.Parse(LocationRestoration.SerializeSnapshot(Scene(), Clock(2, "morning", 100)))!.AsObject();
        if (timestamp == null) snapshot.Remove("snapshotTime");
        else snapshot["snapshotTime"] = JsonNode.Parse(timestamp);
        var storage = At(Clock(5, "evening", 1));
        storage.WorldState.Map = Scene(300);
        string before = JsonSerializer.Serialize(storage.WorldState);
        Assert.False(LocationRestoration.TryRestore(storage, snapshot.ToJsonString()));
        Assert.Equal(before, JsonSerializer.Serialize(storage.WorldState));
    }

    [Fact]
    public void ExpiredEntityAndObjectCallbacksSurviveReturnAndAreDeliveredOnce()
    {
        var map = Scene();
        foreach (var owner in map.Entities!.Cast<CellEntity>().Concat(map.Objects!))
            owner.Effects![0].OnExpire = new() { Effect = "The ward ends" };
        var storage = At(Clock(5, "evening", 1));
        Assert.True(LocationRestoration.TryRestore(storage,
            LocationRestoration.SerializeSnapshot(map, Clock(2, "morning", 100))));
        var expired = storage.TakeRoundBundle()!.ExpiredEffects;
        Assert.Equal(2, expired.Count);
        Assert.Contains(expired, e => e.OwnerPath == "map.entities" && e.Effect.OnExpire!.Effect == "The ward ends");
        Assert.Contains(expired, e => e.OwnerPath == "map.objects" && e.Effect.OnExpire!.Effect == "The ward ends");
        Assert.All(expired, e => Assert.NotEqual(true, e.Effect.Deleted));
        Assert.Null(storage.TakeRoundBundle());
        storage.WorldState.Time.TotalRounds++;
        Assert.Null(storage.TakeRoundBundle());
    }
}
