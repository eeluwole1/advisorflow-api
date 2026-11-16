using AdvisorFlow.Api.Features;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Application.Opportunities;
using Microsoft.AspNetCore.Mvc;

namespace AdvisorFlow.Api.Controllers;

/// <summary>
/// Advisory opportunity endpoints (M4, contract v1.3): the pipeline, a household's opportunities,
/// and create/update with the stage rule BR-OP-004 (ADR-010). There is no delete (ADR-008):
/// an opportunity ends as Closed Won or Closed Lost.
/// </summary>
[ApiController]
[Route("api/opportunities")]
public sealed class OpportunitiesController(OpportunityService opportunities) : ControllerBase
{
    /// <summary>The pipeline: paginated opportunities ordered by close date, optionally filtered.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<OpportunityDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = OpportunityService.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] string? stage = null,
        [FromQuery] string? householdId = null,
        CancellationToken ct = default)
    {
        var errors = OpportunityService.ValidateQuery(page, pageSize, search, stage, householdId);
        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        var query = new OpportunityQuery(page, pageSize, search, stage, string.IsNullOrWhiteSpace(householdId) ? null : householdId);
        return Ok(await opportunities.GetOpportunitiesAsync(query, ct));
    }

    /// <summary>A single opportunity.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<OpportunityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        if (!HouseholdService.IsValidRecordId(id))
        {
            return InvalidId();
        }

        var opportunity = await opportunities.GetOpportunityAsync(id, ct);
        return opportunity is null ? OpportunityNotFound() : Ok(opportunity);
    }

    /// <summary>A household's opportunities, ordered by close date.</summary>
    [HttpGet("~/api/households/{id}/opportunities")]
    [ProducesResponseType<IReadOnlyList<OpportunityDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForHousehold(string id, CancellationToken ct)
    {
        if (!HouseholdService.IsValidRecordId(id))
        {
            return InvalidId();
        }

        var list = await opportunities.GetHouseholdOpportunitiesAsync(id, ct);
        return list is null ? HouseholdNotFound() : Ok(list);
    }

    /// <summary>Creates an opportunity for a household. Stage defaults to Prospecting (BR-OP-001, BR-OP-002).</summary>
    [HttpPost("~/api/households/{id}/opportunities")]
    [ServiceFilter<WritesEnabledFilter>]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<OpportunityDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(string id, [FromBody] OpportunityWriteRequest? request, CancellationToken ct)
    {
        if (!HouseholdService.IsValidRecordId(id))
        {
            return InvalidId();
        }

        var errors = OpportunityService.ValidateOpportunity(request, isCreate: true, out var draft);
        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        var result = await opportunities.CreateOpportunityAsync(id, draft!, ct);
        return result.Status == OpportunityWriteStatus.Ok
            ? CreatedAtAction(nameof(Get), new { id = result.Opportunity!.Id }, result.Opportunity)
            : ToProblem(result.Status);
    }

    /// <summary>
    /// Replaces an opportunity's editable fields (empty optional fields are cleared). The stage may
    /// move forward any number of stages or back one; a closed opportunity keeps its stage (BR-OP-004).
    /// </summary>
    [HttpPut("{id}")]
    [ServiceFilter<WritesEnabledFilter>]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<OpportunityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string id, [FromBody] OpportunityWriteRequest? request, CancellationToken ct)
    {
        if (!HouseholdService.IsValidRecordId(id))
        {
            return InvalidId();
        }

        var errors = OpportunityService.ValidateOpportunity(request, isCreate: false, out var draft);
        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        var result = await opportunities.UpdateOpportunityAsync(id, draft!, ct);
        return result.Status == OpportunityWriteStatus.Ok ? Ok(result.Opportunity) : ToProblem(result.Status);
    }

    private IActionResult ToProblem(OpportunityWriteStatus status) => status switch
    {
        OpportunityWriteStatus.HouseholdNotFound => HouseholdNotFound(),
        OpportunityWriteStatus.OpportunityNotFound => OpportunityNotFound(),
        OpportunityWriteStatus.ClosedIsFinal => StageProblem(
            "This opportunity is closed, so its stage can no longer change."),
        OpportunityWriteStatus.TooFarBack => StageProblem(
            "The stage can move forward, or back by one stage only."),
        _ => throw new InvalidOperationException($"Unexpected opportunity write status {status}."),
    };

    private IActionResult StageProblem(string message) => ValidationProblem(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["stage"] = [message] }));

    private IActionResult InvalidId() => ValidationProblem(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["id"] = ["Id must be a 15- or 18-character Salesforce record id."] }));

    private IActionResult OpportunityNotFound() =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Opportunity not found.");

    private IActionResult HouseholdNotFound() =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Household not found.");
}
