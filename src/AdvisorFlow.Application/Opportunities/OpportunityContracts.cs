namespace AdvisorFlow.Application.Opportunities;

/// <summary>Filter and paging options for the opportunity pipeline.</summary>
public sealed record OpportunityQuery(int Page, int PageSize, string? Search, string? Stage, string? HouseholdId);

// ---------------------------------------------------------------------------
// API contract DTOs, v1.3 (docs/api/openapi.v1.yaml). Changes must stay
// backward compatible within v1: add optional fields only, never rename or remove.
// ---------------------------------------------------------------------------

/// <summary>
/// An opportunity as the API returns it. <see cref="CloseDate"/> is an ISO date (yyyy-MM-dd).
/// <see cref="Amount"/> is estimated advisory service revenue, not assets under management (BR-OP-003).
/// </summary>
public sealed record OpportunityDto(
    string Id,
    string HouseholdId,
    string HouseholdName,
    string Name,
    string Stage,
    bool IsClosed,
    DateOnly CloseDate,
    decimal? Amount,
    int? Probability,
    string? ServiceType,
    string? EngagementPriority,
    string? NextStep,
    string? LossReason);

/// <summary>
/// Body of <c>POST /api/households/{id}/opportunities</c> and <c>PUT /api/opportunities/{id}</c>
/// (contract v1.3). PUT replaces every editable field: an omitted or empty optional field is cleared.
/// <see cref="CloseDate"/> is an ISO date (yyyy-MM-dd).
/// </summary>
public sealed record OpportunityWriteRequest(
    string? Name,
    string? Stage,
    string? CloseDate,
    decimal? Amount,
    string? ServiceType,
    string? EngagementPriority,
    string? NextStep,
    string? LossReason);

/// <summary>Outcome of an opportunity create/update that is not a validation error.</summary>
public enum OpportunityWriteStatus
{
    Ok,
    HouseholdNotFound,
    OpportunityNotFound,

    /// <summary>The opportunity is closed and the request changes its stage (BR-OP-004).</summary>
    ClosedIsFinal,

    /// <summary>The request moves the stage back more than one step (BR-OP-004).</summary>
    TooFarBack,
}

public sealed record OpportunityWriteResult(OpportunityWriteStatus Status, OpportunityDto? Opportunity = null);
