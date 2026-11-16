using AdvisorFlow.Application.Activities;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Application.Opportunities;
using AdvisorFlow.Domain.Activities;
using AdvisorFlow.Domain.Dashboard;
using AdvisorFlow.Domain.Households;
using AdvisorFlow.Domain.Opportunities;

namespace AdvisorFlow.Application.Abstractions;

/// <summary>
/// The boundary between the application and the CRM system of record.
/// Infrastructure supplies a mock provider (local development) and a Salesforce
/// provider (M2). Writes were added in M3, opportunities in M4, tasks in M5. Implementations return null for "not found" and throw
/// <see cref="CrmUnavailableException"/> or <see cref="CrmUpstreamException"/>
/// for integration failures.
/// </summary>
public interface ICrmDataProvider
{
    Task<PagedResult<Household>> GetHouseholdsAsync(HouseholdQuery query, CancellationToken ct);

    Task<Household?> GetHouseholdAsync(string id, CancellationToken ct);

    /// <summary>Returns null when the household does not exist.</summary>
    Task<IReadOnlyList<HouseholdMember>?> GetMembersAsync(string householdId, CancellationToken ct);

    /// <summary>Members of several households in one round trip, for list views.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<HouseholdMember>>> GetMembersForHouseholdsAsync(
        IReadOnlyCollection<string> householdIds, CancellationToken ct);

    /// <summary>Creates a household and returns it as stored.</summary>
    Task<Household> CreateHouseholdAsync(HouseholdDraft draft, CancellationToken ct);

    /// <summary>Replaces the editable fields of a household. Returns null when it does not exist.</summary>
    Task<Household?> UpdateHouseholdAsync(string id, HouseholdDraft draft, CancellationToken ct);

    /// <summary>
    /// Adds a member to an existing household and returns it as stored. When the draft is
    /// primary, any other primary member is cleared in the same all-or-nothing change.
    /// </summary>
    Task<HouseholdMember> CreateMemberAsync(string householdId, MemberDraft draft, CancellationToken ct);

    /// <summary>
    /// Replaces a member's editable fields. Returns null when the member does not belong to the
    /// household. When the draft is primary, other primary members are cleared in the same change.
    /// </summary>
    Task<HouseholdMember?> UpdateMemberAsync(string householdId, string memberId, MemberDraft draft, CancellationToken ct);

    /// <summary>
    /// One page of advisory opportunities (Advisory Engagement record type on household Accounts),
    /// ordered by close date, then name. <see cref="OpportunityQuery.Stage"/> is already in its exact spelling.
    /// </summary>
    Task<PagedResult<Opportunity>> GetOpportunitiesAsync(OpportunityQuery query, CancellationToken ct);

    /// <summary>An advisory opportunity, or null when it does not exist.</summary>
    Task<Opportunity?> GetOpportunityAsync(string id, CancellationToken ct);

    /// <summary>A household's opportunities ordered by close date, or null when the household does not exist.</summary>
    Task<IReadOnlyList<Opportunity>?> GetHouseholdOpportunitiesAsync(string householdId, CancellationToken ct);

    /// <summary>Creates an advisory opportunity for an existing household and returns it as stored.</summary>
    Task<Opportunity> CreateOpportunityAsync(string householdId, OpportunityDraft draft, CancellationToken ct);

    /// <summary>Replaces an opportunity's editable fields. Returns null when it does not exist.</summary>
    Task<Opportunity?> UpdateOpportunityAsync(string id, OpportunityDraft draft, CancellationToken ct);

    /// <summary>
    /// One page of tasks on household Accounts or their opportunities, filtered by
    /// <see cref="ActivityQuery.State"/> ("overdue" uses <see cref="ActivityQuery.Today"/>),
    /// in the order of <see cref="ActivityRules.InListOrder"/>.
    /// </summary>
    Task<PagedResult<ActivityTask>> GetTasksAsync(ActivityQuery query, CancellationToken ct);

    /// <summary>
    /// A household's tasks, including those on its opportunities (up to 200, any order),
    /// or null when the household does not exist.
    /// </summary>
    Task<IReadOnlyList<ActivityTask>?> GetHouseholdTasksAsync(string householdId, CancellationToken ct);

    /// <summary>An advisory opportunity's tasks (up to 200, any order), or null when it does not exist.</summary>
    Task<IReadOnlyList<ActivityTask>?> GetOpportunityTasksAsync(string opportunityId, CancellationToken ct);

    /// <summary>
    /// Aggregates for the dashboard (M6): advisory opportunities per stage with their fees, loss
    /// reasons of Closed Lost opportunities, follow-up task counts ("overdue" and "due in the next
    /// 7 days" relative to <paramref name="today"/>), and households per client status.
    /// </summary>
    Task<DashboardData> GetDashboardDataAsync(DateOnly today, CancellationToken ct);
}

/// <summary>The CRM cannot be reached or is not configured. The API maps this to HTTP 503.</summary>
public sealed class CrmUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>The CRM answered with an error or unusable data. The API maps this to HTTP 502.</summary>
public class CrmUpstreamException(string message, Exception? inner = null)
    : Exception(message, inner);
