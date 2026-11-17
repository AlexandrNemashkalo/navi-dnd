using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class WorldPlaceLookupTests
{
    private static WorldMap World() => new() { Places = [
        new() { Name = "Old Mill" }, new() { Name = "Old Tower" }, new() { Name = "Mill" }] };

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Old")]
    [InlineData("Old Mill and Old Tower")]
    public void AmbiguousOrEmptyNameNeverSelectsAnArbitraryPlace(string name)
        => Assert.Null(WorldAtlas.FindPlace(World(), name));

    [Theory]
    [InlineData("Mill", "Mill")]
    [InlineData("«old mill»", "Old Mill")]
    [InlineData("Tower", "Old Tower")]
    [InlineData("the Old Tower nearby", "Old Tower")]
    public void ExactNamesAndUnambiguousShorthandStillResolve(string query, string name)
        => Assert.Equal(name, WorldAtlas.FindPlace(World(), query)!.Name);
}
