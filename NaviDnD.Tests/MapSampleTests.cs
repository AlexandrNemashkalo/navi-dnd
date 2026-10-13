using System.Text;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class MapSampleTests
{
    public static readonly ChunkExit[] Exits =
    [new() { Side = ChunkSide.Left, Offset = 7, External = true },
     new() { Side = ChunkSide.Right, Offset = 7 },
     new() { Side = ChunkSide.Top, Offset = 10 },
     new() { Side = ChunkSide.Bottom, Offset = 10 }];

    [Fact]
    public void ExportSamples()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/map-samples"));
        Directory.CreateDirectory(root);
        foreach (string theme in new[] { "forest", "hills", "swamp", "village", "building", "dungeon", "cave" })
        for (int seed = 1; seed <= 5; seed++)
        foreach (bool river in new[] { false, true })
        {
            var chunk = new MapChunk { Theme = theme, Seed = seed, Style = "tavern" };
            List<string> terrain;
            List<Room> rooms = [];
            List<Door> doors;
            if (MapChunk.IsOutdoorTheme(theme))
            {
                var result = OutdoorGenerator.Generate(chunk, Exits, new Dictionary<ChunkSide, char>(), seed,
                    river ? new(true, 8, 3, seed) : null);
                terrain = result.Terrain; doors = result.Doors;
            }
            else if (theme is "building" or "village")
            {
                var result = theme == "building"
                    ? BuildingGenerator.Generate(chunk, Exits, seed, true, new Dictionary<ChunkSide, char>(), [])
                    : BuildingGenerator.GenerateVillage(chunk, Exits, new Dictionary<ChunkSide, char>());
                terrain = result.Terrain; rooms = result.Rooms; doors = result.Doors;
            }
            else
            {
                var result = ChunkGenerator.Generate(chunk, Exits);
                rooms = result.Rooms; doors = result.Doors;
                terrain = DungeonDecorator.Decorate(chunk, rooms, doors);
            }
            var sb = new StringBuilder();
            sb.AppendLine($"theme={theme}, seed={seed}, river={river} (river applies to outdoor themes)");
            foreach (string row in terrain.AsEnumerable().Reverse()) sb.AppendLine(row);
            sb.AppendLine("Legend (one cell = one character; north above):");
            foreach (char code in terrain.SelectMany(x => x).Distinct().Order())
                sb.AppendLine($"{code}: {TerrainCatalog.Get(code)?.Name ?? "void"}");
            sb.AppendLine("Doors (1-based col,row; room walls are implicit between rooms):");
            foreach (var door in doors.Where(d => d.IsWindow != true))
                sb.AppendLine($"{string.Join(',', door.From)} -> {string.Join(',', door.To)}");
            foreach (var room in rooms)
                sb.AppendLine($"{room.Name}: {string.Join(' ', room.Positions.Select(p => string.Join(',', p)))}");
            File.WriteAllText(Path.Combine(root, $"{theme}{(river ? "-river" : "")}-{seed}.txt"), sb.ToString());
        }
    }
}
