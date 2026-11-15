using System.Text.Json;
using System.Text.Json.Serialization;
using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Households;
using AdvisorFlow.Infrastructure.Crm.Salesforce;

namespace AdvisorFlow.Infrastructure.Crm;

/// <summary>
/// Reads and writes households (Accounts with the Household record type, ADR-002) and their
/// members (Contacts) in Salesforce, as the integration user (ADR-007, ADR-008). Member changes
/// that move the primary flag run as one all-or-nothing composite request (BR-HH-003).
/// Behaves like <see cref="MockCrmDataProvider"/>: same filters, ordering and paging,
/// so the API contract and the web app do not change when the provider does.
/// </summary>
public sealed class SalesforceCrmDataProvider(SalesforceRestClient salesforce) : ICrmDataProvider
{
    /// <summary>SOQL rejects OFFSET values above 2,000. Pages beyond it come back empty.</summary>
    public const int MaxOffset = 2000;

    private const int MaxIdsPerQuery = 100;

    private const string HouseholdFields =
        "Id, Name, Client_Status__c, Client_Risk_Profile__c, CustomerPriority__c, Phone, AccountNumber";

    private const string MemberFields =
        "Id, AccountId, FirstName, LastName, Email, Phone, Preferred_Contact_Method__c, Is_Primary_Member__c";

    private const string IsHousehold = "RecordType.DeveloperName = 'Household'";

    private string? _householdRecordTypeId;

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
                "ORDER BY Is_Primary_Member__c DESC, LastName ASC, FirstName ASC, Id ASC", ct);

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

    public async Task<Household> CreateHouseholdAsync(HouseholdDraft draft, CancellationToken ct)
    {
        var fields = ToAccountFields(draft);
        fields["RecordTypeId"] = await GetHouseholdRecordTypeIdAsync(ct);

        var id = await salesforce.CreateAsync("Account", fields, ct);
        return await GetHouseholdAsync(id, ct)
            ?? throw new CrmUpstreamException("The new household could not be read back from Salesforce.");
    }

    public async Task<Household?> UpdateHouseholdAsync(string id, HouseholdDraft draft, CancellationToken ct)
    {
        // Only Household Accounts can be changed through this API; anything else is "not found".
        if (await GetHouseholdAsync(id, ct) is null)
        {
            return null;
        }

        await salesforce.UpdateAsync("Account", id, ToAccountFields(draft), ct);
        return await GetHouseholdAsync(id, ct);
    }

    public async Task<HouseholdMember> CreateMemberAsync(string householdId, MemberDraft draft, CancellationToken ct)
    {
        var steps = draft.IsPrimary ? await ClearPrimaryStepsAsync(householdId, exceptMemberId: null, ct) : [];
        var fields = ToContactFields(draft);
        fields["AccountId"] = householdId;
        steps.Add(new CompositeSubrequest("POST", salesforce.SObjectPath("Contact"), "member", fields));

        var results = await salesforce.CompositeAsync(steps, ct);
        var created = results.First(r => r.ReferenceId == "member").Body;
        var id = created.ValueKind == JsonValueKind.Object && created.TryGetProperty("id", out var idProperty)
            ? idProperty.GetString()
            : null;

        return (id is null ? null : await GetMemberAsync(householdId, id, ct))
            ?? throw new CrmUpstreamException("The new member could not be read back from Salesforce.");
    }

    public async Task<HouseholdMember?> UpdateMemberAsync(
        string householdId, string memberId, MemberDraft draft, CancellationToken ct)
    {
        if (await GetMemberAsync(householdId, memberId, ct) is null)
        {
            return null;
        }

        var steps = draft.IsPrimary ? await ClearPrimaryStepsAsync(householdId, memberId, ct) : [];
        steps.Add(new CompositeSubrequest("PATCH", salesforce.SObjectPath("Contact", memberId), "member", ToContactFields(draft)));

        await salesforce.CompositeAsync(steps, ct);
        return await GetMemberAsync(householdId, memberId, ct);
    }

    /// <summary>A member of the household, or null when the id is not a Contact of that household.</summary>
    private async Task<HouseholdMember?> GetMemberAsync(string householdId, string memberId, CancellationToken ct)
    {
        try
        {
            var records = await salesforce.QueryAsync<ContactRecord>(
                $"SELECT {MemberFields} FROM Contact WHERE Id = {Soql.Quote(memberId)} " +
                $"AND AccountId = {Soql.Quote(householdId)} LIMIT 1", ct);
            return records.Count == 0 ? null : ToMember(records[0], householdId);
        }
        catch (SalesforceInvalidIdException)
        {
            return null;
        }
    }

    /// <summary>Composite steps that un-set the household's other primary members.</summary>
    private async Task<List<CompositeSubrequest>> ClearPrimaryStepsAsync(
        string householdId, string? exceptMemberId, CancellationToken ct)
    {
        var current = await salesforce.QueryAsync<IdRecord>(
            $"SELECT Id FROM Contact WHERE AccountId = {Soql.Quote(householdId)} AND Is_Primary_Member__c = true", ct);

        return current
            .Where(c => !string.Equals(c.Id, exceptMemberId, StringComparison.Ordinal))
            .Select((c, i) => new CompositeSubrequest(
                "PATCH", salesforce.SObjectPath("Contact", c.Id), $"clearPrimary{i}",
                new Dictionary<string, object?> { ["Is_Primary_Member__c"] = false }))
            .ToList();
    }

    /// <summary>Editable Contact fields. Null values clear the field (PUT semantics).</summary>
    private static Dictionary<string, object?> ToContactFields(MemberDraft draft) => new()
    {
        ["FirstName"] = draft.FirstName,
        ["LastName"] = draft.LastName,
        ["Email"] = draft.Email,
        ["Phone"] = draft.Phone,
        ["Preferred_Contact_Method__c"] = draft.PreferredContactMethod,
        ["Is_Primary_Member__c"] = draft.IsPrimary,
    };

    /// <summary>Editable Account fields. Null values clear the field (PUT semantics).</summary>
    private static Dictionary<string, object?> ToAccountFields(HouseholdDraft draft) => new()
    {
        ["Name"] = draft.Name,
        ["Client_Status__c"] = draft.ClientStatus,
        ["Client_Risk_Profile__c"] = draft.RiskProfile,
        ["CustomerPriority__c"] = draft.Priority,
        ["Phone"] = draft.Phone,
    };

    /// <summary>The org's Id of the Account record type "Household", looked up once and cached.</summary>
    private async Task<string> GetHouseholdRecordTypeIdAsync(CancellationToken ct)
    {
        if (_householdRecordTypeId is { } cached)
        {
            return cached;
        }

        var records = await salesforce.QueryAsync<RecordTypeRecord>(
            "SELECT Id FROM RecordType WHERE SobjectType = 'Account' AND DeveloperName = 'Household' LIMIT 1", ct);
        return _householdRecordTypeId = records.Count == 1
            ? records[0].Id
            : throw new CrmUpstreamException("The Account record type 'Household' was not found in Salesforce.");
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

    private static HouseholdMember ToMember(ContactRecord r, string householdId) => new(
        r.Id, householdId, r.FirstName ?? string.Empty, r.LastName ?? string.Empty,
        r.Email, r.Phone, r.PreferredContactMethod, r.IsPrimary == true);

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
        [property: JsonPropertyName("Preferred_Contact_Method__c")] string? PreferredContactMethod,
        [property: JsonPropertyName("Is_Primary_Member__c")] bool? IsPrimary);

    private sealed record RecordTypeRecord([property: JsonPropertyName("Id")] string Id);

    private sealed record IdRecord([property: JsonPropertyName("Id")] string Id);
}
