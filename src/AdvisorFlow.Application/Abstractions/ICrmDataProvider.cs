using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Households;

namespace AdvisorFlow.Application.Abstractions;

/// <summary>
/// The boundary between the application and the CRM system of record.
/// Infrastructure supplies a mock provider (local development) and a Salesforce
/// provider (M2). Implementations return null for "not found" and throw
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
}

/// <summary>The CRM cannot be reached or is not configured. The API maps this to HTTP 503.</summary>
public sealed class CrmUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>The CRM answered with an error or unusable data. The API maps this to HTTP 502.</summary>
public sealed class CrmUpstreamException(string message, Exception? inner = null)
    : Exception(message, inner);
