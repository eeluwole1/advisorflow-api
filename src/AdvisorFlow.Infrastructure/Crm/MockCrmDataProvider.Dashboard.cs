using AdvisorFlow.Domain.Activities;
using AdvisorFlow.Domain.Dashboard;
using AdvisorFlow.Domain.Opportunities;

namespace AdvisorFlow.Infrastructure.Crm;

/// <summary>Dashboard aggregates (M6), computed from the in-memory records like a GROUP BY would.</summary>
public sealed partial class MockCrmDataProvider
{
    public Task<DashboardData> GetDashboardDataAsync(DateOnly today, CancellationToken ct)
    {
        List<Domain.Opportunities.Opportunity> opportunities;
        List<ActivityTask> tasks;
        List<Domain.Households.Household> households;
        lock (_lock)
        {
            opportunities = [.. _opportunities];
            tasks = [.. _tasks];
            households = [.. _households];
        }

        var stages = opportunities
            .GroupBy(o => o.Stage)
            .Select(g => new StageTotal(g.Key, g.Count(), g.Sum(o => o.Amount ?? 0m)))
            .ToList();

        var lossReasons = opportunities
            .Where(o => o.Stage == OpportunityStages.ClosedLost)
            .GroupBy(o => o.LossReason)
            .Select(g => new NamedCount(g.Key, g.Count()))
            .ToList();

        var open = tasks.Where(t => !t.IsClosed).ToList();
        var taskCounts = new TaskCounts(
            Open: open.Count,
            Overdue: open.Count(t => ActivityRules.IsOverdue(t.IsClosed, t.DueDate, today)),
            DueNext7Days: open.Count(t => t.DueDate is { } d && d >= today && d <= today.AddDays(6)),
            Completed: tasks.Count(t => t.IsClosed),
            OpenFromAutomation: open.Count(t => t.Automation is not null));

        var statuses = households
            .GroupBy(h => h.ClientStatus)
            .Select(g => new NamedCount(g.Key, g.Count()))
            .ToList();

        return System.Threading.Tasks.Task.FromResult(new DashboardData(stages, lossReasons, taskCounts, statuses));
    }
}
