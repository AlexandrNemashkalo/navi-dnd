using NaviDnD.Data.Models;
namespace NaviDnD.Tests;

public class StorageAreaRoundTests
{
    private static Storage MakeStorage(int round) => new() { WorldState = { Time = { TotalRounds = round } } };
    private static Hero MakeHero() => new() { Symbol = "HRO", Name = "Герой", Position = [3,3] };
    [Fact]
    public void RemovingEarlierAreaDoesNotRepeatRemainingAreaRoundTrigger()
    {
        var storage = MakeStorage(5);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Map.Area = [new() { Name = "Метка", Positions = [[3,3]] },
            new() { Name = "Туман", Positions = [[3,3]], Triggers = new() { OnRound = new() { Effect = "Проверка" } } }];
        Assert.Single(storage.TakeRoundBundle()!.AreaRoundTriggers);
        storage.ApplyUpdateWorldState("""{"map":{"area":[{"id":0,"deleted":true}]}}""");
        Assert.Null(storage.TakeRoundBundle());
    }

    [Fact]
    public void NewAreaAtReusedIndexStillTriggersThisRound()
    {
        var storage = MakeStorage(5);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Map.Area = [new() { Name = "Туман", Positions = [[3,3]], Triggers = new() { OnRound = new() { Effect = "Проверка" } } }];
        Assert.Single(storage.TakeRoundBundle()!.AreaRoundTriggers);
        storage.ApplyUpdateWorldState("""{"map":{"area":[{"id":0,"deleted":true}]}}""");
        storage.ApplyUpdateWorldState("""{"map":{"area":[{"name":"Пламя","positions":[[3,3]],"triggers":{"onRound":{"effect":"Урон"}}}]}}""");
        Assert.Single(storage.TakeRoundBundle()!.AreaRoundTriggers);
    }

    [Fact]
    public void AreaMetadataPatchKeepsSameRoundDeduplication()
    {
        var storage = MakeStorage(5);
        storage.WorldState.Hero = MakeHero();
        storage.WorldState.Map.Area = [new() { Name = "Туман", Positions = [[3,3]], Triggers = new() { OnRound = new() { Effect = "Проверка" } } }];
        Assert.Single(storage.TakeRoundBundle()!.AreaRoundTriggers);
        storage.ApplyUpdateWorldState("""{"map":{"area":[{"id":0,"name":"Ядовитый туман"}]}}""");
        Assert.Null(storage.TakeRoundBundle());
        storage.WorldState.Time.TotalRounds = 6;
        Assert.Single(storage.TakeRoundBundle()!.AreaRoundTriggers);
    }
}
