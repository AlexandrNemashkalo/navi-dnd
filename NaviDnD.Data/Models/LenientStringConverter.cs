using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaviDnD.Data.Models;

// ИИ иногда присылает число там, где формат ожидает строку (например value:4 вместо value:"4"
// для стата с числовым смыслом типа "Подготовлено заклинаний") — по умолчанию System.Text.Json на
// этом бросает JsonException, которое обрывает весь патч героя на середине (см. HeroSpell.Prepared
// комментарий рядом, Hero.Stats и т.д. дефолтятся в [] как вторая линия защиты от той же причины).
// Этот конвертер просто принимает число и как есть, и как строку.
public class LenientStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
            JsonTokenType.True or JsonTokenType.False => reader.GetBoolean().ToString(),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for string value"),
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
