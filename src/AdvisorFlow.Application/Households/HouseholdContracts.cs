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

/// <summary>
/// Body of <c>POST /api/households</c> and <c>PUT /api/households/{id}</c> (contract v1.1).
/// PUT replaces every editable field: an omitted or empty optional field is cleared.
/// </summary>
public sealed record HouseholdWriteRequest(
    string? Name,
    string? ClientStatus,
    string? RiskProfile,
    string? Priority,
    string? Phone);

/// <summary>
/// Body of <c>POST /api/households/{id}/contacts</c> and <c>PUT /api/households/{id}/contacts/{contactId}</c>
/// (contract v1.2). PUT replaces every editable field: an omitted or empty optional field is cleared.
/// </summary>
public sealed record MemberWriteRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? PreferredContactMethod,
    bool? IsPrimary);

/// <summary>Outcome of a member create/update that is not a validation error.</summary>
public enum MemberWriteStatus
{
    Ok,
    HouseholdNotFound,
    MemberNotFound,

    /// <summary>The request un-sets the current primary member (BR-HH-003).</summary>
    CannotUnsetPrimary,
}

public sealed record MemberWriteResult(MemberWriteStatus Status, HouseholdMemberDto? Member = null);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
