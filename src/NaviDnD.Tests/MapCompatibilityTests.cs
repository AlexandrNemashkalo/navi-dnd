using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class MapCompatibilityTests
{
    [Fact]
    public void SavedGeometryAndPlayerChangesSurviveSerializationWithoutRegeneration()
    {
        // A saved layout is authoritative, not a recipe to rebuild from the current seed algorithm.
        var saved = new MapConfig
        {
            Chunks = [new() { Theme = "village", Seed = 1, Populated = true,
                Terrain = Enumerable.Repeat(new string('.', MapChunk.Cols), MapChunk.Rows).ToList() }],
            Rooms = [new() { Name = "Player-renamed workshop", Positions = [[4, 5], [5, 5], [5, 6]], Color = [31, 42, 53] }],
            Doors = [new() { From = [4, 5], To = [3, 5], IsDoor = true, IsDoorOpen = false }],
            Furniture = [new() { Kind = "table", Name = "Moved table", Positions = [[5, 6]], State = "searched", Deleted = true }],
        };
        saved.Chunks[0].Terrain![0] = "=T..................";
        string json = JsonSerializer.Serialize(saved);
        var loaded = JsonSerializer.Deserialize<MapConfig>(json)!;
        Assert.Equal(json, JsonSerializer.Serialize(loaded));
        Assert.Equal("=T..................", loaded.Chunks![0].Terrain![0]);
        Assert.True(loaded.Chunks[0].Populated);
        Assert.Equal("Player-renamed workshop", Assert.Single(loaded.Rooms!).Name);
        Assert.False(Assert.Single(loaded.Doors!).IsDoorOpen);
        Assert.True(Assert.Single(loaded.Furniture!).Deleted);
        Assert.Equal("searched", loaded.Furniture![0].State);
        Assert.Null(loaded.FurnitureAt(5, 6));
        Assert.Equal("Player-renamed workshop", loaded.RoomAt(5, 6)!.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshGenerationRemainsDeterministic(bool village)
    {
        var distinct = new HashSet<string>();
        for (int seed = 1; seed <= 5; seed++)
        {
            var chunk = new MapChunk { Seed = seed, Theme = village ? "village" : "building", Style = "tavern" };
            var bands = new Dictionary<ChunkSide, char>();
            BuildingGenerator.Result Generate() => village ? BuildingGenerator.GenerateVillage(chunk, MapSampleTests.Exits, bands)
                : BuildingGenerator.Generate(chunk, MapSampleTests.Exits, seed, true, bands, []);
            string first = JsonSerializer.Serialize(Generate());
            Assert.Equal(first, JsonSerializer.Serialize(Generate()));
            distinct.Add(first);
        }
        Assert.Equal(5, distinct.Count);
    }
}
