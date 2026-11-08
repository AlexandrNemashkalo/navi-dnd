using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class MapRepairTests
{
    [Fact]
    public void NpcPatchedOntoTreeIsMovedToFreeWalkableCell()
    {
        var chunk = new MapChunk { Theme = "forest", Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() };
        chunk.Terrain[2] = "..T" + new string('.', 17);
        var storage = new Storage();
        storage.WorldState.Map = new() { Chunks = [chunk], Entities = [new() { Name = "Rada", Symbol = "RAD", Position = [4,3] }] };
        storage.WorldState.Hero = new() { Position = [2,2] };
        storage.ApplyUpdateWorldState("""{"map":{"entities":[{"id":0,"position":[3,3]}]}}""");
        var pos = storage.WorldState.Map.Entities[0].Position;
        Assert.NotEqual(new[] { 3, 3 }, pos);
        Assert.NotEqual(storage.WorldState.Hero.Position, pos);
        Assert.NotNull(storage.WorldState.Map.TerrainAt(pos[0], pos[1]));
        Assert.False(storage.WorldState.Map.TerrainAt(pos[0], pos[1])!.BlocksMove);
    }

    [Fact]
    public void VisionRestrictionCanBeClearedWithoutLosingDarkvision()
    {
        var storage = new Storage();
        storage.WorldState.Hero = new() { VisionFt = 5, DarkvisionFt = 60 };
        storage.ApplyUpdateWorldState("""{"hero":{"visionFt":null}}""");
        Assert.Null(storage.WorldState.Hero.VisionFt);
        Assert.Equal(60, storage.WorldState.Hero.DarkvisionFt);
        storage.ApplyUpdateWorldState("""{"hero":{"visionFt":0}}""");
        Assert.Equal(0, storage.WorldState.Hero.VisionFt);
    }

    [Fact]
    public void BoundaryStreetGetsOneWorldExitButConnectedStreetDoesNot()
    {
        var chunk = new MapChunk { Theme = "village", Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() };
        chunk.Terrain[0] = ".....=" + new string('.', 14);
        chunk.Terrain[14] = ".....=" + new string('.', 14);
        var map = new MapConfig { Chunks = [chunk, new() { X = 0, Y = 1, Theme = "village" }], Doors = [] };
        LocationGrower.EnsureBoundaryPathExits(map);
        LocationGrower.EnsureBoundaryPathExits(map);
        var door = Assert.Single(map.Doors);
        Assert.True(door.IsWorldExit);
        Assert.Equal(new[] { 6, 1 }, door.From);
        Assert.Equal(new[] { 6, 0 }, door.To);
        Assert.Equal(LocationGrower.WorldExitColor, door.Color);
    }

    [Theory]
    [InlineData("\"name\":\"Cups\",\"aiInfo\":\"Ordinary dishes\"", true)]
    [InlineData("\"positions\":[[2,2]]", false)]
    [InlineData("\"deleted\":true", false)]
    public void PopulationMayDescribeLocalFurnitureButCannotChangeItsGeometry(string fields, bool allowed)
    {
        var chunk = new MapChunk();
        var ws = new WorldState { Map = new() { Chunks = [chunk], Furniture = [new() { Name = "Cabinet", Positions = [[3,3]] }] } };
        var result = new ChunkPopulationQueue.Result(ws.Map, chunk, "{\"map\":{\"furniture\":[{\"id\":0," + fields + "}]}}");
        if (!allowed) { Assert.Throws<InvalidDataException>(() => ChunkPopulationPatch.Prepare(ws, result)); return; }
        var patch = ChunkPopulationPatch.Prepare(ws, result)!;
        var storage = new Storage(); storage.WorldState.Map = ws.Map;
        storage.ApplyUpdateWorldState(patch);
        Assert.Equal("Cups", ws.Map.Furniture[0].Name);
        Assert.Equal(new[] { 3, 3 }, ws.Map.Furniture[0].Positions[0]);
    }
}
