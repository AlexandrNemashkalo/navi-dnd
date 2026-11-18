using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class MapTraversalTests
{
    [Theory]
    [InlineData("road")]
    [InlineData("square")]
    [InlineData("palisade")]
    [InlineData("river")]
    public void VillageInteriorsAndFurnitureRemainAccessibleThroughActualMovement(string layout)
    {
        for (int seed = 1; seed <= 50; seed++)
        {
            var chunk = new MapChunk { Theme = "village", Seed = seed };
            ChunkExit[] exits = [new() { Side = ChunkSide.Left, Offset = 7, External = true },
                new() { Side = ChunkSide.Right, Offset = 7 }];
            var result = BuildingGenerator.GenerateVillage(chunk, exits,
                Enum.GetValues<ChunkSide>().ToDictionary(s => s, _ => 'T'), layout,
                layout == "river" ? new(true, 8, 3, seed) : null, seed);
            chunk.Terrain = result.Terrain;
            var state = new WorldState { Map = new() { Chunks = [chunk], Rooms = result.Rooms,
                Doors = result.Doors, Furniture = result.Furniture } };
            var entry = exits[0].AnchorCell(chunk);
            var reached = MovementCalculator.Dijkstra(state, entry.col, entry.row, 3000, -1);
            bool Reached(int c, int r) => reached.ContainsKey((c, r, false)) || reached.ContainsKey((c, r, true));
            // Open only doors the explorer can actually approach, rather than granting global access.
            while (true)
            {
                var approachable = result.Doors.Where(d => d.IsDoor == true && d.IsDoorOpen != true
                    && (Reached(d.From[0], d.From[1]) || Reached(d.To[0], d.To[1]))).ToList();
                if (approachable.Count == 0) break;
                foreach (var door in approachable) door.IsDoorOpen = true;
                reached = MovementCalculator.Dijkstra(state, entry.col, entry.row, 3000, -1);
            }
            foreach (var room in result.Rooms)
                foreach (var p in room.Positions)
                    if (state.Map.TerrainAt(p[0], p[1])?.BlocksMove != true)
                        Assert.True(Reached(p[0], p[1]), $"{layout} seed {seed}: room {room.Name} cell {p[0]},{p[1]} unreachable");
            foreach (var furniture in result.Furniture)
                Assert.True(furniture.Positions.Any(p => new[] { (p[0]-1,p[1]), (p[0]+1,p[1]), (p[0],p[1]-1), (p[0],p[1]+1) }
                    .Any(cell => Reached(cell.Item1, cell.Item2)
                        && state.Map.RoomAt(cell.Item1, cell.Item2) == state.Map.RoomAt(p[0], p[1]))),
                    $"{layout} seed {seed}: inaccessible {furniture.Kind}");
            var departure = exits[1].AnchorCell(chunk);
            Assert.True(Reached(departure.col, departure.row));
        }
    }

    [Theory]
    [InlineData("dungeon")]
    [InlineData("cave")]
    public void UndergroundRoutesKeepEveryRoomAndExitReachable(string theme)
    {
        for (int seed = 1; seed <= 100; seed++)
        {
            var chunk = new MapChunk { Theme = theme, Seed = seed };
            var result = ChunkGenerator.Generate(chunk, MapSampleTests.Exits);
            chunk.Terrain = DungeonDecorator.Decorate(chunk, result.Rooms, result.Doors);
            foreach (var door in result.Doors) door.IsDoorOpen = true;
            var ws = new WorldState { Map = new() { Chunks = [chunk], Rooms = result.Rooms, Doors = result.Doors } };
            var start = MapSampleTests.Exits[0].AnchorCell(chunk);
            var reached = MovementCalculator.Dijkstra(ws, start.col, start.row, 3000, -1);
            foreach (var room in result.Rooms)
                foreach (var cell in room.Positions.Where(p => ws.Map.TerrainAt(p[0], p[1])?.BlocksMove != true))
                    Assert.True(reached.ContainsKey((cell[0], cell[1], false)) || reached.ContainsKey((cell[0], cell[1], true)),
                        $"{theme} seed {seed}: room cell {cell[0]},{cell[1]} is unreachable");
            foreach (var exit in MapSampleTests.Exits)
            {
                var cell = exit.AnchorCell(chunk);
                Assert.True(reached.ContainsKey((cell.col, cell.row, false)) || reached.ContainsKey((cell.col, cell.row, true)));
            }
        }
    }

    [Theory]
    [InlineData('^')]
    [InlineData('F')]
    public void CreatureCannotBypassSolidBarrierThroughUngeneratedField(char obstacle)
    {
        var chunk = new MapChunk { X = 1, Y = 1, Theme = "forest",
            Terrain = Enumerable.Repeat("........." + new string(obstacle, 2) + ".........", MapChunk.Rows).ToList() };
        var world = new WorldState { Hero = new() { Symbol = "HRO", Position = [38,23] },
            Map = new() { Cols = 100, Rows = 100, Chunks = [chunk],
                Entities = [new() { Symbol = "NPC", Position = [24,23], SpeedMax = 300 }] } };
        var result = MovementCalculator.Calculate(world, new() { EntityId = 0,
            Intents = [new() { Intent = "approach", Target = "hero" }] });
        Assert.Equal("stay", result.UsedIntent);
        Assert.Equal(new[] { 24,23 }, Assert.Single(result.Steps));
    }

    [Fact]
    public void CreatureCrossesWaterWithinGeneratedTerrain()
    {
        var chunk = new MapChunk { X = 1, Y = 1, Theme = "forest",
            Terrain = Enumerable.Repeat(".........WW.........", MapChunk.Rows).ToList() };
        var world = new WorldState { Hero = new() { Symbol = "HRO", Position = [38,23] },
            Map = new() { Cols = 100, Rows = 100, Chunks = [chunk],
                Entities = [new() { Symbol = "NPC", Position = [24,23], SpeedMax = 300 }] } };
        var result = MovementCalculator.Calculate(world, new() { EntityId = 0,
            Intents = [new() { Intent = "approach", Target = "hero" }] });
        Assert.Equal("approach", result.UsedIntent);
        Assert.All(result.Steps, p => Assert.True(chunk.Contains(p[0], p[1])));
        Assert.Contains(result.Steps, p => p[0] == 30);
        Assert.Contains(result.Steps, p => p[0] == 31);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitAreasRemainWalkableButDeletedAreasDoNot(bool deleted)
    {
        var world = new WorldState { Map = new() { Cols = 100, Rows = 100,
            Chunks = [new() { Theme = "forest", Terrain = Enumerable.Repeat(new string('.',20),15).ToList() }],
            Area = [new() { Positions = [[21,7]], Deleted = deleted }] } };
        Assert.Equal(!deleted, MovementCalculator.CanMoveTo(world, 20,7,21,7,-1));
    }

    public static IEnumerable<object[]> RiverSeams() =>
        from theme in new[] { "forest", "swamp", "hills" }
        from horizontal in new[] { false, true }
        from along in new[] { false, true }
        select new object[] { theme, horizontal, along };

    [Theory, MemberData(nameof(RiverSeams))]
    public void AdjacentRiverChunksAreExplorableWithoutLeavingGeneratedGround(string theme, bool horizontal, bool along)
    {
        for (int seed = 1; seed <= 30; seed++)
        {
            var left = new MapChunk { X = 1, Y = 1, Theme = theme, Seed = seed };
            bool east = horizontal == along;
            var right = new MapChunk { X = east ? 2 : 1, Y = east ? 1 : 2, Theme = theme, Seed = seed + 100 };
            var forward = east ? ChunkSide.Right : ChunkSide.Top;
            var backward = east ? ChunkSide.Left : ChunkSide.Bottom;
            int offset = east ? 7 : 10;
            ChunkExit[] leftExits = [new() { Side = backward, Offset = offset, External = true }, new() { Side = forward, Offset = offset }];
            ChunkExit[] rightExits = [new() { Side = backward, Offset = offset }];
            int center = horizontal ? left.OriginRow + (along ? 8 : MapChunk.Rows)
                : left.OriginCol + (along ? 11 : MapChunk.Cols);
            var river = new OutdoorGenerator.RiverSpec(horizontal, center, 4, seed);
            var leftBands = Enum.GetValues<ChunkSide>().Where(side => side != forward).ToDictionary(side => side, _ => 'F');
            var rightBands = Enum.GetValues<ChunkSide>().Where(side => side != backward).ToDictionary(side => side, _ => 'F');
            left.Terrain = OutdoorGenerator.Generate(left, leftExits, leftBands, seed, river).Terrain;
            right.Terrain = OutdoorGenerator.Generate(right, rightExits, rightBands, seed, river).Terrain;
            var world = new WorldState { Map = new() { Cols = 100, Rows = 100, Chunks = [left, right] } };
            var start = leftExits[0].AnchorCell(left);
            var reached = MovementCalculator.Dijkstra(world, start.col, start.row, 3000, -1);
            Assert.All(reached.Keys, cell => Assert.NotNull(world.Map.TerrainAt(cell.col, cell.row)));
            foreach (var chunk in new[] { left, right })
                for (int c = 1; c <= MapChunk.Cols; c++)
                    for (int r = 1; r <= MapChunk.Rows; r++)
                    {
                        int fc = chunk.OriginCol + c, fr = chunk.OriginRow + r;
                        if (world.Map.TerrainAt(fc,fr) is { BlocksMove: false })
                            Assert.True(reached.ContainsKey((fc,fr,false)) || reached.ContainsKey((fc,fr,true)), $"{theme} seed {seed}: [{fc},{fr}] unreachable");
                    }
        }
    }
}
