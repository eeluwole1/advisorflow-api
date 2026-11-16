namespace AdvisorFlow.Application.Activities;

/// <summary>
/// Filter and paging options for the task list. <see cref="State"/> is one of
/// <see cref="Domain.Activities.ActivityStates"/>; <see cref="Today"/> is the practice's date,
/// used for "overdue".
/// </summary>
public sealed record ActivityQuery(int Page, int PageSize, string? Search, string State, string? HouseholdId, DateOnly Today);

// ---------------------------------------------------------------------------
// API contract DTOs, v1.4 (docs/api/openapi.v1.yaml). Changes must stay
// backward compatible within v1: add optional fields only, never rename or remove.
// ---------------------------------------------------------------------------

/// <summary>
/// A task as the API returns it. <see cref="DueDate"/> is an ISO date (yyyy-MM-dd) or null.
/// <see cref="IsOverdue"/> is computed from the practice's date (BR-ACT-003).
/// <see cref="RelatedToType"/> is "Household" or "Opportunity".
/// </summary>
public sealed record ActivityDto(
    string Id,
    string Subject,
    string Status,
    string? Priority,
    DateOnly? DueDate,
    bool IsClosed,
    bool IsOverdue,
    string HouseholdId,
    string HouseholdName,
    string? RelatedToId,
    string? RelatedToName,
    string? RelatedToType,
    string? ContactName,
    string? OwnerName,
    string? Automation);
