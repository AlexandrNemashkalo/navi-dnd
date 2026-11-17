using System.Reflection;
using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.Display;
using NaviDnD.Helpers;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

[Collection(IconPackCollection.Name)]
public class SceneryPictureRegressionTests
{
    [Fact]
    public void SceneryCardsResolveReviewedPicturesInsteadOfUnrelatedObjects()
    {
        var previous = ArtPictures.Current;
        string pack = CatalogSearch.IconPack;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e");
            CatalogSearch.IconPack = CatalogSearch.DcssPack;
            foreach (char code in new[] { 'w', 'O', 'H', 'Y', 'X', 'F' })
            {
                var kind = TerrainCatalog.Get(code)!;
                string expected = Assert.Single(art.Find(kind.Name, "terrain"));
                Assert.Equal(expected, ArtPictures.TerrainImage(kind));
                Export(kind.Name, expected);
            }
            foreach (var (type, name) in new[] { (WorldPlaceTypes.Village, "Village"), (WorldPlaceTypes.Capital, "Capital") })
            {
                string expected = Assert.Single(art.Find(name, "place"));
                Assert.Equal(expected, PackPictures.ForPack(WorldMapView.PlaceImage(type)));
                Export(name, expected);
            }
            string window = Assert.Single(art.Find("Window", "feature"));
            Assert.Equal(window, PackPictures.ForPack("delapouite/window-bars"));
            Export("Window", window);
            var tool = new IconTools(new McpServerConfig(WorldStatePath: "unused", LogPath: null));
            string axle = Assert.Single(art.Find("Cart axle", "feature"));
            Assert.Equal(axle, tool.FindIcon("feature: spare axle"));
            var prefer = typeof(ArtPictures).GetMethod("Prefer", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(axle, prefer.Invoke(null, ["Запасная ось", "feature", "lorc/torch"]));
            Assert.Equal("portraits/custom.png", prefer.Invoke(null, ["Запасная ось", "feature", "portraits/custom.png"]));
            Export("Cart axle", axle);
        }
        finally { ArtPictures.Current = previous; CatalogSearch.IconPack = pack; }
    }

    private static void Export(string name, string slug)
    {
        var lines = SvgToBrailleConverter.Convert(slug, "")!;
        Assert.NotNull(lines);
        Assert.DoesNotContain(lines, line => line.Any(c => c is >= '\u2800' and <= '\u28ff'));
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/scenery-picture-fixes"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name.Replace(' ', '-') + ".json"), JsonSerializer.Serialize(lines.Select(line =>
        {
            var cells = PixelArt.Colors(line)!;
            return string.Concat(line.Select((c, i) =>
            {
                var fg = cells[i].Fg ?? [18,20,24];
                var bg = PixelArt.Background(cells[i], [18,20,24])!;
                return $"\u001b[38;2;{fg[0]};{fg[1]};{fg[2]}m\u001b[48;2;{bg[0]};{bg[1]};{bg[2]}m{c}";
            }));
        })));
    }
}
