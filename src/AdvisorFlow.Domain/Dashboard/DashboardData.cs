namespace AdvisorFlow.Domain.Dashboard;

/// <summary>
/// Raw totals for the dashboard (M6), as the CRM aggregates them. The application layer turns them
/// into the summary (weighted forecast, win rate, every stage listed). Fees are advisory fees, never
/// assets under management (BR-OP-003).
/// </summary>
public sealed record DashboardData(
    IReadOnlyList<StageTotal> Stages,
    IReadOnlyList<NamedCount> LossReasons,
    TaskCounts Tasks,
    IReadOnlyList<NamedCount> HouseholdStatuses);

/// <summary>Advisory opportunities in one stage and the sum of their fees (null amounts count as 0).</summary>
public sealed record StageTotal(string Stage, int Count, decimal Fees);

/// <summary>A value and how many records have it; <see cref="Name"/> is null for "not set".</summary>
public sealed record NamedCount(string? Name, int Count);

/// <summary>
/// Follow-up task counts on households and their opportunities. <see cref="DueNext7Days"/> is open
/// and due from today to today + 6, so it never overlaps <see cref="Overdue"/> (BR-ACT-003).
/// </summary>
public sealed record TaskCounts(int Open, int Overdue, int DueNext7Days, int Completed, int OpenFromAutomation);
