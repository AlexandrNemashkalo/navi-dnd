using System.Text.Json;
using System.Reflection;
using NaviDnD.Data;
using NaviDnD.Display;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class LocationLandscapeTests
{
    [Fact]
    public void SettlementHasQuietSurroundingsAndAtlasRiverSouthOfNorthernRoad()
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var state = new WorldState { Hero = new() };
            var site = new LocationGrower.WorldSite("forest", true, 2, [], RiverOffset: -0.72,
                RoadHorizontal: true, RoadOffset: 0.72, RoadSides: [ChunkSide.Left, ChunkSide.Right, ChunkSide.Top]);
            var (start, error) = LocationGrower.CreatePlanned(state,
                [new(0, 0, "A working village", "village")], site: site, includeSurroundings: true);
            Assert.Null(error);
            Assert.NotNull(start);
            Assert.Equal(9, state.Map.Chunks!.Count);
            Assert.False(start.Populated);
            Assert.All(state.Map.Chunks.Where(c => c != start), c => Assert.True(c.Populated));
            var water = Cells(state.Map, 'W').ToList();
            Assert.NotEmpty(water);
            Assert.True(water.Average(p => p.r) < start.OriginRow);
            foreach (var door in state.Map.Doors!.Where(d => d.IsWorldExit != true && d.IsWindow != true)) door.IsDoorOpen = true;
            var reached = MovementCalculator.Dijkstra(state, state.Hero!.Position![0], state.Hero.Position[1], 10000, -1);
            foreach (var chunk in state.Map.Chunks)
                foreach (var exit in chunk.Exits)
                {
                    var p = exit.AnchorCell(chunk);
                    Assert.True(reached.ContainsKey((p.col, p.row, false)) || reached.ContainsKey((p.col, p.row, true)),
                        $"Unreachable approach {p} in attempt {attempt}");
                }
            foreach (var side in new[] { ChunkSide.Left, ChunkSide.Right, ChunkSide.Top })
            {
                var roadExits = state.Map.Chunks.SelectMany(c => c.Exits.Where(e => e.External && e.Side == side)
                    .Select(e => e.AnchorCell(c))).ToList();
                Assert.NotEmpty(roadExits);
                if (side != ChunkSide.Top) Assert.All(roadExits, p => Assert.True(p.row > start.OriginRow + MapChunk.Rows));
            }
            // Expanded terrain is stored, not reconstructed from a potentially changed generator on load.
            var restored = JsonSerializer.Deserialize<MapConfig>(JsonSerializer.Serialize(state.Map))!;
            Assert.Equal(state.Map.Chunks.SelectMany(c => c.Terrain!), restored.Chunks!.SelectMany(c => c.Terrain!));
            if (attempt == 0) ExportReview(state, start);
        }
    }

    [Theory]
    [InlineData('.', true)]
    [InlineData('T', false)]
    [InlineData('^', false)]
    [InlineData('W', true)]
    public void NaturalBoundaryAllowsLeavingOnlyFromWalkableGround(char code, bool expected)
    {
        var chunk = new MapChunk { Theme = "forest", Terrain = Enumerable.Repeat(new string(code, 20), 15).ToList() };
        var map = new MapConfig { Chunks = [chunk] };
        Assert.Equal(expected, LocationGrower.IsOpenWorldBoundary(map, 1, 7, 0, 7));
        Assert.False(LocationGrower.IsOpenWorldBoundary(map, 2, 7, 1, 7));
        map.Doors = [new() { From = [1, 7], To = [0, 7], Hidden = true }];
        Assert.False(LocationGrower.IsOpenWorldBoundary(map, 1, 7, 0, 7));
    }

    [Fact]
    public void QuietExpansionKeepsStackedFloorsAndDoesNotMutateRequestedPlan()
    {
        var state = new WorldState { Hero = new() };
        List<LocationGrower.PlannedBlock> plan = [new(0, 0, "Workshop", "building", Building: "house"),
            new(0, 0, "Home", "building", Z: 1, Building: "house")];
        var (start, error) = LocationGrower.CreatePlanned(state, plan,
            site: new("plains", null, 0, []), includeSurroundings: true);
        Assert.Null(error);
        Assert.NotNull(start);
        Assert.Equal(2, plan.Count);
        Assert.Single(state.Map.Chunks!.Where(c => c.Z == 1));
        Assert.NotEmpty(state.Map.Stairs!);
        Assert.True(state.Map.Chunks.Count > 2);
    }

    private static IEnumerable<(int c, int r)> Cells(MapConfig map, char code)
    {
        foreach (var chunk in map.Chunks!)
            for (int r = 0; r < (chunk.Terrain?.Count ?? 0); r++)
                for (int c = 0; c < chunk.Terrain![r].Length; c++)
                    if (chunk.Terrain[r][c] == code) yield return (chunk.OriginCol + c + 1, chunk.OriginRow + r + 1);
    }

    private static void ExportReview(WorldState state, MapChunk center)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "scripts"))) root = root.Parent;
        if (root == null) return;
        string folder = Path.Combine(root.FullName, "artifacts", "landscape-review");
        Directory.CreateDirectory(folder);
        var storage = new Storage();
        var display = new DisplayConfig();
        var config = new AppConfig { RevealMap = true, SaveExploredCells = false };
        foreach (var (name, dy) in new[] { ("village", 0), ("northern-road", 15), ("southern-river", -15) })
        {
            state.Hero!.Position = [center.OriginCol + 10, center.OriginRow + 7 + dy];
            var provider = new MapObjectsProvider(state, display, storage, config);
            var view = new MapDisplay(state, display, new LegendDisplay(storage, display, config));
            view.CenterOnHeroIfNeeded(force: true);
            var draws = (List<Action>)typeof(MapDisplay).GetMethod("BuildLocationRows", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [provider])!;
            var rows = new List<string>();
            var previous = Console.Out;
            try
            {
                foreach (var draw in draws)
                {
                    using var writer = new StringWriter();
                    Console.SetOut(writer); draw(); rows.Add(writer.ToString());
                }
            }
            finally { Console.SetOut(previous); }
            File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(rows));
        }
    }
}
