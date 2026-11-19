using EnergyMeter.Api.Contracts;
using EnergyMeter.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnergyMeter.Api.Controllers;

[ApiController]
[Route("api/sites")]
[Produces("application/json")]
public sealed class SitesController : ControllerBase
{
    private readonly ISiteService _sites;

    public SitesController(ISiteService sites) => _sites = sites;

    /// <summary>Create a site.</summary>
    [HttpPost]
    [ProducesResponseType<SiteResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SiteResponse>> Create([FromBody] CreateSiteRequest request, CancellationToken ct)
    {
        var site = await _sites.CreateAsync(request.Name!, ct);
        return CreatedAtAction(nameof(Get), new { siteId = site.Id }, site);
    }

    /// <summary>List all sites.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SiteResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SiteResponse>>> List(CancellationToken ct)
        => Ok(await _sites.ListAsync(ct));

    /// <summary>Get a site by id.</summary>
    [HttpGet("{siteId:int}")]
    [ProducesResponseType<SiteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SiteResponse>> Get(int siteId, CancellationToken ct)
        => Ok(await _sites.GetAsync(siteId, ct));
}
