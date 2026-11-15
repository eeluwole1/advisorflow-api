using AdvisorFlow.Application.Households;
using Microsoft.AspNetCore.Mvc;

namespace AdvisorFlow.Api.Controllers;

/// <summary>Household endpoints: reads (M1, blueprint §11.7) and household create/update (M3, contract v1.1).</summary>
[ApiController]
[Route("api/households")]
public sealed class HouseholdsController(HouseholdService households) : ControllerBase
{
    /// <summary>Paginated household summaries, optionally filtered by name and status.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<HouseholdSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = HouseholdService.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var errors = HouseholdService.ValidateQuery(page, pageSize, search, status);
        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        var result = await households.GetHouseholdsAsync(new HouseholdQuery(page, pageSize, search, status), ct);
        return Ok(result);
    }

    /// <summary>A single household.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<HouseholdDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        if (!HouseholdService.IsValidRecordId(id))
        {
            return InvalidId();
        }

        var household = await households.GetHouseholdAsync(id, ct);
        return household is null ? HouseholdNotFound() : Ok(household);
    }

    /// <summary>Members (Contacts) of a household; primary member first.</summary>
    [HttpGet("{id}/contacts")]
    [ProducesResponseType<IReadOnlyList<HouseholdMemberDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Contacts(string id, CancellationToken ct)
    {
        if (!HouseholdService.IsValidRecordId(id))
        {
            return InvalidId();
        }

        var members = await households.GetMembersAsync(id, ct);
        return members is null ? HouseholdNotFound() : Ok(members);
    }

    /// <summary>Creates a household. Client Status defaults to Prospect.</summary>
    [HttpPost]
    [ProducesResponseType<HouseholdDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] HouseholdWriteRequest? request, CancellationToken ct)
    {
        var errors = HouseholdService.ValidateHousehold(request, isCreate: true, out var draft);
        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        var created = await households.CreateHouseholdAsync(draft!, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>
    /// Replaces a household's editable fields (empty optional fields are cleared).
    /// There is no delete: set Client Status to Inactive to deactivate (ADR-008).
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType<HouseholdDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string id, [FromBody] HouseholdWriteRequest? request, CancellationToken ct)
    {
        if (!HouseholdService.IsValidRecordId(id))
        {
            return InvalidId();
        }

        var errors = HouseholdService.ValidateHousehold(request, isCreate: false, out var draft);
        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        var updated = await households.UpdateHouseholdAsync(id, draft!, ct);
        return updated is null ? HouseholdNotFound() : Ok(updated);
    }

    private IActionResult InvalidId() => ValidationProblem(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["id"] = ["Id must be a 15- or 18-character Salesforce record id."] }));

    private IActionResult HouseholdNotFound() =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Household not found.");
}
