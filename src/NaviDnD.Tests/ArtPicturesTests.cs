using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NaviDnD.Display;
using NaviDnD.Data;
using NaviDnD.Helpers;
using NaviDnD.McpServer;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

[Collection(IconPackCollection.Name)]
public sealed class ArtPicturesTests
{
    [Theory]
    [InlineData("item: wooden pegs,wood")]
    [InlineData("item: wooden peg")]
    public void RepairPegsUseWoodRatherThanAProstheticLeg(string query)
    {
        Assert.Equal("delapouite/wood-stick", CatalogSearch.IconsFor(query, pack: CatalogSearch.GameIconsPack).Slugs.First());
    }

    [Theory]
    [InlineData("creature: human forester,human")]
    [InlineData("human forester,human")]
    [InlineData("creature: human")]
    [InlineData("creature: villagers")]
    [InlineData("creature: commoner")]
    [InlineData("creature: citizen")]
    public void HumanCreatureFallbackUsesAPersonInsteadOfAnUnrelatedCompoundIcon(string query)
    {
        string previous = CatalogSearch.IconPack;
        try
        {
            CatalogSearch.IconPack = CatalogSearch.GameIconsPack;
            string response = JsonSerializer.Serialize(new { map = new { entities = new[] { new { name = "Forester", icon = query } } } });
            var parsed = JsonNode.Parse(CatalogReferences.Resolve(response))!;
            Assert.Equal("delapouite/person", (string?)parsed["map"]!["entities"]![0]!["image"]);
            Assert.NotNull(SvgToBrailleConverter.Convert("delapouite/person", "", applyPackMapping: false));
            Assert.Equal("delapouite/human-ear", CatalogSearch.IconsFor("item: human ear").Slugs.First());
        }
        finally { CatalogSearch.IconPack = previous; }
    }

    [Fact]
    public void CharacterCreationOffersReviewedFacesInsteadOfMapActors()
    {
        var previous = ArtPictures.Current;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e", Path.Combine(AppContext.BaseDirectory, "ArtLibrary"));
            Assert.Equal(171, art.Portraits.Count);
            Assert.Single(art.PortraitsForGroup("dragonborn"));
            Assert.Equal(12, art.PortraitsForGroup("tiefling").Count);
            Assert.Equal(48, art.PortraitsForGroup("halfling").Count);
            Assert.Equal(48, art.PortraitsForGroup("human").Count);
            Assert.Equal(16, art.PortraitsForGroup("elf").Count);
            Assert.Equal(16, art.PortraitsForGroup("dark_elf").Count);
            var type = typeof(NewGameDisplay);
            foreach (string property in new[] { "PixelGallery", "Gallery" })
            {
                var gallery = (System.Collections.IEnumerable)type.GetProperty(property, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                foreach (var portrait in gallery)
                {
                    string slug = (string)portrait.GetType().GetProperty("Image")!.GetValue(portrait)!;
                    Assert.False(slug.StartsWith("dcss/"), slug);
                    if (property == "PixelGallery") Assert.Contains(slug, art.Portraits);
                    else Assert.True(slug.Contains("face") || slug.Contains("head") || slug.EndsWith("bust") || slug.EndsWith("profile"), slug);
                    Assert.NotNull(SvgToBrailleConverter.Convert(slug, "", applyPackMapping: false));
                }
            }
            var packOf = type.GetMethod("PackOfImage", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.Equal(1, packOf.Invoke(null, [art.Portraits[0]]));
            var choose = type.GetMethod("PortraitsForRace", BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (string group in new[] { "human", "elf", "dark_elf", "dwarf", "orc", "goblin", "halfling", "unknown", "" })
                foreach (string pack in new[] { CatalogSearch.DcssPack, CatalogSearch.GameIconsPack })
                {
                    var portraits = ((System.Collections.IEnumerable)choose.Invoke(null, [group, pack])!).Cast<object>().ToArray();
                    Assert.NotEmpty(portraits);
                    if (pack == CatalogSearch.GameIconsPack) Assert.Equal(14, portraits.Length);
                    foreach (var portrait in portraits)
                    {
                        string slug = (string)portrait.GetType().GetProperty("Image")!.GetValue(portrait)!;
                        if (pack == CatalogSearch.DcssPack && art.PortraitsForGroup(group).Count > 0)
                            Assert.Contains(slug, art.PortraitsForGroup(group));
                        else if (pack == CatalogSearch.DcssPack)
                            Assert.Equal("lorc/uncertainty", slug);
                        else Assert.NotEqual("lorc/uncertainty", slug);
                    }
                }
            Assert.Empty(art.PortraitsForGroup("elf").Intersect(art.PortraitsForGroup("dark_elf")));
            Assert.Empty(art.PortraitsForGroup("orc").Intersect(art.PortraitsForGroup("goblin")));
            var options = JsonNode.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../NaviDnD/Prompts/Dnd5e/heroOptions.json"))))!;
            var races = options["races"]!.AsArray().ToDictionary(r => (string)r!["name"]!, r => (string?)r!["portraitGroup"]);
            Assert.Equal(races["High elf"], races["Half-elf"]);
            Assert.Equal(races["Orc"], races["Half-orc"]);
            Assert.Equal("halfling", races["Gnome"]);
            Assert.Equal("tiefling", races["Tiefling"]);
            Assert.Equal("dragonborn", races["Dragonborn"]);
            Assert.Equal(races["Halfling"], races["Gnome"]);
            Assert.DoesNotContain("Kenku", races.Keys);
            Assert.DoesNotContain("Lizardfolk", races.Keys);
        }
        finally { ArtPictures.Current = previous; }
    }

    [Fact]
    public void EveryPublishedNamedPictureExistsAndRendersWithColor()
    {
        var previous = ArtPictures.Current;
        try
        {
            string packagedRoot = Path.Combine(AppContext.BaseDirectory, "ArtLibrary");
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e", packagedRoot);
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                packagedRoot, "RuleSets", "Dnd5e", "runtime-pictures.json")));
            var bindings = document.RootElement.GetProperty("bindings");
            Assert.True(bindings.GetArrayLength() >= 598);
            foreach (var binding in bindings.EnumerateArray())
            {
                string slug = ArtPictures.Prefix + binding.GetProperty("imageId").GetString();
                if (binding.TryGetProperty("palette", out var palette)) slug += "~" + palette.GetString();
                foreach (var name in binding.GetProperty("names").EnumerateArray())
                    foreach (var category in binding.GetProperty("categories").EnumerateArray())
                        Assert.Contains(slug, art.Find(name.GetString()!, category.GetString()));
                Assert.True(File.Exists(art.ResolvePath(slug)), slug);
                var lines = SvgToBrailleConverter.Convert(slug, "", applyPackMapping: false);
                Assert.NotNull(lines);
                Assert.Equal(18, lines.Length);
                Assert.Contains(lines, line => line.Any(c => c is '▀' or '▄'));
                Assert.Contains(lines, line => PixelArt.Colors(line)?.Any(c => c.Fg != null || c.Bg != null) == true);
            }
        }
        finally { ArtPictures.Current = previous; }
    }

    [Fact]
    public void AiReferenceAndMcpUseSamePictureAndStoragePreservesIt()
    {
        var previous = ArtPictures.Current;
        string pack = CatalogSearch.IconPack;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e");
            CatalogSearch.IconPack = CatalogSearch.DcssPack;
            string expected = art.Find("Magnifying glass", "item").Single();
            var tools = new IconTools(new McpServerConfig(WorldStatePath: "unused", LogPath: null));
            Assert.Equal(expected, tools.FindIcon("item: Magnifying glass"));
            string resolved = CatalogReferences.Resolve("""{"hero":{"inventory":[{"ref":"Magnifying glass","name":"Лупа","icon":"item: glass","count":1}]}}""");
            var parsed = JsonNode.Parse(resolved)!;
            Assert.Equal(expected, (string?)parsed["hero"]!["inventory"]![0]!["image"]);
            var storage = new Storage();
            storage.ApplyUpdateWorldState(resolved);
            var item = Assert.Single(storage.WorldState.Hero!.Inventory!);
            Assert.Equal(expected, item.Image);
            var roundTrip = JsonSerializer.Deserialize<HeroInventory>(JsonSerializer.Serialize(item));
            Assert.Equal(expected, roundTrip!.Image);
            Assert.NotNull(SvgToBrailleConverter.Convert(roundTrip.Image!, ""));
        }
        finally { ArtPictures.Current = previous; CatalogSearch.IconPack = pack; }
    }

    [Fact]
    public async Task OldInventoryUsesNamedIllustrationWithoutMutatingTheSavedItem()
    {
        var previous = ArtPictures.Current;
        string pack = CatalogSearch.IconPack;
        try
        {
            ArtPictures.Current = ArtPictures.Load("Dnd5e");
            CatalogSearch.IconPack = CatalogSearch.DcssPack;
            var item = new HeroInventory { Name = "Песочные часы", Image = "lorc/swap-bag" };
            string before = JsonSerializer.Serialize(item);
            var display = new DisplayConfig();
            typeof(SpellArt).Assembly.GetType("NaviDnD.MouseUiHelper")!.GetMethod("SetSelectedInventoryItem", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [display, item, 0]);
            // PreparePicture may decode on the background path; select again after preparation finishes.
            string slug = ArtPictures.Current.Find(item.Name, "item").Single();
            await SvgToBrailleConverter.PrepareAsync(slug);
            typeof(SpellArt).Assembly.GetType("NaviDnD.MouseUiHelper")!.GetMethod("SetSelectedInventoryItem", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [display, item, 0]);
            Assert.Contains(display.SelectedImageLines!, line => PixelArt.Colors(line)?.Any(c => c.Fg != null) == true);
            Assert.Equal(before, JsonSerializer.Serialize(item));
        }
        finally { ArtPictures.Current = previous; CatalogSearch.IconPack = pack; }
    }

    [Fact]
    public void RolesAndSpecificityArePreservedAndSpellsDoNotLeakIntoItemSearch()
    {
        var previous = ArtPictures.Current;
        string pack = CatalogSearch.IconPack;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e");
            CatalogSearch.IconPack = CatalogSearch.DcssPack;
            Assert.Empty(art.Find("Fireball", "item"));
            Assert.Empty(art.Find("Magnifying glass", "creature"));
            Assert.Empty(art.Find("../Runtime/Images/anything", "item"));
            Assert.Null(art.ResolvePath("art32/../../settings.json"));
            Assert.Equal("goblin", CatalogSearch.IconsFor("creature: goblin,torch").Term);
            Assert.DoesNotContain(CatalogSearch.IconsFor("item: torch", CatalogSearch.GameIconsPack).Slugs,
                slug => slug.StartsWith(ArtPictures.Prefix));
        }
        finally { ArtPictures.Current = previous; CatalogSearch.IconPack = pack; }
    }

    [Theory]
    [InlineData("Banderhobb", "Бандерхобб [Banderhobb]")]
    [InlineData("Half-Ogre (Ogrillon)", "Полуогр (Огриллон) (Half-Ogre (Ogrillon))")]
    public void MonsterEnglishRequestsAndExistingSavedKeysResolveToSameCreature(string english, string savedKey)
    {
        var monster = MonsterDatabase.Find(english);
        Assert.NotNull(monster);
        Assert.Equal(english, monster.NameEn);
        Assert.Same(monster, MonsterDatabase.Find(savedKey));
        Assert.Contains(monster, CatalogSearch.Monsters(english));
    }

    [Theory]
    [InlineData("Herbalism kit", "Набор травника", "item")]
    [InlineData("Travel satchel", "Дорожная сума", "item")]
    [InlineData("cart", "Телега", "feature")]
    public void TravelEquipmentNamesResolveToTheSameReviewedPicture(string english, string russian, string category)
    {
        var art = ArtPictures.Load("Dnd5e");
        var expected = Assert.Single(art.Find(english, category));
        Assert.StartsWith(ArtPictures.Prefix, expected);
        Assert.Equal(expected, Assert.Single(art.Find(russian, category)));
        Assert.True(File.Exists(art.ResolvePath(expected)));
    }

    [Fact]
    public void EveryMonsterReferenceHasAnEnglishSearchName()
    {
        Assert.All(MonsterDatabase.AllMonsters, monster => Assert.False(string.IsNullOrWhiteSpace(monster.NameEn), monster.Name));
    }
}
