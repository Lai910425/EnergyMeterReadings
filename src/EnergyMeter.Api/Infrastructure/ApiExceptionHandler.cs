using EnergyMeter.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EnergyMeter.Api.Infrastructure;

/// <summary>
/// Translates domain exceptions into RFC 7807 problem details. Anything not handled here falls
/// through to the default handler and becomes a generic 500 without leaking internals.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            NotFoundException nf => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = $"{nf.Resource} not found",
                Detail = nf.Message
            },
            DuplicateReadingException dup => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Duplicate reading timestamp",
                Detail = dup.Message,
                Extensions =
                {
                    ["meterId"] = dup.MeterId,
                    ["timestampUtc"] = UtcTimestamp.Format(dup.TimestampUtc)
                }
            },
            _ => null
        };

        if (problem is null)
        {
            return false;
        }

        _logger.LogInformation("Request rejected with {Status}: {Detail}", problem.Status, problem.Detail);

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
