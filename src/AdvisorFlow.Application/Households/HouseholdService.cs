using System.Text.RegularExpressions;
using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Domain.Households;

namespace AdvisorFlow.Application.Households;

/// <summary>
/// Application workflow for households: validates requests, calls the provider,
/// and maps domain records to the stable API DTOs.
/// </summary>
public sealed partial class HouseholdService(ICrmDataProvider provider)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 80;

    /// <summary>Returns field errors; an empty dictionary means the query is valid.</summary>
    public static Dictionary<string, string[]> ValidateQuery(int page, int pageSize, string? search, string? status)
    {
        var errors = new Dictionary<string, string[]>();

        if (page < 1)
        {
            errors["page"] = ["Page must be 1 or greater."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."];
        }

        if (search is { Length: > MaxSearchLength })
        {
            errors["search"] = [$"Search must be {MaxSearchLength} characters or fewer."];
        }

        // BR-CL-001: only approved Client Status values are accepted.
        if (!string.IsNullOrWhiteSpace(status) && !ClientStatuses.All.Contains(status))
        {
            errors["status"] = [$"Status must be one of: {string.Join(", ", ClientStatuses.All)}."];
        }

        return errors;
    }

    /// <summary>
    /// Salesforce record ids are 15 or 18 alphanumeric characters. Rejecting anything else
    /// up front keeps malformed input out of SOQL queries once the Salesforce provider exists.
    /// </summary>
    public static bool IsValidRecordId(string? id) => id is not null && RecordIdPattern().IsMatch(id);

    public async Task<PagedResponse<HouseholdSummaryDto>> GetHouseholdsAsync(HouseholdQuery query, CancellationToken ct)
    {
        var page = await provider.GetHouseholdsAsync(query, ct);
        var ids = page.Items.Select(h => h.Id).ToList();
        var membersById = ids.Count == 0
            ? new Dictionary<string, IReadOnlyList<HouseholdMember>>()
            : await provider.GetMembersForHouseholdsAsync(ids, ct);

        var items = page.Items.Select(h =>
        {
            var members = membersById.TryGetValue(h.Id, out var list) ? list : [];
            var primary = members.FirstOrDefault(m => m.IsPrimary);
            return new HouseholdSummaryDto(
                h.Id, h.Name, h.ClientStatus, h.RiskProfile, h.Priority,
                members.Count,
                primary is null ? null : primary.FullName);
        }).ToList();

        return new PagedResponse<HouseholdSummaryDto>(items, page.Page, page.PageSize, page.TotalCount, page.TotalPages);
    }

    public async Task<HouseholdDetailDto?> GetHouseholdAsync(string id, CancellationToken ct)
    {
        var household = await provider.GetHouseholdAsync(id, ct);
        if (household is null)
        {
            return null;
        }

        var members = await provider.GetMembersAsync(id, ct) ?? [];
        return new HouseholdDetailDto(
            household.Id, household.Name, household.ClientStatus, household.RiskProfile,
            household.Priority, household.Phone, household.AccountNumber, members.Count);
    }

    public async Task<IReadOnlyList<HouseholdMemberDto>?> GetMembersAsync(string householdId, CancellationToken ct)
    {
        var members = await provider.GetMembersAsync(householdId, ct);
        return members?.Select(m => new HouseholdMemberDto(
            m.Id, m.HouseholdId, m.FirstName, m.LastName, m.FullName,
            m.Email, m.Phone, m.PreferredContactMethod, m.IsPrimary)).ToList();
    }

    [GeneratedRegex("^[a-zA-Z0-9]{15}([a-zA-Z0-9]{3})?$")]
    private static partial Regex RecordIdPattern();
}
