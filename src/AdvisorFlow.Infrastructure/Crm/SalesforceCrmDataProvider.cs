using System.Text.Json.Serialization;
using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Households;
using AdvisorFlow.Infrastructure.Crm.Salesforce;

namespace AdvisorFlow.Infrastructure.Crm;

/// <summary>
/// Reads households (Accounts with the Household record type, ADR-002) and their
/// members (Contacts) from Salesforce, as the read-only integration user.
/// Behaves like <see cref="MockCrmDataProvider"/>: same filters, ordering and paging,
/// so the API contract and the web app do not change when the provider does.
/// </summary>
public sealed class SalesforceCrmDataProvider(SalesforceQueryClient salesforce) : ICrmDataProvider
{
    /// <summary>SOQL rejects OFFSET values above 2,000. Pages beyond it come back empty.</summary>
    public const int MaxOffset = 2000;

    private const int MaxIdsPerQuery = 100;

    private const string HouseholdFields =
        "Id, Name, Client_Status__c, Client_Risk_Profile__c, CustomerPriority__c, Phone, AccountNumber";

    private const string MemberFields =
        "Id, AccountId, FirstName, LastName, Email, Phone, Preferred_Contact_Method__c";

    private const string IsHousehold = "RecordType.DeveloperName = 'Household'";

    public async Task<PagedResult<Household>> GetHouseholdsAsync(HouseholdQuery query, CancellationToken ct)
    {
        var where = BuildHouseholdFilter(query);
        var total = await salesforce.CountAsync($"SELECT COUNT() FROM Account WHERE {where}", ct);

        var offset = (query.Page - 1) * query.PageSize;
        if (offset >= total || offset > MaxOffset)
        {
            return new PagedResult<Household>([], query.Page, query.PageSize, total);
        }

        var records = await salesforce.QueryAsync<AccountRecord>(
            $"SELECT {HouseholdFields} FROM Account WHERE {where} " +
            $"ORDER BY Name ASC, Id ASC LIMIT {query.PageSize} OFFSET {offset}", ct);

        return new PagedResult<Household>(records.Select(ToHousehold).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<Household?> GetHouseholdAsync(string id, CancellationToken ct)
    {
        try
        {
            var records = await salesforce.QueryAsync<AccountRecord>(
                $"SELECT {HouseholdFields} FROM Account WHERE Id = {Soql.Quote(id)} AND {IsHousehold} LIMIT 1", ct);
            return records.Count == 0 ? null : ToHousehold(records[0]);
        }
        catch (SalesforceInvalidIdException)
        {
            // Well-formed for our API but not a real Salesforce id, e.g. a mock id.
            return null;
        }
    }

    public async Task<IReadOnlyList<HouseholdMember>?> GetMembersAsync(string householdId, CancellationToken ct)
    {
        if (await GetHouseholdAsync(householdId, ct) is not { } household)
        {
            return null;
        }

        var members = await GetMembersForHouseholdsAsync([household.Id], ct);
        return members[household.Id];
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<HouseholdMember>>> GetMembersForHouseholdsAsync(
        IReadOnlyCollection<string> householdIds, CancellationToken ct)
    {
        var ids = householdIds.Distinct(StringComparer.Ordinal).ToList();
        var byHousehold = ids.ToDictionary(id => id, _ => new List<HouseholdMember>(), StringComparer.Ordinal);

        foreach (var chunk in ids.Chunk(MaxIdsPerQuery))
        {
            var records = await salesforce.QueryAsync<ContactRecord>(
                $"SELECT {MemberFields} FROM Contact WHERE AccountId IN ({Soql.QuoteList(chunk)}) " +
                "ORDER BY LastName ASC, FirstName ASC, Id ASC", ct);

            foreach (var record in records)
            {
                if (record.AccountId is { } accountId && byHousehold.TryGetValue(accountId, out var list))
                {
                    list.Add(ToMember(record, accountId));
                }
            }
        }

        return byHousehold.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<HouseholdMember>)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>The WHERE clause for the household list. User input is escaped (see <see cref="Soql"/>).</summary>
    public static string BuildHouseholdFilter(HouseholdQuery query)
    {
        var conditions = new List<string> { IsHousehold };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add($"Name LIKE {Soql.LikeContains(query.Search.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            // The service has already validated the value (BR-CL-001); use the picklist's exact spelling.
            var status = ClientStatuses.All.FirstOrDefault(
                s => string.Equals(s, query.Status, StringComparison.OrdinalIgnoreCase)) ?? query.Status;
            conditions.Add($"Client_Status__c = {Soql.Quote(status)}");
        }

        return string.Join(" AND ", conditions);
    }

    // A blank Client Status is reported as Prospect, the picklist's default value (BR-CL-001).
    private static Household ToHousehold(AccountRecord r) => new(
        r.Id, r.Name ?? string.Empty, r.ClientStatus ?? ClientStatuses.Prospect,
        r.RiskProfile, r.Priority, r.Phone, r.AccountNumber);

    // The primary-member flag (BR-HH-003) has no Salesforce field until M3.
    private static HouseholdMember ToMember(ContactRecord r, string householdId) => new(
        r.Id, householdId, r.FirstName ?? string.Empty, r.LastName ?? string.Empty,
        r.Email, r.Phone, r.PreferredContactMethod, IsPrimary: false);

    private sealed record AccountRecord(
        [property: JsonPropertyName("Id")] string Id,
        [property: JsonPropertyName("Name")] string? Name,
        [property: JsonPropertyName("Client_Status__c")] string? ClientStatus,
        [property: JsonPropertyName("Client_Risk_Profile__c")] string? RiskProfile,
        [property: JsonPropertyName("CustomerPriority__c")] string? Priority,
        [property: JsonPropertyName("Phone")] string? Phone,
        [property: JsonPropertyName("AccountNumber")] string? AccountNumber);

    private sealed record ContactRecord(
        [property: JsonPropertyName("Id")] string Id,
        [property: JsonPropertyName("AccountId")] string? AccountId,
        [property: JsonPropertyName("FirstName")] string? FirstName,
        [property: JsonPropertyName("LastName")] string? LastName,
        [property: JsonPropertyName("Email")] string? Email,
        [property: JsonPropertyName("Phone")] string? Phone,
        [property: JsonPropertyName("Preferred_Contact_Method__c")] string? PreferredContactMethod);
}
