using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Households;
using Microsoft.Extensions.Options;

namespace AdvisorFlow.Infrastructure.Crm;

/// <summary>Bound from the "Salesforce" configuration section. Secrets come from
/// user-secrets locally and Azure Key Vault in deployed environments, never appsettings.json.</summary>
public sealed class SalesforceOptions
{
    public const string SectionName = "Salesforce";

    /// <summary>e.g. https://login.salesforce.com, or the org's My Domain login URL.</summary>
    public string? LoginUrl { get; set; }

    /// <summary>Consumer key of the External Client App used for the JWT bearer flow.</summary>
    public string? ClientId { get; set; }

    /// <summary>Username of the dedicated, least-privilege integration user.</summary>
    public string? IntegrationUsername { get; set; }

    /// <summary>Key Vault certificate name, or a local PEM path for development only.</summary>
    public string? PrivateKeyReference { get; set; }

    public string ApiVersion { get; set; } = "v68.0";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(LoginUrl) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(IntegrationUsername) &&
        !string.IsNullOrWhiteSpace(PrivateKeyReference);
}

/// <summary>
/// Placeholder for milestone M2. It is selected only when Crm:Provider is "Salesforce".
/// Until the OAuth integration is implemented, every call reports the CRM as unavailable
/// (HTTP 503) instead of returning fake data, so it can never be mistaken for real records.
///
/// M2 plan: JWT bearer token exchange, SOQL queries against Account/Contact
/// (with a Household record type or classification filter), mapping to Domain
/// records, and retry with backoff on transient failures.
/// </summary>
public sealed class SalesforceCrmDataProvider(IOptions<SalesforceOptions> options) : ICrmDataProvider
{
    private readonly SalesforceOptions _options = options.Value;

    public Task<PagedResult<Household>> GetHouseholdsAsync(HouseholdQuery query, CancellationToken ct) =>
        throw NotReady();

    public Task<Household?> GetHouseholdAsync(string id, CancellationToken ct) =>
        throw NotReady();

    public Task<IReadOnlyList<HouseholdMember>?> GetMembersAsync(string householdId, CancellationToken ct) =>
        throw NotReady();

    public Task<IReadOnlyDictionary<string, IReadOnlyList<HouseholdMember>>> GetMembersForHouseholdsAsync(
        IReadOnlyCollection<string> householdIds, CancellationToken ct) =>
        throw NotReady();

    private CrmUnavailableException NotReady() => _options.IsConfigured
        ? new CrmUnavailableException("The Salesforce provider is configured but not implemented yet (milestone M2).")
        : new CrmUnavailableException("The Salesforce provider is not configured. Set Crm:Provider to \"Mock\" for local development.");
}
