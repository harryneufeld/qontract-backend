using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Qontract.Infrastructure.Data;

/// <summary>
/// PostgreSQL stores <see cref="DateTime"/> as <c>timestamp with time zone</c>
/// and Npgsql rejects values whose Kind is not UTC. Qontract treats every
/// stored timestamp as UTC (SQLite hands them back as Unspecified), so
/// Unspecified values are tagged as UTC on write and reads always come back
/// as UTC.
/// </summary>
public sealed class UtcDateTimeValueConverter() : ValueConverter<DateTime, DateTime>(
    value => ToUtc(value),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
{
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
