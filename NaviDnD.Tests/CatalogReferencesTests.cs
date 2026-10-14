using System.Text.Json.Nodes;
using NaviDnD.Helpers;

namespace NaviDnD.Tests;

// Links to reference books in the wizard's response (icon/ref/monster) - the game substitutes the data itself,
// without calling the tool. Against real reference books and the icons/ folder (Russian game is the default
// language).
public class CatalogReferencesTests
{
    private static JsonNode Resolve(string json) => JsonNode.Parse(CatalogReferences.Resolve(json))!;

    [Fact]
    public void IconBecomesImage()
    {
        var node = Resolve("""{"map":{"objects":[{"name":"Факел","icon":"torch"}]}}""");
        var obj = node["map"]!["objects"]![0]!;
        Assert.Equal("delapouite/torch", obj["image"]!.GetValue<string>());
        Assert.Null(obj["icon"]);
    }

    [Fact]
    public void OwnImageIsKeptAndIconDropped()
    {
        var node = Resolve("""{"hero":{"image":"lorc/dragon-head","icon":"torch"}}""");
        Assert.Equal("lorc/dragon-head", node["hero"]!["image"]!.GetValue<string>());
        Assert.Null(node["hero"]!["icon"]);
    }

    [Fact]
    public void SpellReferenceGivesCatalogName()
    {
        var node = Resolve("""{"hero":{"spells":[{"ref":"fireball","level":3,"prepared":true}]}}""");
        var spell = node["hero"]!["spells"]![0]!;
        Assert.Equal("Огненный шар", spell["name"]!.GetValue<string>());
        Assert.Null(spell["ref"]);
        Assert.True(spell["prepared"]!.GetValue<bool>());
    }

    [Fact]
    public void ItemReferenceGivesNameAndIcon()
    {
        var node = Resolve("""{"hero":{"inventory":[{"ref":"longsword","icon":"longsword,sword","equipmentedSlot":["П Рука"]}]}}""");
        var item = node["hero"]!["inventory"]![0]!;
        Assert.Equal("Длинный меч", item["name"]!.GetValue<string>());
        Assert.NotNull(item["image"]);
        Assert.Null(item["ref"]);
    }

    [Fact]
    public void CatalogNameReplacesMastersButUnknownKeepsIt()
    {
        var node = Resolve("""{"hero":{"inventory":[{"ref":"longsword","name":"Длинный меч стражи","description":"1к8 рубящий"},{"ref":"zzzunknownzzz","name":"Амулет Хравна"}]}}""");
        var items = node["hero"]!["inventory"]!;
        Assert.Equal("Длинный меч", items[0]!["name"]!.GetValue<string>());
        Assert.Equal("1к8 рубящий", items[0]!["description"]!.GetValue<string>());
        Assert.Equal("Амулет Хравна", items[1]!["name"]!.GetValue<string>());
    }

    // A loose search confused similar ones: “ink” found “Ink pen”, and the bottle of ink became a writing pen.
    [Fact]
    public void PartialReferenceKeepsMastersName()
    {
        var node = Resolve("""{"hero":{"inventory":[{"ref":"ink","name":"Бутылочка чернил"},{"ref":"knife","name":"Маленький нож"}]}}""");
        var items = node["hero"]!["inventory"]!;
        Assert.Equal("Бутылочка чернил", items[0]!["name"]!.GetValue<string>());
        Assert.Equal("Маленький нож", items[1]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void MonsterGivesMonsterKeyInsideHistoryPatch()
    {
        var node = Resolve("""{"history":[{"text":"…","patch":{"map":{"entities":[{"name":"Гоблин","monster":"goblin"}]}}}]}""");
        var entity = node["history"]![0]!["patch"]!["map"]!["entities"]![0]!;
        Assert.Equal("Гоблин", entity["monsterKey"]!.GetValue<string>());
        Assert.Null(entity["monster"]);
    }

    [Fact]
    public void UnknownReferencesAreDroppedWithoutData()
    {
        var node = Resolve("""{"map":{"entities":[{"name":"Нечто","monster":"zzzunknownzzz","icon":"zzzunknownzzz"}]}}""");
        var entity = node["map"]!["entities"]![0]!;
        Assert.Null(entity["monsterKey"]);
        Assert.Null(entity["image"]);
        Assert.Null(entity["monster"]);
        Assert.Null(entity["icon"]);
    }

    [Fact]
    public void JsonWithoutReferencesIsReturnedAsIs()
    {
        const string json = """{"history":[{"text":"<speak>Привет</speak>"}]}""";
        Assert.Same(json, CatalogReferences.Resolve(json));
    }
}
