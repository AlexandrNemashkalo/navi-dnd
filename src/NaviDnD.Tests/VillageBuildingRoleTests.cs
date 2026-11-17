using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class VillageBuildingRoleTests
{
    [Fact]
    public void WorkAndStorageBuildingsUseTheirOwnFurnitureAcrossSeeds()
    {
        int workshops = 0, stores = 0;
        for (int seed = 1; seed <= 100; seed++)
        {
            var chunk = new MapChunk { Theme = "village", Seed = seed };
            var result = BuildingGenerator.GenerateVillage(chunk,
                [new() { Side = ChunkSide.Left, Offset = 7 }, new() { Side = ChunkSide.Right, Offset = 7 }],
                Enum.GetValues<ChunkSide>().ToDictionary(s => s, _ => 'T'), "road");
            foreach (var room in result.Rooms)
            {
                bool workshop = Enumerable.Range(1, 6).Any(n => room.Name == L.WF("Workshop {0}", n));
                bool store = Enumerable.Range(1, 6).Any(n => room.Name == L.WF("Storehouse {0}", n));
                if (!workshop && !store) continue;
                if (workshop) workshops++; else stores++;
                var cells = room.Positions.Select(p => (p[0], p[1])).ToHashSet();
                var furniture = result.Furniture.Where(f => f.Positions.Any(p => cells.Contains((p[0], p[1])))).ToList();
                Assert.DoesNotContain(furniture, f => f.Kind is "bed" or "hearth");
                if (store) Assert.DoesNotContain(furniture, f => f.Kind == "table");
            }
        }
        Assert.True(workshops > 20, $"Only {workshops} workshops across 100 villages");
        Assert.True(stores > 20, $"Only {stores} stores across 100 villages");
    }
}
