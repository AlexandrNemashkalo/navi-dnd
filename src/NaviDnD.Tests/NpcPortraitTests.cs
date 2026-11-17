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
    public void NewJournalNpcCannotTakeAnExistingMapNpcsFace()
    {
        var previous = ArtPictures.Current;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e");
            string face = art.ChooseNpcPortrait("First", "female", null, new HashSet<string>())!;
            string name = Enumerable.Range(0, 10000).Select(i => $"New NPC {i}").First(n =>
                art.ChooseNpcPortrait(n, "female", null, new HashSet<string>()) == face);
            var storage = new Storage();
            storage.ApplyUpdateWorldState(JsonSerializer.Serialize(new
            {
                map = new { entities = new[] { new { name = "First", image = face } } },
                narrative = new { npcs = new[] { new { name, gender = "female", met = true } } }
            }));
            Assert.Equal(face, Assert.Single(storage.WorldState.Map.Entities!).Image);
            Assert.NotEqual(face, Assert.Single(storage.WorldState.Narrative!.Npcs).Portrait);
            Assert.NotNull(storage.WorldState.Narrative.Npcs[0].Portrait);
        }
        finally { ArtPictures.Current = previous; }
    }

    [Fact]
    public void IncompletePortraitPackNeverSubstitutesAnExplicitlyDifferentGender()
    {
        string root = Path.Combine(Path.GetTempPath(), "navidnd-portrait-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = Path.Combine(AppContext.BaseDirectory, "ArtLibrary");
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "RuleSets/Dnd5e/runtime-pictures.json")))!;
            var female = manifest["portraits"]!.AsArray().First(p =>
                (string?)p!["group"] == "human" && (string?)p["gender"] == "female")!;
            string id = (string)female["imageId"]!;
            Directory.CreateDirectory(Path.Combine(root, "Runtime/Images"));
            Directory.CreateDirectory(Path.Combine(root, "RuleSets/Dnd5e"));
            File.Copy(Path.Combine(source, "Runtime/Images", id + ".png"), Path.Combine(root, "Runtime/Images", id + ".png"));
            File.WriteAllText(Path.Combine(root, "RuleSets/Dnd5e/runtime-pictures.json"), manifest.ToJsonString());
            var art = ArtPictures.Load("Dnd5e", root);
            Assert.Single(art.Portraits);
            Assert.Null(art.ChooseNpcPortrait("Man", "male", null, new HashSet<string>()));
            Assert.Equal(ArtPictures.Prefix + id, art.ChooseNpcPortrait("Woman", "female", null, new HashSet<string>()));
            Assert.Null(art.ChooseNpcPortrait("Unknown", null, null, new HashSet<string>()));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

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
    public void TerrainHasArtworkAndPixelFallbackDoesNotSwitchToBraille()
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
                Assert.NotEmpty(lines);
            }
            const string fallback = "delapouite/high-grass";
            var braille = SvgToBrailleConverter.Convert(fallback, "", applyPackMapping: false)!;
            Assert.Contains(braille, line => line.Any(c => c is >= '\u2800' and <= '\u28ff'));
            var detailed = SvgToBrailleConverter.Convert(fallback, "")!;
            Assert.DoesNotContain(detailed, line => line.Any(c => c is >= '\u2800' and <= '\u28ff'));
            Assert.Contains(detailed, line => line.Contains('▀') || line.Contains('▄') || line.Contains('█'));
            Assert.True(detailed.SelectMany(line => PixelArt.Colors(line) ?? []).Where(c => c.Fg != null)
                .Select(c => string.Join(',', c.Fg!)).Distinct().Count() > 1);
            Assert.All(braille, line => Assert.Null(PixelArt.Colors(line)));
        }
        finally { ArtPictures.Current = previous; CatalogSearch.IconPack = pack; }
    }
}
