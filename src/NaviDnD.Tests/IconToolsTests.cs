using System.Text.Json;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

// Unit tests for searching for icons (find_icon) without a AI - work against the real icons/ folder on disk
// (static set of files game-icons.net, 4173 pcs., does not change during the session).
[Collection(IconPackCollection.Name)]
public class IconToolsTests
{
    private static IconTools CreateTools() => new(new McpServerConfig(WorldStatePath: "unused", LogPath: null));

    [Theory]
    [InlineData("game-icons")]
    [InlineData("dcss")]
    public void PluralHorsesDoNotMatchHorseshoes(string pack)
    {
        var horse = NaviDnD.Helpers.CatalogSearch.IconsFor("creature: horse", pack, "creature");
        Assert.Equal(horse.Slugs[0], NaviDnD.Helpers.CatalogSearch.IconsFor("creature: horses", pack, "creature").Slugs[0]);
        Assert.Equal("delapouite/horseshoe", NaviDnD.Helpers.CatalogSearch.IconsFor("item: horseshoe", "game-icons").Slugs[0]);
        Assert.Equal("delapouite/moon-bats", NaviDnD.Helpers.CatalogSearch.IconsFor("creature: bats", "game-icons").Slugs[0]);
    }

    [Theory]
    [InlineData("creature: clerk carrying a book")]
    [InlineData("creature: miller with a hammer")]
    [InlineData("creature: farmer holding a pitchfork")]
    public void UnknownOccupationNeverBecomesItsCarriedObject(string query)
    {
        foreach (string pack in new[] { "game-icons", "dcss" })
        {
            string occupation = query.Split(' ')[1];
            var expected = NaviDnD.Helpers.CatalogSearch.IconsFor("creature: " + occupation, pack, "creature");
            Assert.Equal(expected.Slugs[0], NaviDnD.Helpers.CatalogSearch.IconsFor(query, pack, "creature").Slugs[0]);
        }
    }

    [Theory]
    [InlineData("pack of wolves", "wolf")]
    [InlineData("flock of ravens", "raven")]
    [InlineData("herd of horses", "horse")]
    [InlineData("swarm of bats", "bat")]
    [InlineData("group of goblins carrying spears", "goblin")]
    public void CreatureGroupsKeepTheirSpecies(string description, string species)
    {
        foreach (string pack in new[] { "game-icons", "dcss" })
            Assert.Equal(NaviDnD.Helpers.CatalogSearch.IconsFor("creature: " + species, pack, "creature").Slugs[0],
                NaviDnD.Helpers.CatalogSearch.IconsFor("creature: " + description, pack, "creature").Slugs[0]);
    }

    [Theory]
    [InlineData("feature: weathered signpost", "delapouite/direction-signs")]
    [InlineData("feature: mossy stone cairn", "delapouite/stone-pile")]
    [InlineData("feature: weathered notice board", "lorc/wooden-sign")]
    [InlineData("item: broken wooden pegs", "delapouite/wood-stick")]
    [InlineData("item: frayed coil of rope", "delapouite/rope-coil")]
    [InlineData("feature: weathered rope bridge", "delapouite/rope-bridge")]
    [InlineData("feature: mossy stone bridge", "delapouite/stone-bridge")]
    [InlineData("feature: old stone bridge with wooden signs", "delapouite/stone-bridge")]
    public void DescriptiveModifiersKeepTheMeaningOfCompoundProps(string query, string expected)
    {
        foreach (string pack in new[] { "game-icons", "dcss" })
            Assert.Equal(expected, NaviDnD.Helpers.CatalogSearch.IconsFor(query, pack).Slugs.FirstOrDefault());
    }

    [Theory]
    [InlineData("game-icons")]
    [InlineData("dcss")]
    public void PropAliasesDoNotReplaceThePrimaryObjectWithItsAccessories(string pack)
    {
        var chest = NaviDnD.Helpers.CatalogSearch.IconsFor("feature: chest", pack);
        Assert.NotEmpty(chest.Slugs);
        Assert.Equal(chest.Slugs[0], NaviDnD.Helpers.CatalogSearch
            .IconsFor("feature: chest with a carved standing stone", pack).Slugs[0]);
        Assert.Equal("delapouite/direction-signs", NaviDnD.Helpers.CatalogSearch
            .IconsFor("feature: weathered signposts with lanterns", pack).Slugs[0]);
    }

    [Theory]
    [InlineData("dcss")]
    [InlineData("game-icons")]
    public void NoticeBoardUsesSignRatherThanSportsEquipment(string pack)
    {
        var result = NaviDnD.Helpers.CatalogSearch.IconsFor("feature: notice board,board", pack);
        Assert.Equal("lorc/wooden-sign", result.Slugs[0]);
        Assert.Equal("delapouite/surf-board",
            NaviDnD.Helpers.CatalogSearch.IconsFor("surf board", pack).Slugs[0]);
    }

    [Theory]
    [InlineData("dcss")]
    [InlineData("game-icons")]
    public void FootprintsKeepTheirMeaningBeforeFallingBackToMud(string pack)
    {
        Assert.Equal("lorc/footprint", NaviDnD.Helpers.CatalogSearch.IconsFor("feature: footprints,mud", pack).Slugs[0]);
    }

    [Fact]
    public void FindIcon_ExactNameMatch_RanksFirst()
    {
        var tools = CreateTools();
        var slugs = Split(tools.FindIcon("torch"));

        Assert.Equal("delapouite/torch", slugs[0]);
    }

    [Fact]
    public void FindIcon_MultiWordQuery_RequiresAllWordsPresent()
    {
        var tools = CreateTools();
        var slugs = Split(tools.FindIcon("primitive torch"));

        Assert.Equal("delapouite/primitive-torch", slugs[0]);
    }

    [Fact]
    public void FindIcon_NoMatch_ReturnsEmptyResult()
    {
        var tools = CreateTools();
        var result = tools.FindIcon("zzznonexistenticonzzz");

        Assert.Equal("", result);
    }

    [Fact]
    public void FindIcon_FirstTermMisses_FallsBackToSecondTerm()
    {
        var tools = CreateTools();
        var slugs = Split(tools.FindIcon("zzznonexistentzzz,torch"));

        Assert.Equal("delapouite/torch", slugs[0]);
    }

    [Fact]
    public void FindIcon_FirstTermHits_DoesNotTrySecondTerm()
    {
        var tools = CreateTools();
        var slugs = Split(tools.FindIcon("torch,sword"));

        Assert.All(slugs, slug => Assert.Contains("torch", slug));
    }

    [Fact]
    public void FindIcon_MatchesAreCapped()
    {
        var tools = CreateTools();
        var slugs = Split(tools.FindIcon("sword"));

        Assert.True(slugs.Length <= 20);
    }

    [Fact]
    public void FindIcon_EmptyQuery_ReturnsError()
    {
        var tools = CreateTools();
        var result = JsonDocument.Parse(tools.FindIcon("   "));

        Assert.True(result.RootElement.TryGetProperty("error", out _));
    }

    private static string[] Split(string result) =>
        result.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
