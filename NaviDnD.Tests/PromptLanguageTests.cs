using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

public class PromptLanguageTests
{
    [Fact]
    public void ActionContextDistinguishesLocationAndWorldAxes()
    {
        var state = new WorldState { Hero = new() { Position = [51, 37] } };
        string context = new NaviDnD.Clients.AiContextBuilder(state).MinimalState("look around");
        Assert.Contains("Location [col,row]: north/up=+row, south/down=-row", context);
        Assert.Contains("world [x,y]: north=-y, south=+y", context);
        Assert.Contains("position:[51,37]", context);
    }

    [Fact]
    public void SharedPromptsAndToolDescriptionsAreEnglish()
    {
        var files = Directory.GetFiles(Path.Combine(RepoRoot(), "NaviDnD", "Prompts", "Dnd5e"), "systemPrompt.md", SearchOption.AllDirectories);
        Assert.Equal(9, files.Length);
        foreach (var file in files)
        {
            string prompt = File.ReadAllText(file);
            Assert.DoesNotMatch("[А-Яа-яЁё]", prompt);
            Assert.Contains("lang", prompt);
        }
        var descriptions = typeof(RollDiceTools).Assembly.GetTypes()
            .Where(t => t.Namespace == "NaviDnD.McpServer")
            .SelectMany(t => t.GetMethods())
            .SelectMany(m => m.GetCustomAttributes<DescriptionAttribute>()
                .Concat(m.GetParameters().SelectMany(p => p.GetCustomAttributes<DescriptionAttribute>()))).ToList();
        Assert.NotEmpty(descriptions);
        foreach (var description in descriptions)
            Assert.DoesNotMatch("[А-Яа-яЁё]", description.Description);
    }

    [Theory]
    [InlineData("north", 0, -1)]
    [InlineData("south", 0, 1)]
    [InlineData("east", 1, 0)]
    [InlineData("west", -1, 0)]
    [InlineData("north-east", 1, -1)]
    [InlineData("southwest", -1, 1)]
    public void EnglishDirectionGuidesPlaceSelection(string direction, int dx, int dy)
    {
        var map = FlatMap();
        var spot = WorldAtlas.FindSpot(map, new("cave", new() { X = 15, Y = 15 }, direction, 3, null), [], new HashSet<int>());
        Assert.NotNull(spot);
        // The placement score includes small random variation; cardinal bearings may shift one cell sideways.
        if (dx == 0) Assert.InRange(Math.Abs(spot.Value.x - 15), 0, 1);
        else Assert.Equal(dx, Math.Sign(spot.Value.x - 15));
        if (dy == 0) Assert.InRange(Math.Abs(spot.Value.y - 15), 0, 1);
        else Assert.Equal(dy, Math.Sign(spot.Value.y - 15));
    }

    [Fact]
    public void EnglishTerrainGuidesPlaceSelection()
    {
        var map = FlatMap();
        map.Biomes = Enumerable.Range(0, 31).Select(_ => new string(WorldBiomes.Forest, 10) + new string(WorldBiomes.Plains, 21)).ToList();
        var spot = WorldAtlas.FindSpot(map, new("cave", null, null, null, "forest"), [], new HashSet<int>());
        Assert.NotNull(spot);
        Assert.InRange(spot.Value.x, 1, 10);
    }

    private static WorldMap FlatMap() => new()
    {
        Width = 31, Height = 31,
        Biomes = Enumerable.Repeat(new string(WorldBiomes.Plains, 31), 31).ToList(),
        Rivers = Enumerable.Repeat(new string('0', 31), 31).ToList(),
        Roads = Enumerable.Repeat(new string('0', 31), 31).ToList(),
        Owners = Enumerable.Repeat(new string('.', 31), 31).ToList()
    };

    private static string RepoRoot([CallerFilePath] string self = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(self)!, ".."));
}
