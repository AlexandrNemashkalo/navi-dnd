using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class MapRenderingTests
{
    [Theory]
    [InlineData("forest", true)]
    [InlineData("house", true)]
    [InlineData("tavern", true)]
    [InlineData("temple", true)]
    [InlineData("furniture", true)]
    [InlineData("doors", true)]
    [InlineData("trees", true)]
    [InlineData("world-exits", true)]
    [InlineData("trees", false)]
    [InlineData("stone-landscape", true)]
    [InlineData("stone-landscape", false)]
    [InlineData("manor", true)]
    [InlineData("house", true, false, null, 17)]
    [InlineData("manor", true, false, null, 42)]
    [InlineData("forest", false)]
    [InlineData("swamp", true)]
    [InlineData("village", true)]
    [InlineData("forest", true, true)]
    [InlineData("village", true, true)]
    [InlineData("plains", true)]
    [InlineData("hills", true)]
    [InlineData("dungeon", true)]
    [InlineData("cave", true)]
    [InlineData("cave", true, false, null, 17)]
    [InlineData("cave", true, false, null, 42)]
    [InlineData("dungeon", true, false, null, 17)]
    [InlineData("dungeon", true, false, null, 42)]
    [InlineData("village", true, false, "square")]
    [InlineData("village", true, false, "palisade")]
    [InlineData("forest", true, false, null, 17)]
    [InlineData("forest", true, false, null, 42)]
    [InlineData("forest", true, false, null, 23)]
    [InlineData("hills", true, false, null, 147)]
    [InlineData("village", true, false, "road", 17)]
    [InlineData("village", true, false, "road", 42)]
    [InlineData("village", true, false, "road", 9)]
    [InlineData("desert", true)]
    [InlineData("taiga", true)]
    [InlineData("taiga", true, false, null, 17)]
    [InlineData("taiga", true, false, null, 42)]
    [InlineData("snow", true)]
    [InlineData("snow", true, false, null, 17)]
    [InlineData("snow", true, false, null, 42)]
    [InlineData("taiga", false)]
    [InlineData("snow", false)]
    [InlineData("village", true, true, "river", 2, true, true)]
    [InlineData("village", true, true, "river", 3, true, false)]
    public void ActualMapRowsKeepTheirWidthAndExportForVisualReview(string theme, bool reveal, bool river = false, string? layout = null, int seed = 3, bool crossroads = false, bool horizontalRiver = true)
    {
        var storage = new Storage();
        var state = storage.WorldState;
        var chunk = new MapChunk { Theme = theme is "house" or "manor" or "tavern" or "temple" ? "building" : theme, Style = theme is "house" or "manor" or "tavern" or "temple" ? theme : null, Seed = seed };
        ChunkExit[] exits = [new() { Side = ChunkSide.Left, Offset = 7, External = true }, new() { Side = ChunkSide.Right, Offset = 7 }];
        if (crossroads) exits = [..exits, new() { Side = ChunkSide.Top, Offset = 10 }, new() { Side = ChunkSide.Bottom, Offset = 10 }];
        var bands = Enum.GetValues<ChunkSide>().ToDictionary(side => side, _ => 'T');
        if (theme == "world-exits")
        {
            chunk.Terrain = Enumerable.Repeat(new string('=', MapChunk.Cols), MapChunk.Rows).ToList();
            chunk.Terrain[0] = "TTTTTTTTT=TTTTTTTTTT";
            state.Map.Doors = [
                new() { From = [1,7], To = [0,7], IsWorldExit = true, IsEntry = true, IsDoor = true },
                new() { From = [20,7], To = [21,7], IsWorldExit = true, IsEntry = true, IsDoor = true },
                new() { From = [10,1], To = [10,0], IsWorldExit = true, IsEntry = true, IsDoor = true },
                new() { From = [10,15], To = [10,16], IsWorldExit = true, IsEntry = true, IsDoor = true }
            ];
        }
        else if (theme == "stone-landscape")
        {
            var cells=Enumerable.Range(0,MapChunk.Rows).Select(_=>new string('.',MapChunk.Cols).ToCharArray()).ToArray();
            for(int y=3;y<12;y++) for(int x=3;x<17;x++)
            {
                double radius=Math.Pow((x-10)/7d,2)+Math.Pow((y-7)/4.5,2);
                if(radius<1) cells[y][x]=radius>.65?'s':'g';
            }
            foreach(var (x,y) in new[]{(5,6),(6,6),(5,7),(6,7),(7,7),(6,8),(7,8),(8,8),
                (12,6),(13,6),(12,7),(13,7),(14,7),(13,8),(15,5),(4,9)}) cells[y][x]='o';
            chunk.Terrain=cells.Select(row=>new string(row)).ToList();
        }
        else if (theme == "trees")
        {
            var cells = Enumerable.Range(0,MapChunk.Rows).Select(_ => new string('.',MapChunk.Cols).ToCharArray()).ToArray();
            foreach (var (c,r) in new[] { (3,3),(6,3),(7,3),(10,3),(11,4),(3,7),(4,7),(3,8),(4,8),(12,9),(13,9),(12,10),(14,11) }) cells[r-1][c-1] = 'T';
            cells[11][0] = 'T'; cells[14][5] = 'T';
            foreach (var (c,r) in new[] { (6,11),(7,11),(6,12),(7,12),(8,13) }) cells[r-1][c-1]='F';
            chunk.Terrain = cells.Select(row => new string(row)).ToList();
            state.Map.Rooms = [new() { Color = [74,60,46], Positions = Enumerable.Range(14,4)
                .SelectMany(c => Enumerable.Range(8,3).Select(r => new List<int> { c,r })).ToList() }];
            state.Map.Doors = [new() { From = [14,9], To = [13,9], IsDoor = true, Color = [180,130,70] }];
        }
        else if (theme == "doors")
        {
            chunk.Terrain = Enumerable.Repeat(new string(' ',MapChunk.Cols),MapChunk.Rows).ToList();
            state.Map.Rooms = [];
            state.Map.Doors = [];
            foreach (bool open in new[] { false,true })
            foreach (bool pair in new[] { false,true })
            {
                int c0 = open ? 12 : 2, r0 = pair ? 2 : 9;
                state.Map.Rooms.Add(new() { Color = [74,60,46], Positions = Enumerable.Range(c0,6)
                    .SelectMany(c => Enumerable.Range(r0,5).Select(r => new List<int> { c,r })).ToList() });
                for (int i = 0; i < (pair ? 2 : 1); i++)
                {
                    state.Map.Doors.Add(new() { From = [c0+2+i,r0+4], To = [c0+2+i,r0+5], IsDoor = true, IsDoorOpen = open });
                    state.Map.Doors.Add(new() { From = [c0+5,r0+1+i], To = [c0+6,r0+1+i], IsDoor = true, IsDoorOpen = open });
                }
            }
        }
        else if (theme == "furniture")
        {
            chunk.Terrain = Enumerable.Repeat(new string('w', MapChunk.Cols), MapChunk.Rows).ToList();
            state.Map.Rooms = [new() { Color = [74,60,46], Positions = Enumerable.Range(1,MapChunk.Cols)
                .SelectMany(c => Enumerable.Range(1,MapChunk.Rows).Select(r => new List<int> { c,r })).ToList() }];
            state.Map.Furniture = [
                new() { Kind = "counter", Positions = [[4,11],[5,11],[6,11]] },
                new() { Kind = "bench", Positions = [[4,8],[5,8],[6,8]] },
                new() { Kind = "stall", Positions = [[4,5],[5,5],[6,5]] },
                new() { Kind = "counter", Positions = [[11,10],[11,11],[11,12]] },
                new() { Kind = "bench", Positions = [[14,10],[14,11],[14,12]] },
                new() { Kind = "counter", Positions = [[11,6]] },
                new() { Kind = "counter", Positions = [[12,6]] }
            ];
        }
        else if (theme is "dungeon" or "cave")
        {
            var underground = ChunkGenerator.Generate(chunk, exits);
            state.Map.Rooms = underground.Rooms;
            state.Map.Doors = underground.Doors;
            chunk.Terrain = DungeonDecorator.Decorate(chunk, underground.Rooms, underground.Doors);
        }
        else if (theme is "house" or "manor" or "tavern" or "temple")
        {
            var building = BuildingGenerator.Generate(chunk, exits, seed, true, bands, []);
            chunk.Terrain = building.Terrain;
            state.Map.Rooms = building.Rooms;
            state.Map.Doors = building.Doors;
            state.Map.Furniture = building.Furniture;
        }
        else if (theme == "village")
        {
            var village = BuildingGenerator.GenerateVillage(chunk, exits, bands, layout ?? (river ? "river" : "road"),
                river ? new(horizontalRiver, 8, 3, seed) : null, seed);
            chunk.Terrain = village.Terrain;
            state.Map.Rooms = village.Rooms;
            state.Map.Doors = village.Doors;
            state.Map.Furniture = village.Furniture;
        }
        else
        {
            var outdoor = OutdoorGenerator.Generate(chunk, exits, bands, seed, theme == "swamp" || river ? new(true, 8, 3, seed) : null);
            chunk.Terrain = outdoor.Terrain;
            state.Map.Doors = outdoor.Doors;
        }
        state.Map.Chunks = [chunk];
        state.Hero = new() { Name = "Explorer", Symbol = "HRO", Position = [2, 7], Color = [240, 220, 140] };
        var config = new AppConfig { RevealMap = reveal, SaveExploredCells = false };
        var display = new DisplayConfig();
        var provider = new MapObjectsProvider(state, display, storage, config);
        var map = new MapDisplay(state, display, new LegendDisplay(storage, display, config));
        var build = typeof(MapDisplay).GetMethod("BuildLocationRows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var drawRows = (List<Action>)build.Invoke(map, [provider])!;
        var rows = new List<string>();
        var previous = Console.Out;
        try
        {
            foreach (var draw in drawRows)
            {
                using var writer = new StringWriter();
                Console.SetOut(writer);
                draw();
                rows.Add(writer.ToString());
            }
        }
        finally { Console.SetOut(previous); }
        var plain = rows.Select(row => Regex.Replace(row, "\u001b\\[[0-9;]*m", "")).ToArray();
        Assert.Equal(33, plain.Length);
        Assert.All(plain, row => Assert.InRange(row.Length, 1, 87));
        for (int row = 2; row < 31; row += 2) Assert.Equal(87, plain[row].Length);
        Assert.Contains(plain, row => row.Contains("HRO"));
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "scripts", "build-release.ps1"))) directory = directory.Parent;
        Assert.NotNull(directory);
        string target = Path.Combine(directory.FullName, "artifacts", "map-render");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, $"{theme}{(seed == 3 ? "" : "-seed" + seed)}{(layout == null ? "" : "-" + layout)}{(river ? "-river" : "")}{(crossroads ? "-crossroads" : "")}{(!horizontalRiver ? "-vertical" : "")}-{(reveal ? "revealed" : "fog")}.json"), JsonSerializer.Serialize(rows));
    }
}
