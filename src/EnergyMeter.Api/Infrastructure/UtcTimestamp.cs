using System.Globalization;

namespace EnergyMeter.Api.Infrastructure;

/// <summary>
/// One place that defines how timestamps enter and leave the API.
/// Inbound: ISO 8601 with an explicit UTC designator ("Z") or offset ("+02:00"); converted to UTC.
/// Values without any zone information are rejected rather than guessed.
/// Outbound: ISO 8601 UTC with a trailing "Z", e.g. 2026-11-01T08:00:00Z.
/// </summary>
public static class UtcTimestamp
{
    // 'FFFFFFF' drops trailing zeros (and the dot when the fraction is zero).
    private const string OutputFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public const string ExpectedFormatMessage =
        "must be an ISO 8601 timestamp with a UTC designator or offset, e.g. 2026-11-01T08:00:00Z";

    public static bool TryParse(string? value, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // RoundtripKind tells us whether the text carried zone information:
        // Unspecified = no "Z" and no offset, which we refuse to interpret.
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            || parsed.Kind == DateTimeKind.Unspecified)
        {
            return false;
        }

        // DateTimeOffset gives an exact conversion to UTC without depending on the server's local time zone.
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offsetValue))
        {
            return false;
        }

        utc = offsetValue.UtcDateTime;
        return true;
    }

    public static string Format(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value
        };
        return utc.ToString(OutputFormat, CultureInfo.InvariantCulture);
    }
}
