using System.Text.Json;
using NaviDnD.Clients;

namespace NaviDnD.PromptTests;

// No model calls: regression coverage for the real-scenario harness.
public class PromptScenarioTests
{
    [Fact]
    public void StreamedToolHistoryAppliesPatchesInOrder()
    {
        var storage = new Storage();
        storage.ApplySavedState("""{"hero":{"symbol":"HRO","hp":"10/10"}}""");
        PromptScenario.ApplyToolHistory(storage,
        [new() { Text = "Первый удар", Patch = JsonDocument.Parse("""{"hero":{"hp":"7/10"}}""").RootElement },
         new() { Text = "Второй удар", Patch = JsonDocument.Parse("""{"hero":{"hp":"5/10"}}""").RootElement }]);
        Assert.Equal("5/10", storage.WorldState.Hero!.Hp);
    }
}
