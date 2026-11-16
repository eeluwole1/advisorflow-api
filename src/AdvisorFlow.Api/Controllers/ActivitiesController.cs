using AdvisorFlow.Application.Activities;
using AdvisorFlow.Application.Households;
using Microsoft.AspNetCore.Mvc;

namespace AdvisorFlow.Api.Controllers;

/// <summary>
/// Follow-up task endpoints (M5, contract v1.4): the task list across households, and the tasks of
/// one household or one opportunity. Read-only: tasks are logged in Salesforce or created by the
/// follow-up Flow (BR-ACT-001/002, ADR-011), so these endpoints also work on the read-only deployment.
/// </summary>
[ApiController]
[Route("api/activities")]
public sealed class ActivitiesController(ActivityService activities) : ControllerBase
{
    /// <summary>
    /// Paginated tasks: open (default), overdue, closed or all. Open tasks come first, then by due
    /// date with undated tasks last.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<ActivityDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = HouseholdService.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] string? state = null,
        [FromQuery] string? householdId = null,
        CancellationToken ct = default)
    {
        var errors = ActivityService.ValidateQuery(page, pageSize, search, state, householdId);
        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        return Ok(await activities.GetActivitiesAsync(page, pageSize, search, state, householdId, ct));
    }

    /// <summary>A household's tasks, including those on its opportunities.</summary>
    [HttpGet("~/api/households/{id}/activities")]
    [ProducesResponseType<IReadOnlyList<ActivityDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForHousehold(string id, CancellationToken ct)
    {
        if (!HouseholdService.IsValidRecordId(id))
        {
            return InvalidId();
        }

        var list = await activities.GetHouseholdActivitiesAsync(id, ct);
        return list is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Household not found.")
            : Ok(list);
    }

    /// <summary>An opportunity's tasks.</summary>
    [HttpGet("~/api/opportunities/{id}/activities")]
    [ProducesResponseType<IReadOnlyList<ActivityDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForOpportunity(string id, CancellationToken ct)
    {
        if (!HouseholdService.IsValidRecordId(id))
        {
            return InvalidId();
        }

        var list = await activities.GetOpportunityActivitiesAsync(id, ct);
        return list is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Opportunity not found.")
            : Ok(list);
    }

    private IActionResult InvalidId() => ValidationProblem(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["id"] = ["Id must be a 15- or 18-character Salesforce record id."] }));
}
