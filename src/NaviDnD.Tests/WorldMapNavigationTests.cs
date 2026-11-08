using NaviDnD.Data.Models;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class WorldMapNavigationTests
{
    [Fact]
    public void ZoomedOutLegendKeepsKnownMinorDestinationsAndRespectsFilters()
    {
        var world = new WorldMap
        {
            Width = 120, Height = 120,
            Places =
            [
                new() { Name = "Village", Type = WorldPlaceTypes.Village, X = 69, Y = 41 },
                new() { Name = "Watchhouse", Type = WorldPlaceTypes.Ruins, X = 72, Y = 41 },
                new() { Name = "Secret", Type = WorldPlaceTypes.Cave, X = 73, Y = 41, Hidden = true },
                new() { Name = "Distant", Type = WorldPlaceTypes.Village, X = 1, Y = 110 },
            ],
        };
        var view = new WorldMapView(world, 87, 33);
        var places = view.VisiblePlaces(87, 33, 69.5, 41.5, view.MaxZoom, ["settlements", "sites"]);
        Assert.Contains(0, places);
        Assert.Contains(1, places);
        Assert.DoesNotContain(2, places);
        Assert.DoesNotContain(3, places);
        Assert.Equal([1], view.VisiblePlaces(87, 33, 69.5, 41.5, view.MaxZoom, ["sites"]));
    }
}
