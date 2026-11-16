using AdvisorFlow.Application.Activities;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Activities;

namespace AdvisorFlow.Infrastructure.Crm;

/// <summary>
/// Fictional follow-up tasks (M5). Ids imitate the Salesforce Task key prefix (00T). Due dates are
/// set relative to the day the provider is created, so the open, overdue and closed cases always
/// exist. Household and opportunity names are looked up on read, as Salesforce does.
/// The mock does not imitate the Salesforce Flow: creating an opportunity here adds no task.
/// </summary>
public sealed partial class MockCrmDataProvider
{
    private const string DemoAdvisor = "Demo Advisor";

    private IReadOnlyList<ActivityTask> SeedTasks(DateOnly today) =>
    [
        SeedTask(1, "Qualification follow-up: Williams Household - Retirement Planning", "Not Started", "Normal", today.AddDays(-4),
            "001MOCK00000000001", "006MOCK00000000001", RelatedToTypes.Opportunity, null, TaskAutomations.QualificationFollowUp),
        SeedTask(2, "Proposal review: Okafor Household - Investment Advisory", "Not Started", "Normal", today.AddDays(3),
            "001MOCK00000000002", "006MOCK00000000002", RelatedToTypes.Opportunity, null, TaskAutomations.ProposalReview),
        SeedTask(3, "Call Amara about RESP contributions", "In Progress", "High", today.AddDays(2),
            "001MOCK00000000002", "001MOCK00000000002", RelatedToTypes.Household, "Amara Okafor", null),
        SeedTask(4, "Send the fee schedule", "Not Started", "High", today.AddDays(-6),
            "001MOCK00000000004", "006MOCK00000000005", RelatedToTypes.Opportunity, "Linh Nguyen", null),
        SeedTask(5, "Send the annual review summary", "Completed", "Normal", today.AddDays(-27),
            "001MOCK00000000002", "006MOCK00000000003", RelatedToTypes.Opportunity, "Chinedu Okafor", null),
        SeedTask(6, "Send a thank-you note", "Completed", "Low", today.AddDays(-50),
            "001MOCK00000000005", "001MOCK00000000005", RelatedToTypes.Household, "Greta Fischer", null),
        SeedTask(7, "Collect pension statements", "Waiting on someone else", "Normal", null,
            "001MOCK00000000006", "006MOCK00000000007", RelatedToTypes.Opportunity, "Harpreet Singh", null),
        SeedTask(8, "Introductory call", "Not Started", "Low", today.AddDays(9),
            "001MOCK00000000003", "001MOCK00000000003", RelatedToTypes.Household, "Marc Tremblay", null),
        // Martin Household intentionally has no tasks (empty-state case).
    ];

    private List<ActivityTask> Tasks
    {
        get { lock (_lock) { return [.. _tasks.Select(WithCurrentNames)]; } }
    }

    public Task<PagedResult<ActivityTask>> GetTasksAsync(ActivityQuery query, CancellationToken ct)
    {
        IEnumerable<ActivityTask> filtered = Tasks;

        filtered = query.State switch
        {
            ActivityStates.Open => filtered.Where(t => !t.IsClosed),
            ActivityStates.Overdue => filtered.Where(t => ActivityRules.IsOverdue(t.IsClosed, t.DueDate, query.Today)),
            ActivityStates.Closed => filtered.Where(t => t.IsClosed),
            _ => filtered,
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            filtered = filtered.Where(t => t.Subject.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.HouseholdId))
        {
            filtered = filtered.Where(t => t.HouseholdId == query.HouseholdId);
        }

        var ordered = ActivityRules.InListOrder(filtered).ToList();
        var page = ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        return System.Threading.Tasks.Task.FromResult(new PagedResult<ActivityTask>(page, query.Page, query.PageSize, ordered.Count));
    }

    public Task<IReadOnlyList<ActivityTask>?> GetHouseholdTasksAsync(string householdId, CancellationToken ct)
    {
        if (Households.All(h => h.Id != householdId))
        {
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<ActivityTask>?>(null);
        }

        IReadOnlyList<ActivityTask> list = Tasks.Where(t => t.HouseholdId == householdId).ToList();
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<ActivityTask>?>(list);
    }

    public Task<IReadOnlyList<ActivityTask>?> GetOpportunityTasksAsync(string opportunityId, CancellationToken ct)
    {
        if (Opportunities.All(o => o.Id != opportunityId))
        {
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<ActivityTask>?>(null);
        }

        IReadOnlyList<ActivityTask> list = Tasks.Where(t => t.RelatedToId == opportunityId).ToList();
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<ActivityTask>?>(list);
    }

    /// <summary>Household and related-record names come from the current records, like Account.Name and What.Name.</summary>
    private ActivityTask WithCurrentNames(ActivityTask t) => t with
    {
        HouseholdName = _households.FirstOrDefault(h => h.Id == t.HouseholdId)?.Name ?? string.Empty,
        RelatedToName = t.RelatedToType == RelatedToTypes.Opportunity
            ? _opportunities.FirstOrDefault(o => o.Id == t.RelatedToId)?.Name
            : _households.FirstOrDefault(h => h.Id == t.RelatedToId)?.Name,
    };

    private static ActivityTask SeedTask(
        int number, string subject, string status, string priority, DateOnly? due, string householdId,
        string relatedToId, string relatedToType, string? contactName, string? automation) => new(
        $"00TMOCK{number:D11}", subject, status, priority, due,
        IsClosed: status == "Completed", householdId, HouseholdName: string.Empty, relatedToId,
        RelatedToName: null, relatedToType, contactName, DemoAdvisor, automation);
}
