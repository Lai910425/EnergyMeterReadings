namespace EnergyMeter.Api.Domain;

/// <summary>Minimal reading shape the calculator needs. Keeps the calculation independent of EF entities.</summary>
public readonly record struct ReadingPoint(DateTime TimestampUtc, decimal ValueKwh);

public sealed record DailyConsumption(DateOnly DateUtc, decimal ConsumptionKwh);

public sealed record ConsumptionSummary(
    int ReadingCount,
    decimal TotalConsumptionKwh,
    bool DecreaseDetected,
    IReadOnlyList<DailyConsumption> Daily);

/// <summary>
/// Pure, side-effect-free consumption calculation. No database, no clock, no I/O,
/// so it can be unit tested exhaustively.
/// </summary>
public static class ConsumptionCalculator
{
    /// <summary>
    /// Rules (from the brief):
    /// <list type="bullet">
    /// <item>Only readings with fromUtc &lt;= timestamp &lt;= toUtc are used (inclusive range).</item>
    /// <item>Total = latest reading - earliest reading in range; 0 if fewer than two readings.</item>
    /// <item>Daily: consecutive pairs, difference attributed to the UTC date of the later reading;
    ///       only positive differences are summed.</item>
    /// </list>
    /// Meter reset handling: a negative difference (the register went down) is treated as a reset
    /// and contributes nothing. When that happens, "latest - earliest" would be misleading (it can be
    /// negative), so the total becomes the sum of positive differences instead, which keeps
    /// total == sum(daily). For normal, non-decreasing data both formulas give the same number.
    /// </summary>
    public static ConsumptionSummary Calculate(IEnumerable<ReadingPoint> readings, DateTime fromUtc, DateTime toUtc)
    {
        ArgumentNullException.ThrowIfNull(readings);
        if (fromUtc > toUtc)
        {
            throw new ArgumentException("fromUtc must be earlier than or equal to toUtc.", nameof(fromUtc));
        }

        var ordered = readings
            .Where(r => r.TimestampUtc >= fromUtc && r.TimestampUtc <= toUtc)
            .OrderBy(r => r.TimestampUtc)
            .ToList();

        if (ordered.Count < 2)
        {
            return new ConsumptionSummary(ordered.Count, 0m, false, Array.Empty<DailyConsumption>());
        }

        var daily = new SortedDictionary<DateOnly, decimal>();
        var sumOfPositiveDeltas = 0m;
        var decreaseDetected = false;

        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];
            var delta = current.ValueKwh - previous.ValueKwh;
            var day = DateOnly.FromDateTime(current.TimestampUtc);

            // The day has a pair attributed to it, so it appears in the breakdown even if its sum is 0.
            daily.TryAdd(day, 0m);

            if (delta > 0)
            {
                daily[day] += delta;
                sumOfPositiveDeltas += delta;
            }
            else if (delta < 0)
            {
                decreaseDetected = true;
            }
        }

        var total = decreaseDetected
            ? sumOfPositiveDeltas
            : ordered[^1].ValueKwh - ordered[0].ValueKwh;

        return new ConsumptionSummary(
            ordered.Count,
            total,
            decreaseDetected,
            daily.Select(kv => new DailyConsumption(kv.Key, kv.Value)).ToList());
    }
}
