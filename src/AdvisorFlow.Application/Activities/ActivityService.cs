using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Activities;

namespace AdvisorFlow.Application.Activities;

/// <summary>
/// Read workflow for follow-up tasks (M5): validates list queries, works out the practice's date for
/// "overdue", and maps tasks to the API DTOs. Tasks are created by people in Salesforce or by the
/// Salesforce Flow (ADR-011); this service never creates them.
/// </summary>
public sealed class ActivityService(ICrmDataProvider provider, TimeProvider clock)
{
    /// <summary>
    /// The practice works in Winnipeg, so "today" and "overdue" follow Central time whatever
    /// time zone the server runs in (ADR-012).
    /// </summary>
    public const string PracticeTimeZoneId = "America/Winnipeg";

    private static readonly TimeZoneInfo PracticeTimeZone = FindTimeZone(PracticeTimeZoneId);

    /// <summary>Today's date in the practice's time zone.</summary>
    public DateOnly Today => TodayFor(clock);

    /// <summary>The practice's date at <paramref name="clock"/>'s current time.</summary>
    public static DateOnly TodayFor(TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), PracticeTimeZone).DateTime);

    /// <summary>Returns field errors; an empty dictionary means the query is valid.</summary>
    public static Dictionary<string, string[]> ValidateQuery(
        int page, int pageSize, string? search, string? state, string? householdId)
    {
        var errors = HouseholdService.ValidateQuery(page, pageSize, search, status: null);

        if (!string.IsNullOrWhiteSpace(state) && NormalizeState(state) is null)
        {
            errors["state"] = [$"State must be one of: {string.Join(", ", ActivityStates.Ordered)}."];
        }

        if (!string.IsNullOrWhiteSpace(householdId) && !HouseholdService.IsValidRecordId(householdId))
        {
            errors["householdId"] = ["Household id must be a 15- or 18-character Salesforce record id."];
        }

        return errors;
    }

    /// <summary>The state in its exact spelling, "open" when empty, or null when it is not a known state.</summary>
    public static string? NormalizeState(string? state) => string.IsNullOrWhiteSpace(state)
        ? ActivityStates.Open
        : ActivityStates.Ordered.FirstOrDefault(s => string.Equals(s, state.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>One page of tasks across all households (call <see cref="ValidateQuery"/> first).</summary>
    public async Task<PagedResponse<ActivityDto>> GetActivitiesAsync(
        int page, int pageSize, string? search, string? state, string? householdId, CancellationToken ct)
    {
        var today = Today;
        var query = new ActivityQuery(
            page, pageSize, string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            NormalizeState(state) ?? ActivityStates.Open,
            string.IsNullOrWhiteSpace(householdId) ? null : householdId, today);

        var result = await provider.GetTasksAsync(query, ct);
        var items = result.Items.Select(t => ToDto(t, today)).ToList();
        return new PagedResponse<ActivityDto>(items, result.Page, result.PageSize, result.TotalCount, result.TotalPages);
    }

    /// <summary>All of a household's tasks, including those on its opportunities; null when it does not exist.</summary>
    public async Task<IReadOnlyList<ActivityDto>?> GetHouseholdActivitiesAsync(string householdId, CancellationToken ct)
    {
        var tasks = await provider.GetHouseholdTasksAsync(householdId, ct);
        return tasks is null ? null : ToDtos(tasks);
    }

    /// <summary>An opportunity's tasks; null when the opportunity does not exist.</summary>
    public async Task<IReadOnlyList<ActivityDto>?> GetOpportunityActivitiesAsync(string opportunityId, CancellationToken ct)
    {
        var tasks = await provider.GetOpportunityTasksAsync(opportunityId, ct);
        return tasks is null ? null : ToDtos(tasks);
    }

    private List<ActivityDto> ToDtos(IEnumerable<ActivityTask> tasks)
    {
        var today = Today;
        return ActivityRules.InListOrder(tasks).Select(t => ToDto(t, today)).ToList();
    }

    private static ActivityDto ToDto(ActivityTask t, DateOnly today) => new(
        t.Id, t.Subject, t.Status, t.Priority, t.DueDate, t.IsClosed,
        ActivityRules.IsOverdue(t.IsClosed, t.DueDate, today),
        t.HouseholdId, t.HouseholdName, t.RelatedToId, t.RelatedToName, t.RelatedToType,
        t.ContactName, t.OwnerName, t.Automation);

    private static TimeZoneInfo FindTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            // A host without time zone data: fall back to UTC rather than failing every request.
            return TimeZoneInfo.Utc;
        }
    }
}
