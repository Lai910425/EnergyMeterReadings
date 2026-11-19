using EnergyMeter.Api.Infrastructure;

namespace EnergyMeter.Api.Domain;

/// <summary>Thrown when a requested resource does not exist. Mapped to HTTP 404.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string resource, object id)
        : base($"{resource} with id '{id}' was not found.")
    {
        Resource = resource;
        ResourceId = id;
    }

    public string Resource { get; }
    public object ResourceId { get; }
}

/// <summary>
/// Thrown when a reading with the same timestamp already exists for the meter. Mapped to HTTP 409.
/// </summary>
public sealed class DuplicateReadingException : Exception
{
    public DuplicateReadingException(int meterId, DateTime timestampUtc, Exception? inner = null)
        : base($"Meter {meterId} already has a reading at {UtcTimestamp.Format(timestampUtc)}.", inner)
    {
        MeterId = meterId;
        TimestampUtc = timestampUtc;
    }

    public int MeterId { get; }
    public DateTime TimestampUtc { get; }
}
