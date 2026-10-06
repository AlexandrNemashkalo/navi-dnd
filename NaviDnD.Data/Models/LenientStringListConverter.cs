using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaviDnD.Data.Models;

// Список строк (aiInfo, memory, knownMonsters, …), который ИИ иногда присылает одной строкой —
// "aiInfo": "текст" вместо ["текст"]. Без этого весь ответ падал на разборе (JsonException), и
// патч не применялся целиком. Одиночное значение → список из одного элемента; null → null.
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
