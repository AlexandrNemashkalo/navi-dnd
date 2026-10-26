using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NaviDnD.Helpers;

// Links to reference books in the wizard's response - instead of calling a tool for each item, and each call is
// an extra circle of the model (~10 s and the whole context again). The GM writes the English name, the game
// substitutes the directory data: "icon": "longsword,sword" (from exact to general) → "image": the first found
// slug game-icons.net;  "ref" to spells[] or with "level" (spell options when leveling up) → canonical name
// spells;  "ref" for an item → the canonical name of the item, and without its own picture - an icon of the
// same name;  "monster" from the creature → monsterKey (the game takes its full stats from the bestiary
// itself). The name and key - only with an EXACT match of the English name: a loose search confused similar
// ones ("ink" → "Ink pen", and the bottle of ink became a writing pen). If it doesn’t match, the GM’s name
// remains (he always writes it), the creature has no stats; The image is searched loosely (from the exact to
// the general), but it is not found - without it.
public static class CatalogReferences
{
    private static readonly JsonSerializerOptions Output = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Resolve(string json)
    {
        if (!json.Contains("\"icon\"") && !json.Contains("\"ref\"") && !json.Contains("\"monster\"")) return json;
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return json; }   // broken answer is repaired by EnsureValidJson, links are already in the repaired one
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

    // key - name of the property in which the object (or an array with it) lies: "spells", "inventory", "entities"…
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
        if (icon != null && obj["image"] == null && CatalogSearch.IconsFor(icon, defaultCategory: DefaultIconCategory(key)).Slugs is [var slug, ..])
            obj["image"] = slug;
    }

    // Kind of picture by the place in the answer - when nothing is found, the default picture of this kind.
    private static string DefaultIconCategory(string? key) => key switch
    {
        "entities" or "hero" or "npcs" => "creature",
        "objects" or "doors" or "furniture" => "feature",
        "spells" => "spell",
        _ => "item",
    };

    // Canonical name - the game uses it to find the spell text for the card.
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

    // Match the entire English name (up to case, hyphens and apostrophes).
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
