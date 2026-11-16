using System.Text.Json.Serialization;
using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Application.Opportunities;
using AdvisorFlow.Domain.Opportunities;
using AdvisorFlow.Infrastructure.Crm.Salesforce;

namespace AdvisorFlow.Infrastructure.Crm;

/// <summary>
/// Advisory opportunities (M4): Opportunities with the Advisory Engagement record type, whose
/// sales process limits Stage to the seven AdvisorFlow stages, on household Accounts only.
/// Salesforce also enforces BR-OP-002 with the validation rule Loss_Reason_Required_When_Closed_Lost.
/// </summary>
public sealed partial class SalesforceCrmDataProvider
{
    private const string OpportunityFields =
        "Id, AccountId, Account.Name, Name, StageName, CloseDate, Amount, Probability, " +
        "Service_Type__c, Engagement_Priority__c, NextStep, Loss_Reason__c";

    private const string OpportunityOrder = "ORDER BY CloseDate ASC, Name ASC, Id ASC";

    /// <summary>A household's opportunities are listed in full, up to this many.</summary>
    private const int MaxHouseholdOpportunities = 200;

    private const string IsAdvisoryOpportunity =
        "RecordType.DeveloperName = 'Advisory_Engagement' AND Account.RecordType.DeveloperName = 'Household'";

    private string? _advisoryRecordTypeId;

    public async Task<PagedResult<Opportunity>> GetOpportunitiesAsync(OpportunityQuery query, CancellationToken ct)
    {
        var where = BuildOpportunityFilter(query);
        var total = await salesforce.CountAsync($"SELECT COUNT() FROM Opportunity WHERE {where}", ct);

        var offset = (query.Page - 1) * query.PageSize;
        if (offset >= total || offset > MaxOffset)
        {
            return new PagedResult<Opportunity>([], query.Page, query.PageSize, total);
        }

        var records = await salesforce.QueryAsync<OpportunityRecord>(
            $"SELECT {OpportunityFields} FROM Opportunity WHERE {where} " +
            $"{OpportunityOrder} LIMIT {query.PageSize} OFFSET {offset}", ct);

        return new PagedResult<Opportunity>(records.Select(ToOpportunity).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<Opportunity?> GetOpportunityAsync(string id, CancellationToken ct)
    {
        try
        {
            var records = await salesforce.QueryAsync<OpportunityRecord>(
                $"SELECT {OpportunityFields} FROM Opportunity WHERE Id = {Soql.Quote(id)} AND {IsAdvisoryOpportunity} LIMIT 1", ct);
            return records.Count == 0 ? null : ToOpportunity(records[0]);
        }
        catch (SalesforceInvalidIdException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Opportunity>?> GetHouseholdOpportunitiesAsync(string householdId, CancellationToken ct)
    {
        if (await GetHouseholdAsync(householdId, ct) is not { } household)
        {
            return null;
        }

        var records = await salesforce.QueryAsync<OpportunityRecord>(
            $"SELECT {OpportunityFields} FROM Opportunity WHERE AccountId = {Soql.Quote(household.Id)} " +
            $"AND {IsAdvisoryOpportunity} {OpportunityOrder} LIMIT {MaxHouseholdOpportunities}", ct);
        return records.Select(ToOpportunity).ToList();
    }

    public async Task<Opportunity> CreateOpportunityAsync(string householdId, OpportunityDraft draft, CancellationToken ct)
    {
        var fields = ToOpportunityFields(draft);
        fields["AccountId"] = householdId;
        fields["RecordTypeId"] = await GetAdvisoryRecordTypeIdAsync(ct);

        var id = await salesforce.CreateAsync("Opportunity", fields, ct);
        return await GetOpportunityAsync(id, ct)
            ?? throw new CrmUpstreamException("The new opportunity could not be read back from Salesforce.");
    }

    public async Task<Opportunity?> UpdateOpportunityAsync(string id, OpportunityDraft draft, CancellationToken ct)
    {
        // Only advisory opportunities on households can be changed through this API.
        if (await GetOpportunityAsync(id, ct) is null)
        {
            return null;
        }

        await salesforce.UpdateAsync("Opportunity", id, ToOpportunityFields(draft), ct);
        return await GetOpportunityAsync(id, ct);
    }

    /// <summary>The WHERE clause for the pipeline. User input is escaped (see <see cref="Soql"/>).</summary>
    public static string BuildOpportunityFilter(OpportunityQuery query)
    {
        var conditions = new List<string> { IsAdvisoryOpportunity };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add($"Name LIKE {Soql.LikeContains(query.Search.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.Stage))
        {
            conditions.Add($"StageName = {Soql.Quote(query.Stage)}");
        }

        if (!string.IsNullOrWhiteSpace(query.HouseholdId))
        {
            conditions.Add($"AccountId = {Soql.Quote(query.HouseholdId)}");
        }

        return string.Join(" AND ", conditions);
    }

    /// <summary>Editable Opportunity fields. Null values clear the field (PUT semantics).
    /// Probability is left to Salesforce, which sets it from the stage.</summary>
    private static Dictionary<string, object?> ToOpportunityFields(OpportunityDraft draft) => new()
    {
        ["Name"] = draft.Name,
        ["StageName"] = draft.Stage,
        ["CloseDate"] = draft.CloseDate,
        ["Amount"] = draft.Amount,
        ["Service_Type__c"] = draft.ServiceType,
        ["Engagement_Priority__c"] = draft.EngagementPriority,
        ["NextStep"] = draft.NextStep,
        ["Loss_Reason__c"] = draft.LossReason,
    };

    /// <summary>The org's Id of the Opportunity record type "Advisory_Engagement", looked up once and cached.</summary>
    private async Task<string> GetAdvisoryRecordTypeIdAsync(CancellationToken ct)
    {
        if (_advisoryRecordTypeId is { } cached)
        {
            return cached;
        }

        var records = await salesforce.QueryAsync<RecordTypeRecord>(
            "SELECT Id FROM RecordType WHERE SobjectType = 'Opportunity' AND DeveloperName = 'Advisory_Engagement' LIMIT 1", ct);
        return _advisoryRecordTypeId = records.Count == 1
            ? records[0].Id
            : throw new CrmUpstreamException("The Opportunity record type 'Advisory_Engagement' was not found in Salesforce.");
    }

    private static Opportunity ToOpportunity(OpportunityRecord r) => new(
        r.Id,
        r.AccountId ?? string.Empty,
        r.Account?.Name ?? string.Empty,
        r.Name ?? string.Empty,
        r.StageName ?? OpportunityStages.Prospecting,
        r.CloseDate ?? throw new CrmUpstreamException("A Salesforce opportunity has no close date."),
        r.Amount,
        r.Probability is { } p ? (int)decimal.Round(p) : null,
        r.ServiceType,
        r.EngagementPriority,
        r.NextStep,
        r.LossReason);

    private sealed record OpportunityRecord(
        [property: JsonPropertyName("Id")] string Id,
        [property: JsonPropertyName("AccountId")] string? AccountId,
        [property: JsonPropertyName("Account")] AccountNameRecord? Account,
        [property: JsonPropertyName("Name")] string? Name,
        [property: JsonPropertyName("StageName")] string? StageName,
        [property: JsonPropertyName("CloseDate")] DateOnly? CloseDate,
        [property: JsonPropertyName("Amount")] decimal? Amount,
        [property: JsonPropertyName("Probability")] decimal? Probability,
        [property: JsonPropertyName("Service_Type__c")] string? ServiceType,
        [property: JsonPropertyName("Engagement_Priority__c")] string? EngagementPriority,
        [property: JsonPropertyName("NextStep")] string? NextStep,
        [property: JsonPropertyName("Loss_Reason__c")] string? LossReason);

    private sealed record AccountNameRecord([property: JsonPropertyName("Name")] string? Name);
}
