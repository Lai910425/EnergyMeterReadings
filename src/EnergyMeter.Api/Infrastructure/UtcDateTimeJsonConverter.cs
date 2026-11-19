using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnergyMeter.Api.Infrastructure;

/// <summary>
/// JSON body handling for DateTime: requires an explicit zone on input, always writes UTC "Z" on output.
/// Applies to DateTime? as well (System.Text.Json wraps it automatically).
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Timestamp {UtcTimestamp.ExpectedFormatMessage}.");
        }

        var text = reader.GetString();
        if (!UtcTimestamp.TryParse(text, out var utc))
        {
            throw new JsonException($"'{text}' is not valid: timestamp {UtcTimestamp.ExpectedFormatMessage}.");
        }

        return utc;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(UtcTimestamp.Format(value));
}
