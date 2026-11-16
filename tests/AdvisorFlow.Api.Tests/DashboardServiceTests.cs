using AdvisorFlow.Application.Activities;
using AdvisorFlow.Application.Dashboard;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Application.Opportunities;
using AdvisorFlow.Infrastructure.Crm;

namespace AdvisorFlow.Api.Tests;

/// <summary>
/// The dashboard (M6). The blueprint's exit criterion is that dashboard totals reconcile to known
/// test records, so these tests compare the dashboard with the list endpoints over the same data.
/// </summary>
public class DashboardServiceTests
{
    private static readonly FixedClock Noon16Nov = new(new DateTimeOffset(2026, 11, 16, 18, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Dashboard_matches_the_known_seed_totals()
    {
        var summary = await new DashboardService(new MockCrmDataProvider(Noon16Nov), Noon16Nov).GetSummaryAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 11, 16), summary.AsOf);
        Assert.Equal(5, summary.Pipeline.OpenCount);
        Assert.Equal(30500m, summary.Pipeline.OpenFees);
        Assert.Equal(16650m, summary.Pipeline.WeightedFees); // 4500×10% + 6000×30% + 12000×60% + 8000×90%
        Assert.Equal((1, 2500m, 1, 3000m, 0.5m), (summary.Outcomes.WonCount, summary.Outcomes.WonFees,
            summary.Outcomes.LostCount, summary.Outcomes.LostFees, summary.Outcomes.WinRate!.Value));
        Assert.Equal("Timing", Assert.Single(summary.Outcomes.LossReasons).Name);
        Assert.Equal(new ActivitySummaryDto(Open: 6, Overdue: 2, DueNext7Days: 2, Completed: 2, OpenFromAutomation: 2), summary.Activities);
        Assert.Equal(7, summary.Households.Total);
        Assert.Equal(["Prospect:3", "Active:3", "Inactive:1"], summary.Households.ByStatus.Select(s => $"{s.Name}:{s.Count}").ToArray());
    }

    [Fact]
    public async Task Dashboard_reconciles_with_the_list_endpoints()
    {
        var provider = new MockCrmDataProvider(Noon16Nov);
        var summary = await new DashboardService(provider, Noon16Nov).GetSummaryAsync(CancellationToken.None);

        var opportunities = (await new OpportunityService(provider).GetOpportunitiesAsync(new OpportunityQuery(1, 100, null, null, null), CancellationToken.None)).Items;
        var open = opportunities.Where(o => !o.IsClosed).ToList();
        Assert.Equal(open.Count, summary.Pipeline.OpenCount);
        Assert.Equal(open.Sum(o => o.Amount ?? 0m), summary.Pipeline.OpenFees);
        Assert.Equal(open.Sum(o => (o.Amount ?? 0m) * o.Probability!.Value / 100m), summary.Pipeline.WeightedFees);
        foreach (var stage in summary.Pipeline.Stages)
        {
            Assert.Equal(open.Count(o => o.Stage == stage.Stage), stage.Count);
        }

        var activities = new ActivityService(provider, Noon16Nov);
        async Task<int> Count(string state) => (await activities.GetActivitiesAsync(1, 100, null, state, null, CancellationToken.None)).TotalCount;
        Assert.Equal(await Count("open"), summary.Activities.Open);
        Assert.Equal(await Count("overdue"), summary.Activities.Overdue);
        Assert.Equal(await Count("closed"), summary.Activities.Completed);

        var households = await new HouseholdService(provider).GetHouseholdsAsync(new HouseholdQuery(1, 100, null, null), CancellationToken.None);
        Assert.Equal(households.TotalCount, summary.Households.Total);
    }
}
