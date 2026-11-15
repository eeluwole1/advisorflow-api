using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Households;

namespace AdvisorFlow.Infrastructure.Crm;

/// <summary>
/// In-memory fictional data for local development and tests.
/// All names, phone numbers (555 range) and emails (example.com) are fictional.
/// Ids imitate Salesforce Account (001) and Contact (003) key prefixes so the
/// frontend never relies on a different id shape.
/// Each instance starts from the same seed data and keeps its own changes in memory
/// (lost on restart), so tests and local runs never affect each other.
/// </summary>
public sealed class MockCrmDataProvider : ICrmDataProvider
{
    private static readonly IReadOnlyList<Household> SeedHouseholds =
    [
        new("001MOCK00000000001", "Williams Household", ClientStatuses.Prospect, "Moderate", "Medium", "204-555-0101", "ADV-0001"),
        new("001MOCK00000000002", "Okafor Household", ClientStatuses.Active, "Conservative", "High", "204-555-0102", "ADV-0002"),
        new("001MOCK00000000003", "Tremblay Household", ClientStatuses.Prospect, "Aggressive", "Low", "204-555-0103", "ADV-0003"),
        new("001MOCK00000000004", "Nguyen Household", ClientStatuses.Active, "Moderate", "Medium", "204-555-0104", "ADV-0004"),
        new("001MOCK00000000005", "Fischer Household", ClientStatuses.Inactive, "Conservative", "Low", "204-555-0105", "ADV-0005"),
        new("001MOCK00000000006", "Singh Household", ClientStatuses.Prospect, "Moderate", "High", "204-555-0106", "ADV-0006"),
        new("001MOCK00000000007", "Martin Household", ClientStatuses.Active, null, "Medium", null, "ADV-0007"),
    ];

    private static readonly IReadOnlyList<HouseholdMember> SeedMembers =
    [
        new("003MOCK00000000001", "001MOCK00000000001", "James", "Williams", "james.williams@example.com", "204-555-0101", "Phone", true),
        new("003MOCK00000000002", "001MOCK00000000001", "Sarah", "Williams", "sarah.williams@example.com", "204-555-0111", "Email", false),
        new("003MOCK00000000003", "001MOCK00000000002", "Chinedu", "Okafor", "chinedu.okafor@example.com", "204-555-0102", "Email", true),
        new("003MOCK00000000004", "001MOCK00000000002", "Amara", "Okafor", "amara.okafor@example.com", "204-555-0112", "SMS", false),
        new("003MOCK00000000005", "001MOCK00000000003", "Marc", "Tremblay", "marc.tremblay@example.com", "204-555-0103", "Phone", true),
        new("003MOCK00000000006", "001MOCK00000000004", "Linh", "Nguyen", "linh.nguyen@example.com", "204-555-0104", "Email", true),
        new("003MOCK00000000007", "001MOCK00000000004", "Bao", "Nguyen", "bao.nguyen@example.com", "204-555-0114", "Email", false),
        new("003MOCK00000000008", "001MOCK00000000004", "Mai", "Nguyen", null, null, null, false),
        new("003MOCK00000000009", "001MOCK00000000005", "Greta", "Fischer", "greta.fischer@example.com", "204-555-0105", "Phone", true),
        new("003MOCK00000000010", "001MOCK00000000006", "Harpreet", "Singh", "harpreet.singh@example.com", "204-555-0106", "SMS", true),
        new("003MOCK00000000011", "001MOCK00000000006", "Jaspreet", "Singh", "jaspreet.singh@example.com", "204-555-0116", "Email", false),
        // Martin Household intentionally has no members (empty-state case).
    ];

    private readonly Lock _lock = new();
    private readonly List<Household> _households = [.. SeedHouseholds];
    private readonly List<HouseholdMember> _members = [.. SeedMembers];
    private int _nextHousehold = SeedHouseholds.Count + 1;

    private List<Household> Households
    {
        get { lock (_lock) { return [.. _households]; } }
    }

    private List<HouseholdMember> Members
    {
        get { lock (_lock) { return [.. _members]; } }
    }

    public Task<PagedResult<Household>> GetHouseholdsAsync(HouseholdQuery query, CancellationToken ct)
    {
        IEnumerable<Household> filtered = Households;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            filtered = filtered.Where(h => h.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            filtered = filtered.Where(h => string.Equals(h.ClientStatus, query.Status, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = filtered.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var page = ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult(new PagedResult<Household>(page, query.Page, query.PageSize, ordered.Count));
    }

    public Task<Household?> GetHouseholdAsync(string id, CancellationToken ct) =>
        Task.FromResult(Households.FirstOrDefault(h => h.Id == id));

    public Task<IReadOnlyList<HouseholdMember>?> GetMembersAsync(string householdId, CancellationToken ct)
    {
        if (Households.All(h => h.Id != householdId))
        {
            return Task.FromResult<IReadOnlyList<HouseholdMember>?>(null);
        }

        IReadOnlyList<HouseholdMember> members = Members
            .Where(m => m.HouseholdId == householdId)
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.LastName)
            .ThenBy(m => m.FirstName)
            .ToList();

        return Task.FromResult<IReadOnlyList<HouseholdMember>?>(members);
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<HouseholdMember>>> GetMembersForHouseholdsAsync(
        IReadOnlyCollection<string> householdIds, CancellationToken ct)
    {
        var members = Members;
        IReadOnlyDictionary<string, IReadOnlyList<HouseholdMember>> result = householdIds
            .Distinct()
            .ToDictionary(
                id => id,
                id => (IReadOnlyList<HouseholdMember>)members.Where(m => m.HouseholdId == id).ToList());

        return Task.FromResult(result);
    }

    public Task<Household> CreateHouseholdAsync(HouseholdDraft draft, CancellationToken ct)
    {
        lock (_lock)
        {
            // 001MOCK + 11 digits = 18 characters, like a Salesforce Account id.
            var household = new Household(
                $"001MOCK{_nextHousehold++:D11}", draft.Name, draft.ClientStatus,
                draft.RiskProfile, draft.Priority, draft.Phone, AccountNumber: null);
            _households.Add(household);
            return Task.FromResult(household);
        }
    }

    public Task<Household?> UpdateHouseholdAsync(string id, HouseholdDraft draft, CancellationToken ct)
    {
        lock (_lock)
        {
            var index = _households.FindIndex(h => h.Id == id);
            if (index < 0)
            {
                return Task.FromResult<Household?>(null);
            }

            // Id and Account Number are not editable through the API.
            var updated = _households[index] with
            {
                Name = draft.Name,
                ClientStatus = draft.ClientStatus,
                RiskProfile = draft.RiskProfile,
                Priority = draft.Priority,
                Phone = draft.Phone,
            };
            _households[index] = updated;
            return Task.FromResult<Household?>(updated);
        }
    }
}
