namespace AdvisorFlow.Application.Households;

/// <summary>Filter and paging options for a household list request.</summary>
public sealed record HouseholdQuery(int Page, int PageSize, string? Search, string? Status);

/// <summary>One page of results from a provider.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

// ---------------------------------------------------------------------------
// API contract DTOs, v1 (docs/api/openapi.v1.yaml). Changes must stay
// backward compatible within v1: add optional fields only, never rename or remove.
// ---------------------------------------------------------------------------

public sealed record HouseholdSummaryDto(
    string Id,
    string Name,
    string ClientStatus,
    string? RiskProfile,
    string? Priority,
    int MemberCount,
    string? PrimaryContactName);

public sealed record HouseholdDetailDto(
    string Id,
    string Name,
    string ClientStatus,
    string? RiskProfile,
    string? Priority,
    string? Phone,
    string? AccountNumber,
    int MemberCount);

public sealed record HouseholdMemberDto(
    string Id,
    string HouseholdId,
    string FirstName,
    string LastName,
    string FullName,
    string? Email,
    string? Phone,
    string? PreferredContactMethod,
    bool IsPrimary);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
