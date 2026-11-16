using AdvisorFlow.Application.Households;
using AdvisorFlow.Application.Opportunities;
using AdvisorFlow.Domain.Opportunities;

namespace AdvisorFlow.Infrastructure.Crm;

/// <summary>
/// Fictional advisory opportunities (M4). Ids imitate the Salesforce Opportunity key prefix (006).
/// Probability follows the stage, as Salesforce sets it. Amounts are illustrative advisory fees,
/// not assets under management (BR-OP-003).
/// </summary>
public sealed partial class MockCrmDataProvider
{
    private static readonly IReadOnlyList<Opportunity> SeedOpportunities =
    [
        Seed(1, "001MOCK00000000001", "Williams Household - Retirement Planning", OpportunityStages.Qualification,
            new DateOnly(2026, 12, 15), 4500m, "Retirement Planning", "High", "Book a discovery meeting", null),
        Seed(2, "001MOCK00000000002", "Okafor Household - Investment Advisory", OpportunityStages.ProposalPresented,
            new DateOnly(2026, 12, 1), 12000m, "Investment Advisory", "Medium", "Follow up on the proposal", null),
        Seed(3, "001MOCK00000000002", "Okafor Household - Annual Financial Review", OpportunityStages.ClosedWon,
            new DateOnly(2026, 10, 20), 2500m, "Financial Review", "Low", null, null),
        Seed(4, "001MOCK00000000003", "Tremblay Household - Retirement Planning", OpportunityStages.Prospecting,
            new DateOnly(2027, 1, 31), null, "Retirement Planning", null, null, null),
        Seed(5, "001MOCK00000000004", "Nguyen Household - Investment Advisory", OpportunityStages.NegotiationReview,
            new DateOnly(2026, 11, 30), 8000m, "Investment Advisory", "High", "Confirm the fee schedule", null),
        Seed(6, "001MOCK00000000005", "Fischer Household - Financial Review", OpportunityStages.ClosedLost,
            new DateOnly(2026, 9, 15), 3000m, "Financial Review", "Low", null, "Timing"),
        Seed(7, "001MOCK00000000006", "Singh Household - Retirement Planning", OpportunityStages.NeedsAssessment,
            new DateOnly(2027, 2, 15), 6000m, "Retirement Planning", "Medium", "Collect pension statements", null),
        // Martin Household intentionally has no opportunities (empty-state case).
    ];

    private readonly List<Opportunity> _opportunities = [.. SeedOpportunities];
    private int _nextOpportunity = SeedOpportunities.Count + 1;

    private List<Opportunity> Opportunities
    {
        get { lock (_lock) { return [.. _opportunities.Select(WithCurrentHouseholdName)]; } }
    }

    public Task<PagedResult<Opportunity>> GetOpportunitiesAsync(OpportunityQuery query, CancellationToken ct)
    {
        IEnumerable<Opportunity> filtered = Opportunities;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            filtered = filtered.Where(o => o.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Stage))
        {
            filtered = filtered.Where(o => o.Stage == query.Stage);
        }

        if (!string.IsNullOrWhiteSpace(query.HouseholdId))
        {
            filtered = filtered.Where(o => o.HouseholdId == query.HouseholdId);
        }

        var ordered = Ordered(filtered).ToList();
        var page = ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        return Task.FromResult(new PagedResult<Opportunity>(page, query.Page, query.PageSize, ordered.Count));
    }

    public Task<Opportunity?> GetOpportunityAsync(string id, CancellationToken ct) =>
        Task.FromResult(Opportunities.FirstOrDefault(o => o.Id == id));

    public Task<IReadOnlyList<Opportunity>?> GetHouseholdOpportunitiesAsync(string householdId, CancellationToken ct)
    {
        if (Households.All(h => h.Id != householdId))
        {
            return Task.FromResult<IReadOnlyList<Opportunity>?>(null);
        }

        IReadOnlyList<Opportunity> list = Ordered(Opportunities.Where(o => o.HouseholdId == householdId)).ToList();
        return Task.FromResult<IReadOnlyList<Opportunity>?>(list);
    }

    public Task<Opportunity> CreateOpportunityAsync(string householdId, OpportunityDraft draft, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_households.All(h => h.Id != householdId))
            {
                throw new InvalidOperationException($"Household {householdId} does not exist.");
            }

            // 006MOCK + 11 digits = 18 characters, like a Salesforce Opportunity id.
            var opportunity = Apply(
                Seed(_nextOpportunity++, householdId, draft.Name, draft.Stage, draft.CloseDate, null, null, null, null, null),
                draft);
            _opportunities.Add(opportunity);
            return Task.FromResult(WithCurrentHouseholdName(opportunity));
        }
    }

    public Task<Opportunity?> UpdateOpportunityAsync(string id, OpportunityDraft draft, CancellationToken ct)
    {
        lock (_lock)
        {
            var index = _opportunities.FindIndex(o => o.Id == id);
            if (index < 0)
            {
                return Task.FromResult<Opportunity?>(null);
            }

            // Id and household are not editable through the API.
            var updated = Apply(_opportunities[index], draft);
            _opportunities[index] = updated;
            return Task.FromResult<Opportunity?>(WithCurrentHouseholdName(updated));
        }
    }

    private static Opportunity Apply(Opportunity o, OpportunityDraft d) => o with
    {
        Name = d.Name,
        Stage = d.Stage,
        CloseDate = d.CloseDate,
        Amount = d.Amount,
        Probability = OpportunityStages.DefaultProbability[d.Stage],
        ServiceType = d.ServiceType,
        EngagementPriority = d.EngagementPriority,
        NextStep = d.NextStep,
        LossReason = d.LossReason,
    };

    private static IEnumerable<Opportunity> Ordered(IEnumerable<Opportunity> opportunities) => opportunities
        .OrderBy(o => o.CloseDate)
        .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(o => o.Id, StringComparer.Ordinal);

    /// <summary>The household name is looked up on read, as Salesforce does through Account.Name.</summary>
    private Opportunity WithCurrentHouseholdName(Opportunity o) =>
        o with { HouseholdName = _households.FirstOrDefault(h => h.Id == o.HouseholdId)?.Name ?? string.Empty };

    private static Opportunity Seed(
        int number, string householdId, string name, string stage, DateOnly closeDate, decimal? amount,
        string? serviceType, string? priority, string? nextStep, string? lossReason) => new(
        $"006MOCK{number:D11}", householdId, HouseholdName: string.Empty, name, stage, closeDate, amount,
        OpportunityStages.DefaultProbability[stage], serviceType, priority, nextStep, lossReason);
}
