using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaviDnD.Data.Models;

// A list of strings (aiInfo, memory, knownMonsters, ...), which the AI sometimes sends in one line -
// "aiInfo": "text" instead of ["text"]. Without this, the entire response would fail during parsing
// (JsonException), and the patch would not be applied entirely. Single value → list of one element; null →
// null.
public class LenientStringListConverter : JsonConverter<List<string>?>
{
    private static readonly LenientStringConverter Item = new();

    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.StartArray:
                var list = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var value = Item.Read(ref reader, typeof(string), options);
                    if (value != null) list.Add(value);
                }
                return list;
            default:
                var single = Item.Read(ref reader, typeof(string), options);
                return single == null ? [] : [single];
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value == null) { writer.WriteNullValue(); return; }
        writer.WriteStartArray();
        foreach (var s in value) writer.WriteStringValue(s);
        writer.WriteEndArray();
    }
}
