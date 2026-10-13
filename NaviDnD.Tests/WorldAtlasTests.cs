using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class WorldAtlasTests
{
    [Theory]
    [InlineData("ru", 'R', "у реки", "река неподалёку")]
    [InlineData("en", 'R', "by a river", "river nearby")]
    [InlineData("ru", 'S', "у моря", "море неподалёку")]
    [InlineData("en", 'S', "by the sea", "sea nearby")]
    [InlineData("ru", 'L', "у озера", "озеро неподалёку")]
    [InlineData("en", 'L', "by a lake", "lake nearby")]
    public void WaterOnThisTileIsDifferentFromNearbyWater(string language, char kind, string here, string nearby)
    {
        string previous = L.World;
        try
        {
            L.SetWorld(language);
            var w = new WorldMap { Width = 5, Height = 5,
                Biomes = Enumerable.Repeat(new string(WorldBiomes.Plains, 5), 5).ToList(),
                Rivers = Enumerable.Repeat("00000", 5).ToList(), Roads = Enumerable.Repeat("00000", 5).ToList() };
            void Set(int x, int y)
            {
                var row = (kind == 'R' ? w.Rivers : w.Biomes)[y].ToCharArray();
                row[x] = kind == 'R' ? '1' : kind == 'S' ? WorldBiomes.Sea : WorldBiomes.Lake;
                (kind == 'R' ? w.Rivers : w.Biomes)[y] = new(row);
            }
            Set(3, 3);
            string description = WorldAtlas.Surroundings(w, 2, 2, true);
            Assert.Contains(nearby, description);
            Assert.DoesNotContain(here, description);
            Set(2, 2);
            description = WorldAtlas.Surroundings(w, 2, 2, true);
            Assert.Contains(here, description);
            Assert.DoesNotContain(nearby, description);
        }
        finally { L.SetWorld(previous); }
    }
}
