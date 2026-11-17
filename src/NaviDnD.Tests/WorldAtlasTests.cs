using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class WorldAtlasTests
{
    [Theory]
    [InlineData("port")]
    [InlineData("village")]
    [InlineData("ruins")]
    public void SmallIslandBetweenSampledCellsRemainsAvailable(string type)
    {
        var world = new WorldMap { Width = 9, Height = 9,
            Biomes = Enumerable.Repeat("~~~~~~~~~", 9).ToList(), Owners = Enumerable.Repeat(".........", 9).ToList(),
            Roads = Enumerable.Repeat("000000000", 9).ToList(), Rivers = Enumerable.Repeat("000000000", 9).ToList(),
            Kingdoms = [new() { Name = "Island" }] };
        world.Biomes[4] = "~~~~.~~~~";
        world.Owners[4] = "....a....";
        var request = new WorldAtlas.SpotRequest(type, null, null, null, null, 0);
        var site = WorldAtlas.FindSpot(world, request, [], new HashSet<int>());
        Assert.NotNull(site);
        Assert.Equal((4, 4), (site.Value.x, site.Value.y));
        Assert.Null(WorldAtlas.FindSpot(world, request, [(4, 4)], new HashSet<int>()));
    }

    [Theory]
    [InlineData('~')]
    [InlineData('l')]
    [InlineData('r')]
    [InlineData('.')]
    public void PortRequiresRealWaterAccessInsteadOfAcceptingDryInlandSite(char water)
    {
        var world = new WorldMap { Width = 9, Height = 9,
            Biomes = Enumerable.Repeat(".........", 9).ToList(), Owners = Enumerable.Repeat(".........", 9).ToList(),
            Roads = Enumerable.Repeat("000000000", 9).ToList(), Rivers = Enumerable.Repeat("000000000", 9).ToList(),
            Places = [new() { Name = "Dry slot", Type = "port", X = 2, Y = 2 },
                new() { Name = "Edge slot", Type = "port", X = 0, Y = 0 }] };
        if (water == 'r') world.Rivers[4] = "000001000";
        else if (water != '.') world.Biomes[4] = "....." + water + "...";
        var request = new WorldAtlas.SpotRequest("port", null, null, null, null);
        var site = WorldAtlas.FindSpot(world, request, [], new HashSet<int>());
        if (water == '.') Assert.Null(site);
        else
        {
            Assert.NotNull(site);
            Assert.InRange(Math.Abs(site.Value.x - 5), 0, 1);
            Assert.InRange(Math.Abs(site.Value.y - 4), 0, 1);
            Assert.False(WorldBiomes.IsWater(world.BiomeAt(site.Value.x, site.Value.y)));
            Assert.Equal(0, world.RiverAt(site.Value.x, site.Value.y));
        }
        Assert.NotNull(WorldAtlas.FindSpot(world, request with { Type = "town" }, [], new HashSet<int>()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequestedKingdomIsNotSilentlyReplacedByAnEasierNeighbor(bool hasLand)
    {
        var owners = Enumerable.Repeat("bbbbbbbbbbbb", 9).ToList();
        if (hasLand) owners[4] = "bbbbbbbbabbb";
        var world = new WorldMap { Width = 12, Height = 9,
            Biomes = Enumerable.Repeat("............", 9).ToList(), Owners = owners,
            Roads = Enumerable.Repeat("000000000000", 9).ToList(), Rivers = Enumerable.Repeat("000000000000", 9).ToList(),
            Kingdoms = [new() { Name = "Requested" }, new() { Name = "Neighbor" }],
            Places = [new() { Name = "Convenient unused slot", Type = "ruins", X = 1, Y = 4 }] };
        var request = new WorldAtlas.SpotRequest("ruins", new WorldPlace { Name = "Origin", X = 2, Y = 4 }, "west", 1, null, 0);
        var result = WorldAtlas.FindSpot(world, request, [], new HashSet<int>());
        if (hasLand)
        {
            Assert.NotNull(result);
            Assert.Equal(0, world.OwnerAt(result.Value.x, result.Value.y));
        }
        else Assert.Null(result);
        Assert.NotNull(WorldAtlas.FindSpot(world, request with { Kingdom = -1 }, [], new HashSet<int>()));
    }

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
