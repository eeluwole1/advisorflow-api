using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Application.Activities;
using AdvisorFlow.Domain.Dashboard;
using AdvisorFlow.Domain.Households;
using AdvisorFlow.Domain.Opportunities;

namespace AdvisorFlow.Application.Dashboard;

// ---------------------------------------------------------------------------
// API contract DTOs, v1.5 (docs/api/openapi.v1.yaml, schema DashboardSummary).
// ---------------------------------------------------------------------------

/// <summary>The practice's pipeline, outcomes, follow-ups and households at <see cref="AsOf"/>.</summary>
public sealed record DashboardSummaryDto(
    DateOnly AsOf,
    PipelineSummaryDto Pipeline,
    OutcomeSummaryDto Outcomes,
    ActivitySummaryDto Activities,
    HouseholdSummaryCountsDto Households);

/// <summary>
/// Open advisory opportunities. <see cref="WeightedFees"/> is each stage's fees times its probability
/// (the stage's default, as Salesforce sets it): a forecast, not a promise.
/// </summary>
public sealed record PipelineSummaryDto(int OpenCount, decimal OpenFees, decimal WeightedFees, IReadOnlyList<StageSummaryDto> Stages);

/// <summary>One open stage: every open stage is listed, in pipeline order, even when empty.</summary>
public sealed record StageSummaryDto(string Stage, int Count, decimal Fees, int Probability, decimal WeightedFees);

/// <summary>
/// Closed opportunities. <see cref="WinRate"/> is won / (won + lost), from 0 to 1, or null with
/// nothing closed yet.
/// </summary>
public sealed record OutcomeSummaryDto(
    int WonCount, decimal WonFees, int LostCount, decimal LostFees, decimal? WinRate, IReadOnlyList<CountDto> LossReasons);

public sealed record ActivitySummaryDto(int Open, int Overdue, int DueNext7Days, int Completed, int OpenFromAutomation);

public sealed record HouseholdSummaryCountsDto(int Total, IReadOnlyList<CountDto> ByStatus);

/// <summary>A label and its count; <see cref="Name"/> is "Not set" when the CRM value is empty.</summary>
public sealed record CountDto(string Name, int Count);

/// <summary>
/// Builds the dashboard (M6) from CRM aggregates in one call. Totals must reconcile to the records
/// they summarize (blueprint M6 exit criterion; see the reconciliation tests).
/// </summary>
public sealed class DashboardService(ICrmDataProvider provider, TimeProvider clock)
{
    public const string NotSet = "Not set";

    /// <summary>Client statuses in lifecycle order (BR-CL-001).</summary>
    private static readonly IReadOnlyList<string> StatusOrder = [ClientStatuses.Prospect, ClientStatuses.Active, ClientStatuses.Inactive];

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        var today = ActivityService.TodayFor(clock);
        var data = await provider.GetDashboardDataAsync(today, ct);
        var byStage = data.Stages
            .GroupBy(s => s.Stage)
            .ToDictionary(g => g.Key, g => (Count: g.Sum(s => s.Count), Fees: g.Sum(s => s.Fees)));

        (int Count, decimal Fees) Totals(string stage) => byStage.TryGetValue(stage, out var t) ? t : (0, 0m);

        var openStages = OpportunityStages.Ordered
            .Where(stage => !OpportunityStages.IsClosed(stage))
            .Select(stage =>
            {
                var (count, fees) = Totals(stage);
                var probability = OpportunityStages.DefaultProbability[stage];
                return new StageSummaryDto(stage, count, fees, probability, Math.Round(fees * probability / 100m, 2));
            })
            .ToList();

        var won = Totals(OpportunityStages.ClosedWon);
        var lost = Totals(OpportunityStages.ClosedLost);
        var closed = won.Count + lost.Count;

        return new DashboardSummaryDto(
            today,
            new PipelineSummaryDto(
                openStages.Sum(s => s.Count),
                openStages.Sum(s => s.Fees),
                openStages.Sum(s => s.WeightedFees),
                openStages),
            new OutcomeSummaryDto(
                won.Count, won.Fees, lost.Count, lost.Fees,
                closed == 0 ? null : Math.Round((decimal)won.Count / closed, 4),
                Counts(data.LossReasons, order: null)),
            new ActivitySummaryDto(
                data.Tasks.Open, data.Tasks.Overdue, data.Tasks.DueNext7Days, data.Tasks.Completed, data.Tasks.OpenFromAutomation),
            new HouseholdSummaryCountsDto(
                data.HouseholdStatuses.Sum(s => s.Count),
                Counts(data.HouseholdStatuses, StatusOrder)));
    }

    /// <summary>
    /// Merges duplicates, labels empty values "Not set", and orders by <paramref name="order"/> when
    /// given (unknown values last), otherwise by count, highest first, then name.
    /// </summary>
    private static List<CountDto> Counts(IEnumerable<NamedCount> counts, IReadOnlyList<string>? order)
    {
        var merged = counts
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Name) ? NotSet : c.Name)
            .Select(g => new CountDto(g.Key, g.Sum(c => c.Count)))
            .Where(c => c.Count > 0);

        if (order is null)
        {
            return merged.OrderByDescending(c => c.Count).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
        }

        int Rank(string name)
        {
            for (var i = 0; i < order.Count; i++)
            {
                if (string.Equals(order[i], name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        return merged.OrderBy(c => Rank(c.Name)).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
    }
}
