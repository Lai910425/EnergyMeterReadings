using EnergyMeter.Api.Domain;

namespace EnergyMeter.Tests;

public class ConsumptionCalculatorTests
{
    private static DateTime Utc(int day, int hour, int minute = 0) =>
        new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    private static ReadingPoint R(DateTime ts, decimal kwh) => new(ts, kwh);

    private static readonly DateTime From = Utc(1, 0);
    private static readonly DateTime To = Utc(30, 23, 59);

    [Fact]
    public void MultipleReadings_AcrossThreeDays_ProducesTotalAndDailyBreakdown()
    {
        var readings = new[]
        {
            R(Utc(1, 8), 1000.0m),
            R(Utc(1, 20), 1010.5m),  // +10.5 -> Nov 1
            R(Utc(2, 6), 1015.5m),   // +5.0  -> Nov 2
            R(Utc(2, 18), 1030.0m),  // +14.5 -> Nov 2
            R(Utc(3, 0), 1032.0m),   // +2.0  -> Nov 3
        };

        var result = ConsumptionCalculator.Calculate(readings, From, To);

        Assert.Equal(5, result.ReadingCount);
        Assert.Equal(32.0m, result.TotalConsumptionKwh);
        Assert.False(result.DecreaseDetected);
        Assert.Collection(result.Daily,
            d => { Assert.Equal(new DateOnly(2026, 11, 1), d.DateUtc); Assert.Equal(10.5m, d.ConsumptionKwh); },
            d => { Assert.Equal(new DateOnly(2026, 11, 2), d.DateUtc); Assert.Equal(19.5m, d.ConsumptionKwh); },
            d => { Assert.Equal(new DateOnly(2026, 11, 3), d.DateUtc); Assert.Equal(2.0m, d.ConsumptionKwh); });

        // With normal (increasing) data, the daily totals add up to the total.
        Assert.Equal(result.TotalConsumptionKwh, result.Daily.Sum(d => d.ConsumptionKwh));
    }

    [Fact]
    public void PairSpanningMidnight_IsAttributedToTheDateOfTheLaterReading()
    {
        var readings = new[]
        {
            R(Utc(1, 23), 100m),
            R(Utc(2, 1), 110m),
        };

        var result = ConsumptionCalculator.Calculate(readings, From, To);

        var day = Assert.Single(result.Daily);
        Assert.Equal(new DateOnly(2026, 11, 2), day.DateUtc);
        Assert.Equal(10m, day.ConsumptionKwh);
        Assert.Equal(10m, result.TotalConsumptionKwh);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FewerThanTwoReadingsInRange_ReturnsZero(int count)
    {
        var readings = Enumerable.Range(0, count).Select(i => R(Utc(5, 12 + i), 500m));

        var result = ConsumptionCalculator.Calculate(readings, From, To);

        Assert.Equal(count, result.ReadingCount);
        Assert.Equal(0m, result.TotalConsumptionKwh);
        Assert.Empty(result.Daily);
    }

    [Fact]
    public void RangeIsInclusive_ReadingsExactlyOnBoundariesAreUsed_OutsideAreIgnored()
    {
        var from = Utc(10, 8);
        var to = Utc(11, 8);
        var readings = new[]
        {
            R(from.AddTicks(-1), 1m),  // just before range: ignored
            R(from, 100m),             // exactly fromUtc: included
            R(Utc(10, 20), 120m),
            R(to, 150m),               // exactly toUtc: included
            R(to.AddTicks(1), 9999m),  // just after range: ignored
        };

        var result = ConsumptionCalculator.Calculate(readings, from, to);

        Assert.Equal(3, result.ReadingCount);
        Assert.Equal(50m, result.TotalConsumptionKwh);
        Assert.Collection(result.Daily,
            d => { Assert.Equal(new DateOnly(2026, 11, 10), d.DateUtc); Assert.Equal(20m, d.ConsumptionKwh); },
            d => { Assert.Equal(new DateOnly(2026, 11, 11), d.DateUtc); Assert.Equal(30m, d.ConsumptionKwh); });
    }

    [Fact]
    public void UnorderedInput_IsOrderedByTimestampBeforeCalculating()
    {
        var readings = new[]
        {
            R(Utc(2, 12), 130m),
            R(Utc(1, 12), 100m),
            R(Utc(1, 18), 110m),
        };

        var result = ConsumptionCalculator.Calculate(readings, From, To);

        Assert.Equal(30m, result.TotalConsumptionKwh);
        Assert.Collection(result.Daily,
            d => Assert.Equal(10m, d.ConsumptionKwh),
            d => Assert.Equal(20m, d.ConsumptionKwh));
    }

    [Fact]
    public void DecreasingReading_IsTreatedAsResetAndIgnored()
    {
        var readings = new[]
        {
            R(Utc(1, 0), 100m),
            R(Utc(1, 12), 150m),  // +50 -> Nov 1
            R(Utc(2, 0), 20m),    // -130: meter reset, contributes nothing
            R(Utc(2, 12), 50m),   // +30 -> Nov 2
        };

        var result = ConsumptionCalculator.Calculate(readings, From, To);

        Assert.True(result.DecreaseDetected);
        // latest - earliest would be -50; the total instead counts only real consumption.
        Assert.Equal(80m, result.TotalConsumptionKwh);
        Assert.Collection(result.Daily,
            d => { Assert.Equal(new DateOnly(2026, 11, 1), d.DateUtc); Assert.Equal(50m, d.ConsumptionKwh); },
            d => { Assert.Equal(new DateOnly(2026, 11, 2), d.DateUtc); Assert.Equal(30m, d.ConsumptionKwh); });
        Assert.Equal(result.TotalConsumptionKwh, result.Daily.Sum(d => d.ConsumptionKwh));
    }

    [Fact]
    public void DayWithOnlyAFlatPair_AppearsWithZero()
    {
        var readings = new[]
        {
            R(Utc(1, 12), 100m),
            R(Utc(2, 12), 100m),
        };

        var result = ConsumptionCalculator.Calculate(readings, From, To);

        var day = Assert.Single(result.Daily);
        Assert.Equal(new DateOnly(2026, 11, 2), day.DateUtc);
        Assert.Equal(0m, day.ConsumptionKwh);
        Assert.Equal(0m, result.TotalConsumptionKwh);
    }

    [Fact]
    public void FromAfterTo_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ConsumptionCalculator.Calculate(Array.Empty<ReadingPoint>(), Utc(2, 0), Utc(1, 0)));
    }
}
