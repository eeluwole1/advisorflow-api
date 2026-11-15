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

    /// <summary>Salesforce Account Name and Phone field lengths.</summary>
    public const int MaxNameLength = 255;
    public const int MaxPhoneLength = 40;

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
    /// Validates a create (<paramref name="isCreate"/>) or update request and, when it is valid,
    /// returns the cleaned values as a <see cref="HouseholdDraft"/>: text trimmed, empty optional
    /// fields cleared, picklist values in their exact spelling. On create a missing Client Status
    /// defaults to Prospect (the picklist default); on update it is required, because PUT
    /// replaces every editable field.
    /// </summary>
    public static Dictionary<string, string[]> ValidateHousehold(
        HouseholdWriteRequest? request, bool isCreate, out HouseholdDraft? draft)
    {
        draft = null;
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["body"] = ["A JSON body with the household fields is required."];
            return errors;
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            errors["name"] = ["Name is required."];
        }
        else if (name.Length > MaxNameLength)
        {
            errors["name"] = [$"Name must be {MaxNameLength} characters or fewer."];
        }

        string? status;
        if (string.IsNullOrWhiteSpace(request.ClientStatus))
        {
            status = isCreate ? ClientStatuses.Prospect : null;
            if (status is null)
            {
                errors["clientStatus"] = ["Client status is required."];
            }
        }
        else
        {
            // BR-CL-001: only approved Client Status values are accepted.
            status = Canonical(ClientStatuses.All, request.ClientStatus, "clientStatus", errors);
        }

        var risk = Canonical(RiskProfiles.All, request.RiskProfile, "riskProfile", errors);
        var priority = Canonical(Priorities.All, request.Priority, "priority", errors);

        var phone = Optional(request.Phone);
        if (phone is { Length: > MaxPhoneLength })
        {
            errors["phone"] = [$"Phone must be {MaxPhoneLength} characters or fewer."];
        }

        if (errors.Count == 0)
        {
            draft = new HouseholdDraft(name!, status!, risk, priority, phone);
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
        return ToDetail(household, members.Count);
    }

    public async Task<IReadOnlyList<HouseholdMemberDto>?> GetMembersAsync(string householdId, CancellationToken ct)
    {
        var members = await provider.GetMembersAsync(householdId, ct);
        return members?.Select(m => new HouseholdMemberDto(
            m.Id, m.HouseholdId, m.FirstName, m.LastName, m.FullName,
            m.Email, m.Phone, m.PreferredContactMethod, m.IsPrimary)).ToList();
    }

    /// <summary>Creates a household from a validated draft.</summary>
    public async Task<HouseholdDetailDto> CreateHouseholdAsync(HouseholdDraft draft, CancellationToken ct)
    {
        var household = await provider.CreateHouseholdAsync(draft, ct);
        return ToDetail(household, memberCount: 0);
    }

    /// <summary>Replaces a household's editable fields. Returns null when it does not exist.</summary>
    public async Task<HouseholdDetailDto?> UpdateHouseholdAsync(string id, HouseholdDraft draft, CancellationToken ct)
    {
        var household = await provider.UpdateHouseholdAsync(id, draft, ct);
        if (household is null)
        {
            return null;
        }

        var members = await provider.GetMembersAsync(household.Id, ct) ?? [];
        return ToDetail(household, members.Count);
    }

    private static HouseholdDetailDto ToDetail(Household h, int memberCount) => new(
        h.Id, h.Name, h.ClientStatus, h.RiskProfile, h.Priority, h.Phone, h.AccountNumber, memberCount);

    /// <summary>Trimmed text, or null when empty (PUT clears empty optional fields).</summary>
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The picklist's exact spelling of <paramref name="value"/>, null when empty,
    /// or an error when the value is not approved.</summary>
    private static string? Canonical(
        IReadOnlySet<string> allowed, string? value, string field, Dictionary<string, string[]> errors)
    {
        var trimmed = Optional(value);
        if (trimmed is null)
        {
            return null;
        }

        var match = allowed.FirstOrDefault(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            errors[field] = [$"Must be one of: {string.Join(", ", allowed)}."];
        }

        return match;
    }

    [GeneratedRegex("^[a-zA-Z0-9]{15}([a-zA-Z0-9]{3})?$")]
    private static partial Regex RecordIdPattern();
}
