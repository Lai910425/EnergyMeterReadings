using System.Text.Json;
using EnergyMeter.Api.Infrastructure;

namespace EnergyMeter.Tests;

public class UtcTimestampTests
{
    private static readonly DateTime Expected = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("2026-11-01T08:00:00Z")]
    [InlineData("2026-11-01T10:00:00+02:00")]  // same instant, Johannesburg offset
    [InlineData("2026-11-01T03:00:00-05:00")]
    public void TimestampsWithZoneInformation_AreNormalisedToUtc(string input)
    {
        Assert.True(UtcTimestamp.TryParse(input, out var utc));
        Assert.Equal(Expected, utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Theory]
    [InlineData("2026-11-01T08:00:00")]  // no zone: ambiguous, rejected
    [InlineData("not a date")]
    [InlineData("")]
    [InlineData(null)]
    public void TimestampsWithoutZoneOrInvalid_AreRejected(string? input)
    {
        Assert.False(UtcTimestamp.TryParse(input, out _));
    }

    [Fact]
    public void Format_WritesIso8601WithZ()
    {
        Assert.Equal("2026-11-01T08:00:00Z", UtcTimestamp.Format(Expected));
        Assert.Equal("2026-11-01T08:00:00.5Z", UtcTimestamp.Format(Expected.AddMilliseconds(500)));
    }

    [Fact]
    public void JsonConverter_RoundTripsAndRejectsMissingZone()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new UtcDateTimeJsonConverter());

        Assert.Equal("\"2026-11-01T08:00:00Z\"", JsonSerializer.Serialize(Expected, options));
        Assert.Equal(Expected, JsonSerializer.Deserialize<DateTime>("\"2026-11-01T10:00:00+02:00\"", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>("\"2026-11-01T08:00:00\"", options));
    }
}
