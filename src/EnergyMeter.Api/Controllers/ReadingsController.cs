using System.ComponentModel.DataAnnotations;
using EnergyMeter.Api.Contracts;
using EnergyMeter.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnergyMeter.Api.Controllers;

[ApiController]
[Route("api/meters/{meterId:int}")]
[Produces("application/json")]
public sealed class ReadingsController : ControllerBase
{
    private readonly IReadingService _readings;

    public ReadingsController(IReadingService readings) => _readings = readings;

    /// <summary>Record a cumulative kWh reading. Returns 409 if the meter already has a reading at that timestamp.</summary>
    [HttpPost("readings")]
    [ProducesResponseType<ReadingResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReadingResponse>> Create(int meterId, [FromBody] CreateReadingRequest request, CancellationToken ct)
    {
        var reading = await _readings.CreateAsync(meterId, request.TimestampUtc!.Value, request.ValueKwh!.Value, ct);
        return CreatedAtAction(nameof(List), new { meterId }, reading);
    }

    /// <summary>List readings for a meter, oldest first. fromUtc and toUtc are optional and inclusive.</summary>
    [HttpGet("readings")]
    [ProducesResponseType<IReadOnlyList<ReadingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ReadingResponse>>> List(
        int meterId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken ct)
    {
        if (fromUtc is { } f && toUtc is { } t && f > t)
        {
            return RangeError();
        }

        return Ok(await _readings.ListAsync(meterId, fromUtc, toUtc, ct));
    }

    /// <summary>
    /// Consumption summary for [fromUtc, toUtc] (inclusive): total kWh plus a per-UTC-day breakdown.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType<ConsumptionSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConsumptionSummaryResponse>> Summary(
        int meterId,
        [FromQuery, Required] DateTime? fromUtc,
        [FromQuery, Required] DateTime? toUtc,
        CancellationToken ct)
    {
        if (fromUtc!.Value > toUtc!.Value)
        {
            return RangeError();
        }

        return Ok(await _readings.GetSummaryAsync(meterId, fromUtc.Value, toUtc.Value, ct));
    }

    private ActionResult RangeError()
    {
        ModelState.AddModelError("fromUtc", "fromUtc must be earlier than or equal to toUtc.");
        return ValidationProblem(ModelState);
    }
}
