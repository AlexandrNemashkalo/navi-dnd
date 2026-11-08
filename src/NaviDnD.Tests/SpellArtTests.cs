using NaviDnD.Helpers;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;
using SkiaSharp;
using System.Reflection;

namespace NaviDnD.Tests;

public class SpellArtTests
{
    private static int PreparedSpellCount()
    {
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "SpellArt", "RuleSets", "Dnd5e", "spell-icons.json")));
        return manifest.RootElement.GetProperty("icons").GetArrayLength();
    }

    [Theory]
    [InlineData(L.Russian, CatalogSearch.GameIconsPack, SpellArtMode.Braille)]
    [InlineData(L.English, CatalogSearch.GameIconsPack, SpellArtMode.Pixels)]
    [InlineData(L.Russian, CatalogSearch.DcssPack, SpellArtMode.Pixels)]
    [InlineData(L.English, CatalogSearch.DcssPack, SpellArtMode.Braille)]
    public void AllSpellArtAppearsForNonCasterWithoutChangingSavedHero(string language, string pack, SpellArtMode mode)
    {
        var current = typeof(SpellArt).GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = current.GetValue(null);
        string world = L.World, previousPack = CatalogSearch.IconPack;
        try
        {
            L.SetWorld(language);
            CatalogSearch.IconPack = pack;
            var catalog = SpellArt.Load("Dnd5e", mode);
            current.SetValue(null, catalog);
            var hero = new Hero { Spells = [], Inventory = [] };
            string before = System.Text.Json.JsonSerializer.Serialize(hero);
            var visible = HeroDisplay.SortedSpells(hero);
            Assert.Equal(PreparedSpellCount(), visible.Count);
            Assert.Equal(visible.Count, visible.Select(s => s.Name).Distinct().Count());
            Assert.True(new HeroDisplay(new WorldState { Hero = hero }, new DisplayConfig()).HasSpells);
            var display = new DisplayConfig();
            // All rows, including the final pages, must select their own image through the real UI path.
            foreach (var spell in visible)
            {
                var info = SpellDatabase.Find(spell.Name)!;
                Assert.NotNull(info);
                Assert.Equal(info.Localized.Name, spell.Name);
                SelectCard("SetSelectedSpell", display, spell, 0);
                Assert.Equal(catalog.Render(info.NameEn, mode: SpellArtMode.Braille), display.SelectedImageLines!.Take(18));
                Assert.All(display.SelectedImageLines.Take(18), line =>
                    Assert.All(line, c => Assert.InRange((int)c, 0x2800, 0x28ff)));
                Assert.Contains(display.SelectedImageLines.Take(18).SelectMany(line => PixelArt.Colors(line)!), cell => cell.Fg != null);
                Assert.Equal(catalog.NameColor(info.NameEn), display.SelectedImageColor);
                Assert.DoesNotContain(display.SelectedImageLines, line => line.Contains(L.T("BG3 preview")));
            }
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(hero));
            Assert.Empty(hero.Spells);
            Assert.Empty(hero.Inventory);
        }
        finally { current.SetValue(null, previous); L.SetWorld(world); CatalogSearch.IconPack = previousPack; }
    }

    [Theory]
    [InlineData(L.Russian)]
    [InlineData(L.English)]
    public void PreviewDoesNotDuplicateLearnedSpellOrReplaceItsProperties(string language)
    {
        var current = typeof(SpellArt).GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = current.GetValue(null);
        string world = L.World;
        try
        {
            L.SetWorld(language);
            current.SetValue(null, SpellArt.Load("Dnd5e"));
            var entry = SpellDatabase.Find("Fireball")!;
            // Existing spell deliberately uses the opposite language from the gallery.
            var learned = new HeroSpell { Name = language == L.English ? entry.Name : "Fireball", Level = 3, Prepared = true, Color = [1, 2, 3] };
            var custom = new HeroSpell { Name = "Custom spell", Level = 1, Color = [4, 5, 6] };
            var hero = new Hero { Spells = [learned, custom] };
            var visible = HeroDisplay.SortedSpells(hero);
            Assert.Equal(PreparedSpellCount() + 1, visible.Count);
            Assert.Same(learned, Assert.Single(visible, s => SpellDatabase.Find(s.Name)?.NameEn == "Fireball"));
            Assert.Contains(custom, visible);
            Assert.True(learned.Prepared);
            Assert.Equal(new[] { 1, 2, 3 }, learned.Color);
            Assert.Equal(new[] { learned, custom }, hero.Spells);
        }
        finally { current.SetValue(null, previous); L.SetWorld(world); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(28)]
    public void LongSpellNamesCannotPushTheNextColumn(int width)
    {
        var fit = typeof(HeroDisplay).GetMethod("FitSpellListName", BindingFlags.Static | BindingFlags.NonPublic)!;
        string result = (string)fit.Invoke(null, ["Protection from Evil and Good / extra long name", width])!;
        Assert.Equal(width, result.Length);
        if (width > 1) Assert.EndsWith("…", result);
    }

    private static void SelectCard(string method, DisplayConfig display, object entry, int page) =>
        typeof(SpellArt).Assembly.GetType("NaviDnD.MouseUiHelper")!
            .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [display, entry, page]);

    [Fact]
    public void LocalizedSpellCardUsesItsCanonicalIconAndLeavesDescriptionPageReadable()
    {
        var current = typeof(SpellArt).GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = current.GetValue(null);
        var catalog = SpellArt.Load("Dnd5e");
        string previousPack = CatalogSearch.IconPack;
        try
        {
            CatalogSearch.IconPack = CatalogSearch.GameIconsPack;
            current.SetValue(null, catalog);
            var entry = SpellDatabase.Find("Fireball")!;
            Assert.NotNull(entry);
            var spell = new HeroSpell { Name = entry.Name, Level = 3, Color = [255, 80, 0] };
            var display = new DisplayConfig();
            SelectCard("SetSelectedSpell", display, spell, 0);
            Assert.Equal(catalog.Render("Fireball", mode: SpellArtMode.Braille), display.SelectedImageLines!.Take(18));
            Assert.Contains(display.SelectedImageLines, line => line.Contains(entry.Name.ToUpperInvariant()));
            Assert.NotNull(PixelArt.Colors(display.SelectedImageLines[0]));
            Assert.Equal(catalog.NameColor("Fireball"), display.SelectedImageColor);
            var titleSegment = display.SelectedImageSegments!.SelectMany(s => s ?? [])
                .Single(s => s.text == entry.Name.ToUpperInvariant());
            Assert.Equal(catalog.NameColor("Fireball"), titleSegment.color);
            SelectCard("SetSelectedSpell", display, spell, 1);
            Assert.Equal(1, display.NotePage);
            Assert.All(display.SelectedImageLines!, line => Assert.Null(PixelArt.Colors(line)));
            Assert.Contains(display.SelectedImageLines!, line => line.Contains(entry.Name.ToUpperInvariant()));
            Assert.True(display.SelectedImageColorTitleOnly);
            Assert.Equal(catalog.NameColor("Fireball"), display.SelectedImageColor);
            Assert.Equal(new[] { 255, 80, 0 }, spell.Color); // Display binding does not rewrite saved spell data.
        }
        finally { current.SetValue(null, previous); CatalogSearch.IconPack = previousPack; }
    }

    [Fact]
    public void EveryAliasAndBothRenderModesResolveToTheSamePermanentIcon()
    {
        var catalog = SpellArt.Load("Dnd5e");
        string root = Path.Combine(AppContext.BaseDirectory, "SpellArt");
        using var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "RuleSets", "Dnd5e", "spell-icons.json")));
        foreach (var icon in data.RootElement.GetProperty("icons").EnumerateArray())
        {
            string name = icon.GetProperty("name").GetString()!, id = icon.GetProperty("id").GetString()!;
            var braille = catalog.Render(name, mode: SpellArtMode.Braille)!;
            var pixels = catalog.Render(name, mode: SpellArtMode.Pixels)!;
            Assert.NotEqual(braille, pixels);
            Assert.Contains(pixels, row => row.Contains('▀') || row.Contains('▄'));
            Assert.All(pixels, row => Assert.DoesNotContain(row, c => c is >= '\u2800' and <= '\u28ff'));
            using var png = SKBitmap.Decode(Path.Combine(root, "Images", "bg3-spells", icon.GetProperty("pixelFile").GetString()!));
            Assert.Equal((32, 32), (png.Width, png.Height));
            foreach (var alias in icon.GetProperty("aliases").EnumerateArray())
            {
                string value = alias.GetString()!;
                Assert.Equal(id, catalog.GetIconId(value));
                Assert.Same(braille, catalog.Render(value, mode: SpellArtMode.Braille));
                Assert.Same(pixels, catalog.Render(value, mode: SpellArtMode.Pixels));
                Assert.Equal(catalog.NameColor(name), catalog.NameColor(value));
            }
            Assert.Equal(id, SpellArt.Load("Dnd5e").GetIconId(name));
        }
    }

    [Theory]
    [InlineData(CatalogSearch.GameIconsPack)]
    [InlineData(CatalogSearch.DcssPack)]
    public void UnmappedSpellAlsoUsesTintedBrailleWithoutChangingTheChosenPack(string pack)
    {
        var current = typeof(SpellArt).GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = current.GetValue(null);
        string previousPack = CatalogSearch.IconPack;
        try
        {
            var catalog = SpellArt.Load("Dnd5e", SpellArtMode.Pixels);
            Assert.Null(catalog.GetIconId("Wish"));
            current.SetValue(null, catalog);
            CatalogSearch.IconPack = pack;
            var spell = new HeroSpell { Name = "Wish", Level = 9, Color = [80, 130, 210] };
            var display = new DisplayConfig();
            SelectCard("SetSelectedSpell", display, spell, 0);
            var image = display.SelectedImageLines!.Take(18).ToArray();
            Assert.All(image, line => Assert.All(line, c => Assert.InRange((int)c, 0x2800, 0x28ff)));
            Assert.Contains(image, line => line.Any(c => c > '\u2800'));
            Assert.Equal(spell.Color, display.SelectedImageColor);
            Assert.False(display.SelectedImageColorTitleOnly);
            Assert.Equal(pack, CatalogSearch.IconPack);
            Assert.Equal(SpellArtMode.Pixels, catalog.DefaultMode);
        }
        finally { current.SetValue(null, previous); CatalogSearch.IconPack = previousPack; }
    }

    [Theory]
    [InlineData("  ОГНЁННЫЙ\tШАР ", "fireball")]
    [InlineData("Tasha’s Hideous Laughter", "tasha-s-hideous-laughter")]
    [InlineData("Огненный", null)]
    [InlineData("Fireball alternate unknown", null)]
    [InlineData("Порыв ветра", "gust-of-wind")]
    [InlineData("Шквал", "gust")]
    [InlineData("Пекло Аганаззара", "aganaezar-s-scorcher")]
    [InlineData("Зной Аганаззара", "aganazzar-s-scorcher")]
    public void BindingNormalizesNamesWithoutGuessingOtherSpells(string name, string? id) =>
        Assert.Equal(id, SpellArt.Load("Dnd5e").GetIconId(name));

    [Fact]
    public void ObsoletePreviewEnvironmentDoesNotSubstituteInventoryPictures()
    {
        const string variable = "NAVIDND_INVENTORY_BRAILLE_PREVIEW";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "lightning");
            var item = new HeroInventory { Name = "Test item", Description = "", Color = [200, 200, 200] };
            var display = new DisplayConfig();
            SelectCard("SetSelectedInventoryItem", display, item, 0);
            Assert.Null(item.Image);
            Assert.Equal("Test item", item.Name);
            Assert.Contains(display.SelectedImageLines!, line => line.Contains("Test item"));
            Assert.All(display.SelectedImageLines!, line => Assert.Null(PixelArt.Colors(line)));
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
    }

    [Theory]
    [InlineData("Fireball")]
    [InlineData("Magic Missile")]
    [InlineData("Witch Bolt")]
    [InlineData("Cure Wounds")]
    public void SmallLineMaskRendersStrokesAsBrailleInsteadOfColoredBlocks(string image)
    {
        var lines = SpellArt.Load("Dnd5e").Render(image, 32, SpellArtMode.Braille);
        Assert.NotNull(lines);
        Assert.Equal(16, lines!.Length);
        Assert.All(lines, line =>
        {
            Assert.Equal(32, line.Length);
            Assert.All(line, c => Assert.InRange((int)c, 0x2800, 0x28ff));
        });
        Assert.Contains(lines, line => line.Any(c => c != '\u2800'));
        // The empty top margin must not become a filled dark-background rectangle.
        Assert.All(lines[0], c => Assert.Equal('\u2800', c));
        Assert.All(lines, line => Assert.NotNull(PixelArt.Colors(line)));
        Assert.Contains(lines.SelectMany(line => PixelArt.Colors(line)!), cell => cell.Fg != null);
        Assert.Contains(lines.SelectMany(line => PixelArt.Colors(line)!), cell => cell.Bg != null);
        Assert.All(PixelArt.Colors(lines[0])!, cell => Assert.InRange((int)cell.BgAlpha, 0, 127));
    }

    [Theory]
    [InlineData("bg3-lines/axe")]
    [InlineData("bg3-lines/lightning")]
    [InlineData("spell.mask.png")]
    public void PreparedMasksAreNotAvailableToGenericItemRenderer(string image) =>
        Assert.Null(SvgToBrailleConverter.Convert(image, ""));

    [Fact]
    public void UnknownSpellAndUnconfiguredRuleSetKeepNormalArtwork()
    {
        Assert.Null(SpellArt.Load("Dnd5e").Render("Unknown spell"));
        Assert.Null(SpellArt.Load("OtherRules").Render("Fireball"));
        Assert.Null(SpellArt.Load("../Dnd5e").Render("Fireball"));
    }

    [Fact]
    public void OnlyMaskDotsContributeToCellColorAndUseCorrectBrailleBits()
    {
        using var mask = new SKBitmap(4, 4);
        using var colors = new SKBitmap(4, 4);
        mask.Erase(SKColors.White);
        colors.Erase(SKColors.Magenta); // Background must not contaminate the line color.
        mask.SetPixel(0, 0, SKColors.Black);
        mask.SetPixel(1, 3, SKColors.Black);
        colors.SetPixel(0, 0, new SKColor(40, 120, 240));
        colors.SetPixel(1, 3, new SKColor(60, 160, 200));
        var lines = ColoredBraille.Render(mask, colors, 2);
        Assert.Equal((char)(0x2800 + 1 + 128), lines[0][0]);
        Assert.Equal('\u2800', lines[0][1]);
        var cells = PixelArt.Colors(lines[0])!;
        Assert.Equal(new[] { 50, 140, 220 }, cells[0].Fg);
        Assert.Null(cells[0].Bg);
        Assert.Null(cells[1].Fg);
    }

    [Fact]
    public void NormalPanelKeepsNativeDotsAndOriginalBlueColors()
    {
        var lines = SpellArt.Load("Dnd5e").Render("Witch Bolt", 36, SpellArtMode.Braille)!;
        Assert.Equal(18, lines.Length);
        Assert.All(lines.Take(18), line => Assert.Equal("⠀⠀", line[..2]));
        var foregrounds = lines.Take(18).SelectMany(line => PixelArt.Colors(line)!).Where(c => c.Fg != null);
        Assert.Contains(foregrounds, c => c.Fg![2] > c.Fg[0]);
    }

    [Fact]
    public void BackgroundShowsThroughEmptyBrailleAndPreservesItsGradient()
    {
        using var mask = new SKBitmap(4, 4);
        using var background = new SKBitmap(4, 4);
        mask.Erase(SKColors.White);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                background.SetPixel(x, y, x < 2 ? new SKColor(20, 40, 60) : new SKColor(60, 90, 120));
        var line = ColoredBraille.Render(mask, null, 2, background)[0];
        Assert.Equal("⠀⠀", line);
        var cells = PixelArt.Colors(line)!;
        Assert.Equal(new[] { 20, 40, 60 }, cells[0].Bg);
        Assert.Equal(new[] { 60, 90, 120 }, cells[1].Bg);
        Assert.All(cells, cell => { Assert.Null(cell.Fg); Assert.Equal(255, cell.BgAlpha); });
    }

    [Fact]
    public void SoftBackgroundUsesAlphaCoverageAndBlendsWithTheCurrentTheme()
    {
        using var mask = new SKBitmap(4, 4);
        using var background = new SKBitmap(4, 4);
        mask.Erase(SKColors.White);
        background.Erase(new SKColor(255, 0, 255, 0));
        background.SetPixel(0, 0, new SKColor(40, 100, 180));
        var cell = PixelArt.Colors(ColoredBraille.Render(mask, null, 2, background)[0])![0];
        Assert.Equal(new[] { 40, 100, 180 }, cell.Bg);
        Assert.Equal(31, cell.BgAlpha);
        Assert.Equal(new[] { 22, 39, 57 }, PixelArt.Background(cell, [20, 30, 40]));
        Assert.Equal(new[] { 216, 223, 233 }, PixelArt.Background(cell, [240, 240, 240]));
    }

    [Fact]
    public void BackgroundDoesNotChangeMaskBitsOrLineColors()
    {
        using var mask = new SKBitmap(4, 4);
        using var colors = new SKBitmap(4, 4);
        using var background = new SKBitmap(4, 4);
        mask.Erase(SKColors.White);
        mask.SetPixel(0, 0, SKColors.Black);
        colors.Erase(new SKColor(180, 220, 255));
        background.Erase(new SKColor(20, 40, 60));
        var plain = ColoredBraille.Render(mask, colors, 2);
        var layered = ColoredBraille.Render(mask, colors, 2, background);
        Assert.Equal(plain, layered);
        Assert.Equal(PixelArt.Colors(plain[0])![0].Fg, PixelArt.Colors(layered[0])![0].Fg);
        Assert.Equal(new[] { 20, 40, 60 }, PixelArt.Colors(layered[0])![0].Bg);
    }

    [Fact]
    public void BackgroundFillsAllFourMarginsWhileSymbolKeepsNativeSize()
    {
        using var mask = new SKBitmap(8, 8);
        using var colors = new SKBitmap(8, 8);
        using var background = new SKBitmap(8, 8);
        mask.Erase(SKColors.White);
        mask.SetPixel(0, 0, SKColors.Black);
        mask.SetPixel(7, 7, SKColors.Black);
        colors.Erase(new SKColor(180, 220, 255));
        background.Erase(new SKColor(30, 34, 40));
        // A 12x12 display holds the native 8x8 symbol with two dots of margin.
        var plain = ColoredBraille.Render(mask, colors, 6);
        var layered = ColoredBraille.Render(mask, colors, 6, background);
        Assert.Equal(plain, layered);
        Assert.All(layered, line => Assert.All(PixelArt.Colors(line)!, cell =>
        {
            Assert.Equal(new[] { 30, 34, 40 }, cell.Bg);
            Assert.Equal(255, cell.BgAlpha);
        }));
        Assert.All(layered, line => { Assert.Equal('⠀', line[0]); Assert.Equal('⠀', line[^1]); });
    }

    [Fact]
    public void EveryPreparedSpellHasUsableColoredLinesAndBackground()
    {
        var catalog = SpellArt.Load("Dnd5e");
        var manifest = Path.Combine(AppContext.BaseDirectory, "SpellArt", "RuleSets", "Dnd5e", "spell-icons.json");
        using var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
        var icons = data.RootElement.GetProperty("icons").EnumerateArray().ToArray();
        Assert.True(icons.Length >= 204);
        foreach (var icon in icons)
        {
            Assert.Equal("spell", icon.GetProperty("usage").GetString());
            var lines = catalog.Render(icon.GetProperty("name").GetString());
            Assert.NotNull(lines);
            Assert.Equal(18, lines.Length);
            Assert.All(lines, line => Assert.Equal(36, line.Length));
            Assert.Contains(lines, line => line.Any(c => c != '\u2800'));
            var cells = lines.SelectMany(line => PixelArt.Colors(line)!);
            Assert.Contains(cells, cell => cell.Fg != null);
            Assert.Contains(cells, cell => cell.Bg != null && cell.BgAlpha > 0);
        }
    }
}
