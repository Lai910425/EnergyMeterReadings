using EnergyMeter.Api.Contracts;
using EnergyMeter.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnergyMeter.Api.Controllers;

/// <summary>
/// Meters are created and listed in the context of their site (/api/sites/{siteId}/meters),
/// but addressed directly once they exist (/api/meters/{meterId}) so URLs stay short.
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class MetersController : ControllerBase
{
    private readonly IMeterService _meters;

    public MetersController(IMeterService meters) => _meters = meters;

    /// <summary>Create a meter at a site.</summary>
    [HttpPost("api/sites/{siteId:int}/meters")]
    [ProducesResponseType<MeterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MeterResponse>> Create(int siteId, [FromBody] CreateMeterRequest request, CancellationToken ct)
    {
        var meter = await _meters.CreateAsync(siteId, request.SerialNumber!, ct);
        return CreatedAtAction(nameof(Get), new { meterId = meter.Id }, meter);
    }

    /// <summary>List the meters at a site.</summary>
    [HttpGet("api/sites/{siteId:int}/meters")]
    [ProducesResponseType<IReadOnlyList<MeterResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<MeterResponse>>> ListForSite(int siteId, CancellationToken ct)
        => Ok(await _meters.ListForSiteAsync(siteId, ct));

    /// <summary>Get a meter by id.</summary>
    [HttpGet("api/meters/{meterId:int}")]
    [ProducesResponseType<MeterResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MeterResponse>> Get(int meterId, CancellationToken ct)
        => Ok(await _meters.GetAsync(meterId, ct));
}
