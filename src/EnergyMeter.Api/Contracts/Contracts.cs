using System.ComponentModel.DataAnnotations;

namespace EnergyMeter.Api.Contracts;

// ---------- Requests ----------

public sealed class CreateSiteRequest
{
    /// <example>Johannesburg Head Office</example>
    [Required]
    [StringLength(200)]
    public string? Name { get; init; }
}

public sealed class CreateMeterRequest
{
    /// <example>EM-0001</example>
    [Required]
    [StringLength(100)]
    public string? SerialNumber { get; init; }
}

public sealed class CreateReadingRequest : IValidatableObject
{
    /// <summary>ISO 8601 with "Z" or an offset, e.g. 2026-11-01T08:00:00Z.</summary>
    [Required]
    public DateTime? TimestampUtc { get; init; }

    /// <summary>Cumulative register value in kWh, e.g. 12543.7.</summary>
    [Required]
    [Range(typeof(decimal), "0", "999999999999999.999",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "valueKwh must be between 0 and 999999999999999.999.")]
    public decimal? ValueKwh { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // The column is decimal(18,3). Reject extra precision instead of letting SQL Server round it silently.
        if (ValueKwh is { } value && decimal.Round(value, 3) != value)
        {
            yield return new ValidationResult(
                "valueKwh supports at most 3 decimal places.",
                new[] { nameof(ValueKwh) });
        }
    }
}

// ---------- Responses ----------

public sealed record SiteResponse(int Id, string Name, DateTime CreatedUtc);

public sealed record MeterResponse(int Id, int SiteId, string SerialNumber, DateTime CreatedUtc);

public sealed record ReadingResponse(long Id, int MeterId, DateTime TimestampUtc, decimal ValueKwh);

public sealed record DailyConsumptionResponse(DateOnly DateUtc, decimal ConsumptionKwh);

public sealed record ConsumptionSummaryResponse(
    int MeterId,
    DateTime FromUtc,
    DateTime ToUtc,
    int ReadingCount,
    decimal TotalConsumptionKwh,
    bool DecreaseDetected,
    IReadOnlyList<DailyConsumptionResponse> Daily);
