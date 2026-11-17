using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;
using NaviDnD.Helpers;

namespace NaviDnD.Tests;

[Collection(IconPackCollection.Name)]
public class MapPresentationRegressionTests
{
    [Theory]
    [InlineData('p', 2)]
    [InlineData('q', 2)]
    [InlineData('k', 2)]
    [InlineData('w', 1)]
    public void BuiltFloorPatternUsesItsMaterialEvenUnderFurniture(char floor, int expectedPattern)
    {
        var storage = new Storage();
        var state = storage.WorldState;
        state.Map = new()
        {
            Chunks = [new() { Theme = "building", Style = "temple", Terrain = Enumerable.Repeat(new string(floor, 20), 15).ToList() }],
            Rooms = [new() { Positions = [[3,3],[4,3]], Color = [70,70,78] }],
            Furniture = [new() { Kind = "bench", Positions = [[3,3]] }]
        };
        state.Hero = new() { Position = [1,1] };
        var provider = new MapObjectsProvider(state, new DisplayConfig(), storage, new AppConfig { RevealMap = true });
        var pattern = typeof(MapObjectsProvider).GetMethod("TileKind", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal(expectedPattern, pattern.Invoke(provider, [3,3]));
        Assert.Equal(expectedPattern, pattern.Invoke(provider, [4,3]));
        string ansi = Capture(() => provider.WriteCellEntity(4,3));
        string? background = null;
        var backgrounds = new List<string?>();
        foreach (Match token in Regex.Matches(ansi, @"\x1b\[[0-9;]*m|."))
        {
            if (token.Value.StartsWith("\u001b[48;")) background = token.Value;
            else if (!token.Value.StartsWith('\u001b')) backgrounds.Add(background);
        }
        Assert.Equal(3, backgrounds.Count);
        Assert.DoesNotContain(null, backgrounds);
        Assert.NotEqual(backgrounds[0], backgrounds[1]);
        Assert.Equal(backgrounds[expectedPattern == 2 ? 1 : 0], backgrounds[2]);
    }

    [Theory]
    [InlineData('.')]
    [InlineData('w')]
    [InlineData('=')]
    [InlineData('p')]
    public void PlainGroundClearsTheCardWithoutShowingTheHeroOrPreviousObject(char code)
    {
        var storage = new Storage();
        var display = new DisplayConfig { DefaultImageLines = ["hero portrait"], SelectedImageLines = ["old object"], NotePageCount = 3 };
        var select = typeof(SpellArt).Assembly.GetType("NaviDnD.MouseUiHelper")!
            .GetMethod("SetSelectedTerrainImage", BindingFlags.NonPublic | BindingFlags.Static)!;
        select.Invoke(null, [display, TerrainCatalog.Get(code)!, "ground"]);
        var dialog = new DialogDisplay(storage.WorldState, display, storage);
        var actual = (string[])typeof(DialogDisplay).GetMethod("GetDefaultRightPanelLines", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(dialog, null)!;
        Assert.Equal(new[] { "" }, actual);
        Assert.Equal(0, display.NotePageCount);
        Assert.Equal(new[] { "hero portrait" }, display.DefaultImageLines);
    }

    [Fact]
    public void TallGrassKeepsItsOwnPixelCardAfterHoveringPlainGround()
    {
        var previous = ArtPictures.Current;
        string pack = CatalogSearch.IconPack;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e");
            CatalogSearch.IconPack = CatalogSearch.DcssPack;
            var display = new DisplayConfig();
            var select = typeof(SpellArt).Assembly.GetType("NaviDnD.MouseUiHelper")!
                .GetMethod("SetSelectedTerrainImage", BindingFlags.NonPublic | BindingFlags.Static)!;
            select.Invoke(null, [display, TerrainCatalog.Get('.')!, "grass"]);
            select.Invoke(null, [display, TerrainCatalog.Get(',')!, "Высокая трава"]);
            Assert.Equal(Assert.Single(art.Find("Tall grass", "terrain")), ArtPictures.TerrainImage(TerrainCatalog.Get(',')!));
            Assert.Contains("Высокая трава", display.SelectedImageLines!);
            Assert.DoesNotContain(display.SelectedImageLines!, line => line.Any(c => c is >= '\u2800' and <= '\u28ff'));
            Assert.Contains(display.SelectedImageLines!, line => PixelArt.Colors(line)?.Any(c => c.Fg != null) == true);
        }
        finally { ArtPictures.Current = previous; CatalogSearch.IconPack = pack; }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(14)]
    [InlineData(21)]
    public void PlaceCaptionFitsBelowThePictureInShortPanels(int height)
    {
        var pack = CatalogSearch.IconPack;
        try
        {
            CatalogSearch.IconPack = CatalogSearch.DcssPack;
            var display = new DisplayConfig { MaxHistoryLines = height, DialogRightPanelWidth = 36 };
            var select = typeof(SpellArt).Assembly.GetType("NaviDnD.MouseUiHelper")!
                .GetMethod("SetSelectedImage", BindingFlags.NonPublic | BindingFlags.Static)!;
            select.Invoke(null, [display, "lorc/crown-coin", "Белоречье\nСтолица · Королевство Арен", null]);
            Assert.NotNull(display.SelectedImageLines);
            Assert.InRange(display.SelectedImageLines!.Length, 1, height);
            Assert.Contains("Белоречье", display.SelectedImageLines);
            Assert.Equal("Столица · Королевство Арен", display.SelectedImageLines.Last());
            Assert.All(display.SelectedImageLines, line => Assert.InRange(line.Length, 0, 36));
        }
        finally { CatalogSearch.IconPack = pack; }
    }

    [Fact]
    public void WorldCardsUseBrailleWithoutChangingPixelPreference()
    {
        var pack=CatalogSearch.IconPack;
        try
        {
            CatalogSearch.IconPack=CatalogSearch.DcssPack;
            var display=new DisplayConfig { MapLevel=MapLevel.World, MaxHistoryLines=21, DialogRightPanelWidth=36 };
            var select=typeof(SpellArt).Assembly.GetType("NaviDnD.MouseUiHelper")!
                .GetMethod("SetSelectedImage",BindingFlags.NonPublic|BindingFlags.Static)!;
            foreach(string icon in new[]{"lorc/crown-coin","delapouite/river","delapouite/stone-bridge"})
            {
                select.Invoke(null,[display,icon,"Место",null]);
                string art=string.Join("",display.SelectedImageLines!);
                Assert.Contains(art,c=>c>'\u2800'&&c<='\u28ff');
                Assert.DoesNotContain('▀',art);
                Assert.Contains("Место",display.SelectedImageLines!);
            }
            Assert.Equal(CatalogSearch.DcssPack,CatalogSearch.IconPack);
        }
        finally { CatalogSearch.IconPack=pack; }
    }

    [Theory]
    [InlineData('.')]
    [InlineData('=')]
    public void OutdoorGroundUsesStableTextureAcrossCellsAndTheirSeams(char code)
    {
        var storage = new Storage();
        var state = storage.WorldState;
        state.Map = new() { Chunks = [new() { Theme = "plains", Terrain = Enumerable.Repeat(new string(code, 20), 15).ToList() }] };
        state.Hero = new() { Position = [1,1] };
        string before = JsonSerializer.Serialize(state);
        var provider = new MapObjectsProvider(state, new DisplayConfig(), storage, new AppConfig { RevealMap = true });
        string Draw() => Capture(() =>
        {
            for (int y = 4; y <= 6; y++)
                for (int x = 3; x <= 8; x++)
                {
                    provider.WriteCellEntity(x, y);
                    provider.WriteLine(x + .5, y, false);
                    provider.WriteLine(x, y + .5, true);
                }
        });
        string ansi = Draw();
        Assert.Equal(ansi, Draw());
        var backgrounds = Regex.Matches(ansi, @"\x1b\[48;2;\d+;\d+;\d+m").Select(m => m.Value).Distinct().ToArray();
        Assert.True(backgrounds.Length >= 6, "The terrain should have more than one flat background shade.");
        string glyphs = Regex.Replace(ansi, "\u001b\\[[0-9;]*m", "");
        Assert.DoesNotContain('·', glyphs);
        Assert.DoesNotContain('\'', glyphs);
        Assert.Equal(before, JsonSerializer.Serialize(state));
        Assert.Equal(5, state.Map.TerrainAt(5,5)!.StepCostFt);
    }

    private static string Capture(Action draw)
    {
        var previous = Console.Out;
        using var writer = new StringWriter();
        try { Console.SetOut(writer); draw(); return writer.ToString(); }
        finally { Console.SetOut(previous); }
    }
}
