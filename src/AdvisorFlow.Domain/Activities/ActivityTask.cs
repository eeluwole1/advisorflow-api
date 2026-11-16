namespace AdvisorFlow.Domain.Activities;

/// <summary>
/// A follow-up task (Salesforce Task) for a household or one of its advisory opportunities (M5).
/// <see cref="HouseholdId"/> is the household Account the task belongs to; <see cref="RelatedToId"/>
/// is the record it is attached to: the household itself or one of its opportunities.
/// <see cref="Automation"/> names the AdvisorFlow automation that created it, or null when a person
/// logged it (ADR-011).
/// </summary>
public sealed record ActivityTask(
    string Id,
    string Subject,
    string Status,
    string? Priority,
    DateOnly? DueDate,
    bool IsClosed,
    string HouseholdId,
    string HouseholdName,
    string? RelatedToId,
    string? RelatedToName,
    string? RelatedToType,
    string? ContactName,
    string? OwnerName,
    string? Automation);

/// <summary>Which tasks a list shows: still to do, past their due date, done, or everything.</summary>
public static class ActivityStates
{
    public const string Open = "open";
    public const string Overdue = "overdue";
    public const string Closed = "closed";
    public const string All = "all";

    public static readonly IReadOnlyList<string> Ordered = [Open, Overdue, Closed, All];
}

/// <summary>What a task is attached to, as the API reports it.</summary>
public static class RelatedToTypes
{
    public const string Household = "Household";
    public const string Opportunity = "Opportunity";
}

/// <summary>Values of the Task field Automation__c (ADR-011).</summary>
public static class TaskAutomations
{
    public const string QualificationFollowUp = "Qualification follow-up";
    public const string ProposalReview = "Proposal review";
}

public static class ActivityRules
{
    /// <summary>
    /// A task is overdue when it is still open and its due date is before today, in the practice's
    /// time zone. A task without a due date is never overdue (BR-ACT-003).
    /// </summary>
    public static bool IsOverdue(bool isClosed, DateOnly? dueDate, DateOnly today) =>
        !isClosed && dueDate is { } due && due < today;

    /// <summary>
    /// The order of every task list: open before closed, then by due date with undated tasks
    /// last, then subject and id so the order is stable.
    /// </summary>
    public static IEnumerable<ActivityTask> InListOrder(IEnumerable<ActivityTask> tasks) => tasks
        .OrderBy(t => t.IsClosed)
        .ThenBy(t => t.DueDate is null)
        .ThenBy(t => t.DueDate)
        .ThenBy(t => t.Subject, StringComparer.OrdinalIgnoreCase)
        .ThenBy(t => t.Id, StringComparer.Ordinal);
}
