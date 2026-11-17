using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class RiverBuildingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BankBuildingPreservesChannelRoomsAndActualRoutes(bool horizontal)
    {
        int accepted = 0, shifted = 0;
        for (int seed = 1; seed <= 50; seed++)
        {
            var chunk = new MapChunk { Theme = "building", Style = "house", Seed = seed };
            var river = new OutdoorGenerator.RiverSpec(horizontal, horizontal ? 12 : 17, 3, seed);
            Dictionary<ChunkSide, char> bands = [];
            ChunkExit[] exits = [new() { Side = ChunkSide.Left, Offset = 7, External = true },
                new() { Side = ChunkSide.Right, Offset = 7 }, new() { Side = ChunkSide.Top, Offset = 10 }];
            if (!BuildingGenerator.TryRiverFootprintOffset(chunk, seed, bands, river, seed, false, out var offset, out var error))
            {
                Assert.NotNull(error);
                continue;
            }
            accepted++;
            if (offset != default) shifted++;
            Assert.True(BuildingGenerator.TryRiverFootprintOffset(chunk, seed, bands, river, seed, false, out var again, out _));
            Assert.Equal(offset, again);
            var built = BuildingGenerator.Generate(chunk, exits, seed, true, bands, [], footprintOffset: offset, river: river, noiseSeed: seed);
            chunk.Terrain = built.Terrain;
            var state = new WorldState { Map = new() { Chunks = [chunk], Rooms = built.Rooms, Doors = built.Doors, Furniture = built.Furniture } };
            var bed = new char[MapChunk.Cols, MapChunk.Rows];
            OutdoorGenerator.StampRiver(bed, chunk, bands, seed, river, '.');
            for (int c = 0; c < MapChunk.Cols; c++)
                for (int r = 0; r < MapChunk.Rows; r++)
                    if (bed[c,r] is 'W' or '~') Assert.Contains(built.Terrain[r][c], new[] { 'W', '~' });
            foreach (var room in built.Rooms)
                Assert.All(room.Positions, p => Assert.DoesNotContain(bed[p[0]-1,p[1]-1], new[] { 'W', '~' }));
            var entry = exits[0].AnchorCell(chunk);
            var reached = MovementCalculator.Dijkstra(state, entry.col, entry.row, 3000, -1);
            bool Reached(int c, int r) => reached.ContainsKey((c,r,false)) || reached.ContainsKey((c,r,true));
            while (true)
            {
                var doors = built.Doors.Where(d => d.IsDoor == true && d.IsDoorOpen != true
                    && (Reached(d.From[0],d.From[1]) || Reached(d.To[0],d.To[1]))).ToList();
                if (doors.Count == 0) break;
                foreach (var door in doors) door.IsDoorOpen = true;
                reached = MovementCalculator.Dijkstra(state,entry.col,entry.row,3000,-1);
            }
            foreach (var exit in exits)
            {
                var cell = exit.AnchorCell(chunk);
                Assert.True(Reached(cell.col,cell.row), $"exit: seed {seed}, horizontal {horizontal}");
            }
            foreach (var room in built.Rooms)
                foreach (var p in room.Positions.Where(p => state.Map.TerrainAt(p[0],p[1])?.BlocksMove != true))
                    Assert.True(Reached(p[0],p[1]), $"room: seed {seed}, horizontal {horizontal}");
            foreach (var furniture in built.Furniture)
                Assert.True(furniture.Positions.Any(p => new[] { (p[0]-1,p[1]), (p[0]+1,p[1]), (p[0],p[1]-1), (p[0],p[1]+1) }
                    .Any(n => Reached(n.Item1,n.Item2))), $"furniture: seed {seed}, horizontal {horizontal}");
            var upper = new MapChunk { Theme = "building", Style = "house", Z = 1, Seed = seed + 100 };
            var upstairs = BuildingGenerator.Generate(upper, [], seed, false, bands, [], footprintOffset: offset);
            Assert.Equal(built.Rooms.SelectMany(r => r.Positions).Select(p => (p[0],p[1])).Order(),
                upstairs.Rooms.SelectMany(r => r.Positions).Select(p => (p[0],p[1])).Order());
        }
        Assert.True(accepted >= 10, $"Only {accepted} bank placements fit.");
        Assert.True(shifted > 0);
    }

    [Fact]
    public void ShiftFitsEveryFloorEvenWhenCallerOmitsGroundStyle()
    {
        int accepted = 0;
        for (int seed = 1; seed <= 30; seed++)
        {
            var ground = new MapChunk { Theme = "building", Style = "house", Seed = seed };
            Dictionary<ChunkSide,char> bands = [];
            if (!BuildingGenerator.TryRiverFootprintOffset(ground,seed,bands,new(true,12,3,seed),seed,false,
                out var offset,out _,["manor"])) continue;
            accepted++;
            foreach (string style in new[] { "house", "manor" })
            {
                var floor = new MapChunk { Theme = "building", Style = style, Seed = seed, Z = 1 };
                var built = BuildingGenerator.Generate(floor,[],seed,false,bands,[],footprintOffset:offset);
                Assert.All(built.Rooms.SelectMany(r => r.Positions), p =>
                { Assert.InRange(p[0],1,MapChunk.Cols); Assert.InRange(p[1],1,MapChunk.Rows); });
            }
        }
        Assert.True(accepted > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImpossibleRiverPlanPreservesEntireState(bool moat)
    {
        var state = new WorldState { Hero = new() { Position = [4,5] }, Map = new()
        {
            DefaultImage = "existing-scene", TriggerCells = [new() { Position = [3,3] }],
            Rooms = [new() { Name = "Existing room", Positions = [[4,5]] }]
        } };
        string before = JsonSerializer.Serialize(state);
        var previous = state.Map;
        var result = LocationGrower.CreatePlanned(state,
            [new(0,0,"Existing house","building",Building:"house",Moat:moat)],
            site: new("forest",true,50,[]));
        Assert.Null(result.start);
        Assert.NotNull(result.error);
        Assert.Same(previous,state.Map);
        Assert.Equal(before,JsonSerializer.Serialize(state));
    }

    [Fact]
    public void BasementBelowOutdoorRiverDoesNotRequireDryBuildingFootprint()
    {
        var state = new WorldState { Hero = new() { Position = [4,5] } };
        var result = LocationGrower.CreatePlanned(state,
            [new(0,0,"Riverbank","forest"), new(0,0,"Cellar","building",Z:-1,Building:"house")],
            site: new("forest",true,30,[]));
        Assert.Null(result.error);
        Assert.NotNull(result.start);
        Assert.Equal(2,state.Map.Chunks!.Count);
        Assert.NotEmpty(state.Map.Stairs!);
    }
}
