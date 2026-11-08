using System.Text.Json;
using System.Text.Json.Nodes;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;

namespace NaviDnD.Tests;

[Collection(IconPackCollection.Name)]
public sealed class NpcPortraitTests
{
    [Fact]
    public void FacesPersistAcrossMapChangesSaveReloadAndModelPatches()
    {
        var previous = ArtPictures.Current;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e");
            var storage = new Storage();
            storage.ApplyUpdateWorldState("""{"map":{"entities":[{"name":"Lada","image":"dcss/monster/human"}]},"narrative":{"npcs":[{"name":"Lada","gender":"female","met":true},{"name":"Sava","gender":"male","met":true}]}}""");
            var lada = storage.WorldState.Narrative!.Npcs[0];
            var sava = storage.WorldState.Narrative.Npcs[1];
            Assert.Contains(lada.Portrait, art.PortraitsForGroup("human"));
            Assert.NotEqual(lada.Portrait, sava.Portrait);
            string face = lada.Portrait!;
            storage.WorldState.Map.Entities!.Clear();
            storage.ApplyUpdateWorldState("""{"narrative":{"npcs":[{"id":0,"notes":"New encounter","portrait":"lorc/uncertainty"}]}}""");
            Assert.Equal(face, storage.WorldState.Narrative.Npcs[0].Portrait);
            var copy = new Storage();
            copy.ApplySavedState(JsonSerializer.Serialize(storage.WorldState, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            var loaded = copy.WorldState;
            Assert.Equal(face, NpcPortraits.Find(loaded, " lada ")!.Portrait);
            Assert.DoesNotContain("portrait", new AiContextBuilder(loaded).Serialize(["narrative.npcs"]), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("New encounter", loaded.Narrative!.Npcs[0].Notes);
        }
        finally { ArtPictures.Current = previous; }
    }

    [Fact]
    public void MapNpcGetsFaceBeforeJournalEntryAndMonstersKeepTheirArt()
    {
        var previous = ArtPictures.Current;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e");
            var storage = new Storage();
            storage.ApplyUpdateWorldState("""{"map":{"entities":[{"name":"Innkeeper","image":"dcss/monster/human"},{"name":"Enemy","monsterKey":"Goblin","image":"dcss/monster/goblin"}]}}""");
            string face = storage.WorldState.Map.Entities![0].Image!;
            Assert.Contains(face, art.PortraitsForGroup("human"));
            Assert.Equal("dcss/monster/goblin", storage.WorldState.Map.Entities[1].Image);
            storage.ApplyUpdateWorldState("""{"narrative":{"npcs":[{"name":"Innkeeper","met":true}]}}""");
            Assert.Equal(face, storage.WorldState.Narrative!.Npcs[0].Portrait);
        }
        finally { ArtPictures.Current = previous; }
    }

    [Fact]
    public void CustomFacesAndNonHumanoidCreaturesAreNotReplaced()
    {
        var art = ArtPictures.Load("Dnd5e");
        Assert.Equal("portraits/custom.png", art.ChooseNpcPortrait("Lada", "female", "portraits/custom.png", new HashSet<string>()));
        Assert.Null(art.ChooseNpcPortrait("Wolf", null, "dcss/monster/animals/wolf", new HashSet<string>()));
        Assert.Contains(art.ChooseNpcPortrait("Orc", null, "dcss/monster/orc_warrior", new HashSet<string>()), art.PortraitsForGroup("orc"));
        Assert.Contains(art.ChooseNpcPortrait("Citizen", null, art.Find("Commoner", "creature").Single(), new HashSet<string>()), art.PortraitsForGroup("human"));
        string sailor = art.Find("sailor", "npc_role").Single();
        Assert.Equal(sailor, art.ChooseNpcPortrait("Sailor", null, sailor, new HashSet<string>()));
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ArtLibrary/RuleSets/Dnd5e/runtime-pictures.json")))!;
        foreach (string gender in new[] { "male", "female" })
        {
            string chosen = art.ChooseNpcPortrait("same identity", gender, "dcss/monster/human", new HashSet<string>())!;
            var portrait = manifest["portraits"]!.AsArray().Single(p => (string?)p!["imageId"] == chosen[ArtPictures.Prefix.Length..]);
            Assert.Equal(gender, (string?)portrait!["gender"]);
        }
    }

    [Theory]
    [InlineData("delapouite/grass")]
    [InlineData("delapouite/trail")]
    [InlineData("delapouite/forest")]
    [InlineData("lorc/incense")]
    public void PreparedTerrainAndIncenseUseColorPixels(string slug)
    {
        var previous = ArtPictures.Current;
        string pack = CatalogSearch.IconPack;
        try
        {
            ArtPictures.Current = ArtPictures.Load("Dnd5e");
            CatalogSearch.IconPack = CatalogSearch.DcssPack;
            Assert.StartsWith(ArtPictures.Prefix, PackPictures.ForPack(slug));
            var lines = SvgToBrailleConverter.Convert(slug, "")!;
            Assert.Contains(lines, line => PixelArt.Colors(line)?.Any(c => c.Fg != null) == true);
            Assert.DoesNotContain(lines, line => line.Any(c => c is >= '\u2800' and <= '\u28ff'));
        }
        finally { ArtPictures.Current = previous; CatalogSearch.IconPack = pack; }
    }

    [Fact]
    public void EveryTerrainIsPixelRenderedAndSpellFallbackKeepsBrailleWithSeparateCache()
    {
        var previous = ArtPictures.Current;
        string pack = CatalogSearch.IconPack;
        try
        {
            ArtPictures.Current = ArtPictures.Load("Dnd5e");
            CatalogSearch.IconPack = CatalogSearch.DcssPack;
            foreach (var terrain in TerrainCatalog.Kinds.Values)
            {
                var lines = SvgToBrailleConverter.Convert(ArtPictures.TerrainImage(terrain)!, "");
                Assert.NotNull(lines);
                Assert.DoesNotContain(lines, line => line.Any(c => c is >= '\u2800' and <= '\u28ff'));
            }
            const string fallback = "delapouite/high-grass";
            var braille = SvgToBrailleConverter.Convert(fallback, "", applyPackMapping: false)!;
            Assert.Contains(braille, line => line.Any(c => c is >= '\u2800' and <= '\u28ff'));
            var pixels = SvgToBrailleConverter.Convert(fallback, "")!;
            Assert.DoesNotContain(pixels, line => line.Any(c => c is >= '\u2800' and <= '\u28ff'));
        }
        finally { ArtPictures.Current = previous; CatalogSearch.IconPack = pack; }
    }
}
