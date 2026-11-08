using System.Text.Json;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class SpellStatsTests
{
    [Theory]
    [InlineData("Сложность спасброска от заклинаний")]
    [InlineData("Сложность спасброска заклинаний")]
    [InlineData("Сложность спасбросков заклинаний")]
    [InlineData("СЛ спасброска от заклинаний")]
    [InlineData("Spell saving throw DC")]
    [InlineData("Spell Save DC (ranger)")]
    public void SpellSaveNamesGetTheSameKeyOnPatchAndLoad(string name)
    {
        string json = JsonSerializer.Serialize(new
        {
            formatVersion = StorageFormat.Current,
            hero = new { stats = new[] { new { name, value = "DC 12" } } }
        });
        var storage = new Storage();
        storage.ApplyUpdateWorldState(json);
        Assert.Equal("DC 12", storage.WorldState.Hero!.Stat(StatKeys.SpellSave)?.Value);
        Assert.Single(storage.WorldState.Hero.Stats!);

        string path = Path.Combine(Path.GetTempPath(), "navidnd-spell-stat-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, json);
            var loaded = new Storage();
            loaded.LoadFrom(path);
            Assert.Equal("DC 12", loaded.WorldState.Hero!.Stat(StatKeys.SpellSave)?.Value);
            Assert.Single(loaded.WorldState.Hero.Stats!);
            Assert.Equal(json, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
