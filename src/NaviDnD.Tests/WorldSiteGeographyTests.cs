using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class WorldSiteGeographyTests
{
    [Fact]
    public void RoadNorthAndRiverSouthStayOnOppositeSidesOfSettlement()
    {
        var world = World();
        world.Roads[2] = "0022200";
        world.Rivers[4] = "0044400";
        world.Chronicle.Places.Add(new() { Name = "Riverside", Type = WorldPlaceTypes.Town, X = 3, Y = 3 });

        var site = GameWorld.SiteAt(world, 3, 3);

        Assert.True(site.RoadHorizontal);
        Assert.InRange(site.RoadOffset, 0.5, 0.9);
        Assert.Equal(new[] { ChunkSide.Left, ChunkSide.Right }.Order(), site.RoadSides!.Order());
        Assert.True(site.RiverHorizontal);
        Assert.InRange(site.RiverOffset, -0.9, -0.5);
        Assert.Equal(2, site.RiverWidth);
        Assert.Equal(WorldPlaceTypes.Town, site.PlaceType);
    }

    [Fact]
    public void NorthSouthRiverWestOfSettlementKeepsItsAxisAndSide()
    {
        var world = World();
        foreach (int y in new[] { 2, 3, 4 }) world.Rivers[y] = "0070000";
        var site = GameWorld.SiteAt(world, 3, 3);
        Assert.False(site.RiverHorizontal);
        Assert.InRange(site.RiverOffset, -0.9, -0.5);
        Assert.Equal(3, site.RiverWidth);
    }

    [Fact]
    public void ChronicleTrailsAreUsedEvenBeforeComposingTheVisibleWorldMap()
    {
        var world = World();
        world.Chronicle.Places.Add(new() { Name = "Trail", X = 3, Y = 3, Type = WorldPlaceTypes.Village,
            Trail = [2, 2, 3, 2, 4, 2] });
        var site = GameWorld.SiteAt(world, 3, 3);
        Assert.True(site.RoadHorizontal);
        Assert.True(site.RoadOffset > 0);
        Assert.Contains(ChunkSide.Left, site.RoadSides!);
        Assert.Contains(ChunkSide.Right, site.RoadSides!);
        Assert.All(world.Roads, row => Assert.Equal("0000000", row));
    }

    [Fact]
    public void RouteOnTheCurrentCellTakesPriorityOverNearbyLargerRoute()
    {
        var world = World();
        world.Rivers[2] = "0099900";
        world.Rivers[3] = "0011100";
        var site = GameWorld.SiteAt(world, 3, 3);
        Assert.Equal(0, site.RiverOffset);
        Assert.Equal(1, site.RiverWidth);
    }

    [Fact]
    public void RoadJunctionKeepsAllActualApproaches()
    {
        var world = World();
        world.Roads[2] = "0002000";
        world.Roads[3] = "0022200";
        var site = GameWorld.SiteAt(world, 3, 3);
        Assert.Equal(new[] { ChunkSide.Top, ChunkSide.Left, ChunkSide.Right }.Order(), site.RoadSides!.Order());
        Assert.DoesNotContain(ChunkSide.Bottom, site.RoadSides!);
        Assert.Equal(0, site.RoadOffset);
    }

    [Fact]
    public void DistantRoutesDoNotInventALocalRiverOrRoad()
    {
        var world = World();
        world.Rivers[0] = "9999999";
        world.Roads[6] = "2222222";
        var site = GameWorld.SiteAt(world, 3, 3);
        Assert.Null(site.RiverHorizontal);
        Assert.Null(site.RoadHorizontal);
        Assert.Empty(site.RoadSides!);
    }

    [Fact]
    public void IsolatedRoadCellDoesNotInventAThroughRoad()
    {
        var world = World();
        world.Roads[3] = "0001000";
        var site = GameWorld.SiteAt(world, 3, 3);
        Assert.Null(site.RoadHorizontal);
        Assert.Empty(site.RoadSides!);
    }

    [Fact]
    public void WorldEdgeDoesNotInventWaterOutsideTheWorld()
    {
        var world = World();
        world.Biomes[0] = ".l.....";
        var site = GameWorld.SiteAt(world, 0, 0);
        Assert.Equal(new[] { ChunkSide.Right }, site.WaterSides);
    }

    private static WorldMap World() => new()
    {
        Width = 7, Height = 7,
        Biomes = Enumerable.Repeat(".......", 7).ToList(),
        Rivers = Enumerable.Repeat("0000000", 7).ToList(),
        Roads = Enumerable.Repeat("0000000", 7).ToList(),
    };
}
