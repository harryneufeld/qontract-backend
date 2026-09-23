using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qontract.API.Serialization;

/// <summary>
/// Serializes every <see cref="DateTime"/> as ISO-8601 with an explicit UTC
/// designator so the JSON contract is stable across the two paths by which
/// timestamps leave the API (issue #31):
///
/// <list type="bullet">
/// <item>create/update responses — the entity is still in memory with
/// <see cref="DateTimeKind.Utc"/>; System.Text.Json already appends 'Z';</item>
/// <item>list/detail responses — the entity was reloaded from SQLite, where
/// EF Core stores DateTime as TEXT without timezone info, so it comes back
/// with <see cref="DateTimeKind.Unspecified"/>; the default converter would
/// omit the 'Z' suffix.</item>
/// </list>
///
/// Dart's <c>DateTime.parse</c> treats an offset-less value as local time, so
/// the mixed formats made list/detail views display createdAt/modifiedAt/
/// uploadedAt shifted by the client's UTC offset (2 h in CEST) relative to
/// create responses. This converter normalizes on the way out:
///
/// <list type="bullet">
/// <item><see cref="DateTimeKind.Utc"/> — written unchanged with 'Z';</item>
/// <item><see cref="DateTimeKind.Unspecified"/> — the stored instant is UTC by
/// construction (all values are stamped with <c>DateTime.UtcNow</c>), so it is
/// written with 'Z';</item>
/// <item><see cref="DateTimeKind.Local"/> — converted to UTC, written with 'Z'
/// (defensive; services never produce local-kind timestamps).</item>
/// </list>
///
/// On read, values with an explicit offset are converted to UTC and values
/// without one are assumed to already be UTC, so in-memory kinds are
/// normalized the same way as on write.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return default;

            case JsonTokenType.Number:
                if (!reader.TryGetInt64(out var ticks))
                {
                    throw new JsonException("Unable to convert numeric value to DateTime.");
                }
                return new DateTime(ticks, DateTimeKind.Utc);

            case JsonTokenType.String:
                var value = reader.GetString();
                if (string.IsNullOrEmpty(value))
                {
                    throw new JsonException("Unable to convert empty string to DateTime.");
                }
                if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    throw new JsonException($"Unable to convert string '{value}' to DateTime.");
                }
                return parsed.Kind == DateTimeKind.Local
                    ? parsed.ToUniversalTime()
                    : DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

            default:
                throw new JsonException($"Cannot convert {reader.TokenType} to DateTime.");
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => value
        };

        // "O" is the round-trippable format; for UTC it always ends in 'Z'
        // and keeps full 100ns precision (matching what SQLite stores).
        writer.WriteStringValue(utc.ToString("O", CultureInfo.InvariantCulture));
    }
}
