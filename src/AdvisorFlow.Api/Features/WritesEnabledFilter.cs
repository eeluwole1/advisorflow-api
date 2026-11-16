using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace AdvisorFlow.Api.Features;

/// <summary>
/// Guards create/update endpoints. It is a resource filter, so it runs before model binding
/// and validation: with writes off, any write request gets the same 403, whatever its body.
/// </summary>
public sealed class WritesEnabledFilter(IOptionsMonitor<ApiFeatures> features) : IAsyncResourceFilter
{
    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (features.CurrentValue.AllowWrites)
        {
            return next();
        }

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Changes are disabled in this environment.",
            Detail = "This deployment is read-only. Households and members can be changed in a local development environment.",
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" },
        };
        return Task.CompletedTask;
    }
}
