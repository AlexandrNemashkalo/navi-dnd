using System.Reflection;
using System.Text.Json;

namespace NaviDnD.Tests;

public class ResponsePreflightTests
{
    [Theory]
    [InlineData("{\"playerNote\":\"The old bridge\"}")]
    [InlineData("{\"playerNote\":[\"The old bridge\",null,\"Ask the keeper\"]}")]
    [InlineData("{\"history\":[{\"patch\":{\"playerNote\":null}}]}")]
    public void ValidNotesPassPureValidationWithoutWritingThem(string response)
    {
        var type = typeof(AppConfig).Assembly.GetType("NaviDnD.Clients.SequentialHistoryPlayer")!;
        var validate = type.GetMethod("ValidateResponse", BindingFlags.NonPublic | BindingFlags.Static,
            null, [typeof(string)], null)!;
        validate.Invoke(null, [response]);
    }

    private static Task Play(Storage storage, string response, Action? beforeApply = null)
    {
        var type = typeof(AppConfig).Assembly.GetType("NaviDnD.Clients.SequentialHistoryPlayer")!;
        var player = Activator.CreateInstance(type, storage.WorldState, storage, new JsonSerializerOptions(), null, null, null, null)!;
        return (Task)type.GetMethod("PlayValidatedAsync")!.Invoke(player, [response, beforeApply])!;
    }

    private static Storage WithFood()
    {
        var storage = new Storage();
        storage.ApplySavedState("""
            {"hero":{"name":"Traveler","hp":"10/10","inventory":[{"name":"Rations","quantity":5}]},"pendingTravel":"Two days on the road"}
            """);
        return storage;
    }

    [Theory]
    [InlineData("{\"text\":42}")]
    [InlineData("{\"author\":[]}")]
    [InlineData("{\"patch\":{\"playerNote\":[\"The old bridge\",42]}}")]
    [InlineData("{\"patch\":{\"PlayerNote\":{\"text\":\"Wrong shape\"}}}")]
    [InlineData("null")]
    [InlineData("{\"patch\":{\"map\":42}}")]
    [InlineData("{\"patch\":{\"map\":{\"entities\":[{\"id\":0,\"movePath\":[[1,2],[2,\"bad\"]]}]}}}")]
    [InlineData("{\"patch\":{\"map\":{\"entities\":[{\"id\":0,\"movePath\":[[1]]}]}}}")]
    public async Task LaterMalformedHistoryRejectsBeforeRootFoodOrEarlierHistoryPatch(string malformed)
    {
        var storage = WithFood();
        string before = JsonSerializer.Serialize(storage.WorldState);
        bool accepted = false;
        string response = "{\"hero\":{\"inventory\":[{\"id\":0,\"quantity\":3}]},\"history\":[{\"text\":\"First\",\"patch\":{\"hero\":{\"hp\":\"9/10\"}}}," + malformed + "]}";
        await Assert.ThrowsAsync<JsonException>(() => Play(storage, response, () => accepted = true));
        Assert.False(accepted);
        Assert.Equal(before, JsonSerializer.Serialize(storage.WorldState));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{\"history\":null}")]
    [InlineData("{\"history\":{}}")]
    [InlineData("{\"playerNote\":[\"The old bridge\",false]}")]
    public async Task InvalidResponseEnvelopeDoesNotAcknowledgePendingTravel(string response)
    {
        var storage = WithFood();
        string before = JsonSerializer.Serialize(storage.WorldState);
        await Assert.ThrowsAsync<JsonException>(() => Play(storage, response, () => storage.WorldState.PendingTravel = null));
        Assert.Equal(before, JsonSerializer.Serialize(storage.WorldState));
    }

    [Fact]
    public async Task AcceptedResponseInvokesHookOnceBeforeApplyingFoodAndAllowsNullableText()
    {
        var storage = WithFood();
        storage.WorldState.Map.Entities = [new() { Name = "Watcher", Symbol = "NPC", Position = [2, 2], Hp = "5/5" }];
        int calls = 0;
        await Play(storage, """
            {"hero":{"inventory":[{"id":0,"quantity":3}]},"history":[{"text":null,"author":null,"patch":null},{"patch":{"map":{"entities":[{"id":0,"movePath":null}]}}}]}
            """, () =>
            {
                calls++;
                Assert.Equal(5, storage.WorldState.Hero!.Inventory[0].Quantity);
                storage.WorldState.PendingTravel = null;
            });
        Assert.Equal(1, calls);
        Assert.Null(storage.WorldState.PendingTravel);
        Assert.Equal(3, storage.WorldState.Hero!.Inventory[0].Quantity);
        Assert.Equal(new[] { 2, 2 }, Assert.Single(storage.WorldState.Map.Entities!).Position);
    }
}
