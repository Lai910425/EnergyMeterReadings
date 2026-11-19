namespace EnergyMeter.Api.Domain;

/// <summary>A physical location (building, plant, campus) that hosts one or more meters.</summary>
public class Site
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }

    public List<Meter> Meters { get; set; } = new();
}

/// <summary>An energy meter installed at a site. Produces cumulative kWh readings.</summary>
public class Meter
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }

    public Site? Site { get; set; }
    public List<Reading> Readings { get; set; } = new();
}

/// <summary>A cumulative kWh register value captured at a UTC instant.</summary>
public class Reading
{
    public long Id { get; set; }
    public int MeterId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public decimal ValueKwh { get; set; }

    public Meter? Meter { get; set; }
}
