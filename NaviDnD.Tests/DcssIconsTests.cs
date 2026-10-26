using System.Text.Json.Nodes;
using NaviDnD.Helpers;

namespace NaviDnD.Tests;

// The second pack of pictures - Dungeon Crawl Stone Soup tiles (icons/dcss, PNG 32×32): search by the name of
// the tile and its folders, render to braille. The pack is passed explicitly - the static setting is common for
// parallel tests.
[Collection(IconPackCollection.Name)]
public class DcssIconsTests
{
    private static List<string> Find(string term) => CatalogSearch.Icons(term, pack: CatalogSearch.DcssPack);

    [Fact]
    public void Icons_OldAndNewArt_AreOneName()
    {
        Assert.Equal(["dcss/monster/orc_warrior_new", "dcss/monster/orc_warrior_old"], Find("orc warrior")[..2]);
    }

    [Fact]
    public void Icons_NumberedVariants_MatchExactly()
    {
        Assert.StartsWith("dcss/item/weapon/long_sword", Find("long sword")[0]);
    }

    [Fact]
    public void Icons_FolderName_FindsTilesNamedOtherwise()
    {
        // Potions are named by their look ("potion_bubbly"), rings by material - the item folder decides.
        Assert.StartsWith("dcss/item/ring/", Find("ring")[0]);
        Assert.StartsWith("dcss/item/potion/", Find("potion")[0]);
    }

    [Fact]
    public void Icons_CreaturesBeforeSpellIcons()
    {
        Assert.Equal("dcss/monster/dragons/dragon", Find("dragon")[0]);
    }

    [Fact]
    public void Icons_PaperDollLayers_NotSearched()
    {
        Assert.DoesNotContain(Find("boots"), slug => slug.StartsWith("dcss/player/boots/"));
    }

    [Fact]
    public void IconsFor_DefaultPack_StaysGameIcons()
    {
        Assert.DoesNotContain(CatalogSearch.IconsFor("torch", CatalogSearch.GameIconsPack).Slugs, slug => slug.StartsWith(CatalogSearch.DcssPrefix));
    }

    [Fact]
    public void Convert_DcssTile_RendersColorHalfBlocksInPictureBlock()
    {
        var lines = SvgToBrailleConverter.Convert("dcss/monster/orc_warrior_new", "");

        Assert.NotNull(lines);
        Assert.Equal(18, lines.Length);
        Assert.All(lines, line => Assert.Equal(36, line.Length));
        Assert.All(lines, line => Assert.All(line, c => Assert.Contains(c, " ▀▄")));
        // 32×32 → 1:1 with margins of 2 pixels: the first and last characters of each line are transparent.
        Assert.All(lines, line => Assert.Equal("  ", line[..2]));
        var colored = lines.Select(PixelArt.Colors).ToArray();
        Assert.All(colored, Assert.NotNull);
        Assert.Contains(colored, cells => cells!.Any(cell => cell.Fg != null && cell.Bg != null));
    }

    [Fact]
    public void Convert_DcssTile_WithName_KeepsColors()
    {
        var lines = SvgToBrailleConverter.Convert("dcss/monster/orc_warrior_new", "Orc");

        Assert.NotNull(lines);
        Assert.NotNull(PixelArt.Colors(lines[5]));
        Assert.Null(PixelArt.Colors(lines[^1]));
    }

    [Fact]
    public void Convert_DcssTile_SmallCard_ScalesDown()
    {
        var lines = SvgToBrailleConverter.Convert("dcss/monster/orc_warrior_new", "", 16);

        Assert.NotNull(lines);
        Assert.Equal(8, lines.Length);
        Assert.All(lines, line => Assert.Equal(16, line.Length));
    }

    [Fact]
    public void IconsFor_Category_SearchesOnlyItsFolder()
    {
        Assert.StartsWith("dcss/item/weapon/", CatalogSearch.IconsFor("weapon: sword", CatalogSearch.DcssPack).Slugs[0]);
        Assert.StartsWith("dcss/monster/", CatalogSearch.IconsFor("creature: dragon", CatalogSearch.DcssPack).Slugs[0]);
        Assert.StartsWith("dcss/dungeon/", CatalogSearch.IconsFor("feature: chest", CatalogSearch.DcssPack).Slugs[0]);
    }

    [Fact]
    public void IconsFor_CategoryWord_IsLastFallback()
    {
        // Potions are named by look: "healing" is not found, "potion" - any potion.
        var (term, slugs) = CatalogSearch.IconsFor("potion: healing", CatalogSearch.DcssPack);
        Assert.Equal("potion", term);
        Assert.StartsWith("dcss/item/potion/", slugs[0]);
    }

    [Fact]
    public void IconsFor_GameIcons_CategoryDoesNotBreakSearch()
    {
        Assert.Equal("delapouite/torch", CatalogSearch.IconsFor("item: torch", CatalogSearch.GameIconsPack).Slugs[0]);
        Assert.Equal("delapouite/torch", CatalogSearch.IconsFor("unknown: torch", CatalogSearch.GameIconsPack).Slugs[0]);
    }

    [Fact]
    public void HeroDoll_Slug_LayersInDrawingOrder()
    {
        var gear = new HeroDoll.Gear("hand_right/staff_mage+body/robe_blue+cloak/black", "short_black", "fem_black", "long_white");

        Assert.Equal("dcss/doll/cloak/black+base/human_male+body/robe_blue+hair/short_black+beard/long_white+hand_right/staff_mage",
            HeroDoll.Slug("human", female: false, gear));
        Assert.Equal("dcss/doll/cloak/black+base/elf_female+body/robe_blue+hair/fem_black+hand_right/staff_mage",
            HeroDoll.Slug("elf", female: true, gear));
        Assert.Equal("dcss/doll/cloak/black+base/kenku_wingless_male+body/robe_blue+hand_right/staff_mage",
            HeroDoll.Slug("kenku_wingless", female: false, gear, hair: false));
    }

    // Every race and class of the questionnaire assembles a portrait: all layer files exist.
    [Fact]
    public void HeroDoll_EveryRaceAndClassOfRules_Renders()
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppConfig.AssetDirectory("NaviDnD"), "Prompts", "Dnd5e", "heroOptions.json")))!;
        var gears = root["classes"]!.AsArray().SelectMany(c => c!["dolls"]!.AsArray())
            .Select(g => new HeroDoll.Gear((string)g!["gear"]!, (string?)g["hair"], (string?)g["hairFemale"], (string?)g["beard"])).ToList();
        Assert.Equal(24, gears.Count);
        foreach (var race in root["races"]!.AsArray())
            foreach (var gear in gears)
                foreach (bool female in new[] { false, true })
                {
                    string slug = HeroDoll.Slug((string)race!["doll"]!, female, gear, (bool?)race["dollHair"] ?? true);
                    using var doll = HeroDoll.Render(slug);
                    Assert.True(doll != null, slug);
                }
    }

    [Fact]
    public void Convert_HeroDoll_ColorPortraitInPictureBlock()
    {
        var lines = SvgToBrailleConverter.Convert("dcss/doll/base/human_male+body/robe_blue+head/wizard_blue+hand_right/staff_mage", "");

        Assert.NotNull(lines);
        Assert.Equal(18, lines.Length);
        Assert.All(lines, line => Assert.NotNull(PixelArt.Colors(line)));
        Assert.Null(SvgToBrailleConverter.Convert("dcss/doll/base/human_male+body/no_such_layer", ""));
    }

    // Dungeon Crawl has no everyday gear: the same terms in game-icons - a picture of another style instead of none.
    [Fact]
    public void IconsFor_Dcss_NotFound_FallsBackToGameIcons()
    {
        Assert.Equal("delapouite/torch", CatalogSearch.IconsFor("item: torch", CatalogSearch.DcssPack).Slugs[0]);
        // Potions are named by look - any potion of the pack, not a game-icons picture.
        Assert.StartsWith("dcss/item/potion/", CatalogSearch.IconsFor("potion: healing potion", CatalogSearch.DcssPack).Slugs[0]);
    }

    [Theory]
    [InlineData("creature: wolves", "dcss/monster/animals/wolf")]
    [InlineData("food: rations", "dcss/item/food/meat_ration_new")]
    [InlineData("creature: skeleton", "dcss/monster/undead/skeletons/skeleton_humanoid_large_new")]
    public void IconsFor_Dcss_SingularsWordsAndAliases(string query, string slug)
    {
        Assert.Equal(slug, CatalogSearch.IconsFor(query, CatalogSearch.DcssPack).Slugs[0]);
    }

    // A piece inside another word is not a match ("lute" is not "flute"); the engine gets the default picture of the
    // category, the MCP tools - an honest empty result.
    [Fact]
    public void IconsFor_NothingFound_DefaultOnlyForEngine()
    {
        Assert.Empty(CatalogSearch.IconsFor("item: lute", CatalogSearch.GameIconsPack).Slugs);
        Assert.Equal("lorc/swap-bag", CatalogSearch.IconsFor("item: lute", CatalogSearch.GameIconsPack, defaultCategory: "item").Slugs[0]);
        Assert.Equal("dcss/monster/human_new", CatalogSearch.IconsFor("innkeeper", CatalogSearch.DcssPack, defaultCategory: "creature").Slugs[0]);
        Assert.All(CatalogSearch.DefaultIcons, slug => Assert.True(slug.StartsWith(CatalogSearch.DcssPrefix)
            ? File.Exists(Path.Combine(AppConfig.AssetDirectory("icons"), slug + ".png"))
            : File.Exists(Path.Combine(AppConfig.AssetDirectory("icons"), "000000", "ffffff", "1x1", slug + ".svg")), slug));
    }

    // The engine's own pictures (terrain, doors, world map) - in tiles of the pack, only in a Dungeon Crawl game.
    [Fact]
    public void PackPictures_OnlyInDcssGame_AllTilesExist()
    {
        Assert.All(PackPictures.DcssTiles, tile => Assert.True(File.Exists(Path.Combine(AppConfig.AssetDirectory("icons"), tile + ".png")), tile));
        try
        {
            Assert.Equal("delapouite/grass", PackPictures.ForPack("delapouite/grass"));
            CatalogSearch.IconPack = CatalogSearch.DcssPack;
            Assert.Equal("dcss/dungeon/floor/grass/grass_0_new", PackPictures.ForPack("delapouite/grass"));
            Assert.Equal("delapouite/table", PackPictures.ForPack("delapouite/table"));   // no furniture in the pack
        }
        finally { CatalogSearch.IconPack = CatalogSearch.GameIconsPack; }
    }

    // The pack is bound to the game like the language: from the save; a save without the field - game-icons.
    [Fact]
    public void Storage_IconPack_FromSaveAndResetForOldSave()
    {
        var storage = new Storage();
        string path = Path.Combine(Path.GetTempPath(), $"navidnd-iconpack-{Guid.NewGuid():N}.json");
        try
        {
            storage.ApplySavedState("""{"iconPack":"dcss"}""");
            Assert.Equal(CatalogSearch.DcssPack, storage.WorldState.IconPack);
            Assert.Equal(CatalogSearch.DcssPack, CatalogSearch.IconPack);

            File.WriteAllText(path, """{"formatVersion":2,"language":"ru"}""");
            storage.LoadFrom(path);
            Assert.Equal(CatalogSearch.GameIconsPack, storage.WorldState.IconPack);
            Assert.Equal(CatalogSearch.GameIconsPack, CatalogSearch.IconPack);
        }
        finally
        {
            CatalogSearch.IconPack = CatalogSearch.GameIconsPack;
            File.Delete(path);
        }
    }
}

// Tests that read or change the pack of the current game (static CatalogSearch.IconPack) - not in parallel.
[CollectionDefinition(Name)]
public sealed class IconPackCollection
{
    public const string Name = "IconPack";
}
