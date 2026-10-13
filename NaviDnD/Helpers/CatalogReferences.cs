using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NaviDnD.Helpers;

// Ссылки на справочники в ответе мастера — вместо вызова инструмента на каждый предмет, а каждый вызов — лишний круг
// модели (~10 с и весь контекст заново). Мастер пишет английское название, игра подставляет данные справочника:
//  "icon": "longsword,sword" (от точного к общему) → "image": первый найденный слаг game-icons.net;
//  "ref" в spells[] или с "level" (варианты заклинаний при повышении уровня) → каноничное name заклинания;
//  "ref" у предмета → каноничное name предмета, а без своей картинки — иконка по тому же названию;
//  "monster" у существа → monsterKey (полные статы игра берёт из бестиария сама).
// Название и ключ — только при ТОЧНОМ совпадении английского названия: нестрогий поиск путал похожие ("ink" →
// «Ink pen», и бутылочка чернил становилась писчим пером). Не совпало — остаётся name мастера (он пишет его всегда),
// существо — без статов; картинка ищется нестрого (от точного к общему), не нашлась — без неё.
public static class CatalogReferences
{
    private static readonly JsonSerializerOptions Output = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Resolve(string json)
    {
        if (!json.Contains("\"icon\"") && !json.Contains("\"ref\"") && !json.Contains("\"monster\"")) return json;
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return json; }   // битый ответ чинит EnsureValidJson, ссылки — уже в починенном
        if (root == null) return json;
        Walk(root, null);
        return root.ToJsonString(Output);
    }

    private static void Walk(JsonNode node, string? key)
    {
        switch (node)
        {
            case JsonObject obj:
                ResolveObject(obj, key);
                foreach (var (name, child) in obj.ToList())
                    if (child != null) Walk(child, name);
                break;
            case JsonArray array:
                foreach (var item in array)
                    if (item != null) Walk(item, key);
                break;
        }
    }

    // key — имя свойства, в котором лежит объект (или массив с ним): "spells", "inventory", "entities"…
    private static void ResolveObject(JsonObject obj, string? key)
    {
        string? icon = Take(obj, "icon");
        if (Take(obj, "ref") is { } reference)
        {
            if (key == "spells" || obj.ContainsKey("level")) Spell(obj, reference);
            else
            {
                Item(obj, reference);
                icon ??= reference;
            }
        }
        if (Take(obj, "monster") is { } monster && Exact(CatalogSearch.Monsters(monster), m => m.NameEn, monster) is { } found)
            obj["monsterKey"] ??= found.LocalizedName;
        if (icon != null && obj["image"] == null && CatalogSearch.IconsFor(icon).Slugs is [var slug, ..])
            obj["image"] = slug;
    }

    // Каноничное название — по нему игра находит текст заклинания для карточки.
    private static void Spell(JsonObject obj, string reference)
    {
        if (Exact(CatalogSearch.Spells(reference), s => s.NameEn, reference) is not { } spell) return;
        obj["name"] = spell.Localized.Name;
        obj["level"] ??= spell.Level;
    }

    private static void Item(JsonObject obj, string reference)
    {
        if (Exact(CatalogSearch.Items(reference), i => i.NameEn, reference) is { } item)
            obj["name"] = item.LocalizedName;
    }

    // Совпадение по английскому названию целиком (с точностью до регистра, дефисов и апострофов).
    private static T? Exact<T>(IEnumerable<T> candidates, Func<T, string> englishName, string reference) where T : class =>
        candidates.FirstOrDefault(c => CatalogSearch.Score(englishName(c), reference) == 100);

    private static string? Take(JsonObject obj, string name)
    {
        string? value = Text(obj, name);
        obj.Remove(name);
        return value;
    }

    private static string? Text(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
}
