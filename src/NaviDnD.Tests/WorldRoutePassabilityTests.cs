using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class WorldRoutePassabilityTests
{
    [Theory]
    [InlineData(TravelService.Pace.Normal)]
    [InlineData(TravelService.Pace.Slow)]
    [InlineData(TravelService.Pace.Fast)]
    public void ShortRoadKeepsExactCostButPreviewsOneChargedDay(TravelService.Pace pace)
    {
        var world = new WorldMap { Width = 4, Height = 1, Biomes = ["...."],
            Roads = ["2222"], Rivers = ["0000"], Owners = ["...."] };
        var route = WorldAtlas.Route(world, (0, 0), (3, 0))!;
        Assert.Equal(0.5625, route.Days);
        Assert.Equal(1, new TravelService.Plan((0, 0), (3, 0), null, route).Days(pace));
    }

    [Theory]
    [InlineData(WorldBiomes.Ocean)]
    [InlineData(WorldBiomes.Sea)]
    [InlineData(WorldBiomes.Lake)]
    [InlineData(WorldBiomes.Peaks)]
    public void ImpassableDestinationCannotBeReachedByWalking(char destination)
    {
        var world = Map(destination, false);
        Assert.True(double.IsPositiveInfinity(WorldAtlas.StepDays(world, 1, 0)));
        Assert.Null(WorldAtlas.Route(world, (0, 0), (1, 0)));
    }

    [Theory]
    [InlineData(WorldBiomes.Ocean)]
    [InlineData(WorldBiomes.Sea)]
    [InlineData(WorldBiomes.Lake)]
    [InlineData(WorldBiomes.Peaks)]
    public void ExplicitRoadCrossingRemainsUsable(char destination)
    {
        var world = Map(destination, true);
        var route = WorldAtlas.Route(world, (0, 0), (1, 0));
        Assert.NotNull(route);
        Assert.Equal(new[] { (0, 0), (1, 0) }, route.Path);
    }

    private static WorldMap Map(char destination, bool road) => new() { Width = 2, Height = 1,
        Biomes = ["." + destination], Roads = [road ? "22" : "00"], Rivers = ["00"], Owners = [".."] };
}
