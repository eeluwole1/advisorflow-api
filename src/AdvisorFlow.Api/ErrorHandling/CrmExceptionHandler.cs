using AdvisorFlow.Application.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AdvisorFlow.Api.ErrorHandling;

/// <summary>
/// Turns CRM integration failures into 502/503 problem responses (blueprint §11.7).
/// The response never includes upstream error details, which may contain record data.
/// </summary>
public sealed class CrmExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<CrmExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            CrmUnavailableException => (StatusCodes.Status503ServiceUnavailable, "The CRM service is unavailable."),
            CrmUpstreamException => (StatusCodes.Status502BadGateway, "The CRM service returned an error."),
            _ => (0, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        logger.LogWarning(exception, "CRM integration failure on {Path}", context.Request.Path);
        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title },
        });
    }
}
