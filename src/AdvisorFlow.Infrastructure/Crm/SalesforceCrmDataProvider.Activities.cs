using System.Globalization;
using System.Text.Json.Serialization;
using AdvisorFlow.Application.Activities;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Activities;
using AdvisorFlow.Infrastructure.Crm.Salesforce;

namespace AdvisorFlow.Infrastructure.Crm;

/// <summary>
/// Follow-up tasks (M5): Salesforce Tasks whose Account is a household, attached either to the
/// household or to one of its opportunities (Task.AccountId is set for both). Read-only: tasks are
/// logged by people in Salesforce or created by the follow-up Flow (ADR-011). The integration user
/// needs Access Activities and read access to the Task fields below (runbook §10e).
/// </summary>
public sealed partial class SalesforceCrmDataProvider
{
    private const string TaskFields =
        "Id, Subject, Status, Priority, ActivityDate, IsClosed, AccountId, Account.Name, " +
        "WhatId, What.Name, What.Type, Who.Name, Owner.Name, Automation__c";

    /// <summary>Open before closed, then by due date with undated tasks last (ActivityRules.InListOrder).</summary>
    private const string TaskOrder = "ORDER BY IsClosed ASC, ActivityDate ASC NULLS LAST, Subject ASC, Id ASC";

    /// <summary>A household's or an opportunity's tasks are listed in full, up to this many.</summary>
    private const int MaxRecordTasks = 200;

    private const string IsHouseholdTask = "Account.RecordType.DeveloperName = 'Household'";

    public async Task<PagedResult<ActivityTask>> GetTasksAsync(ActivityQuery query, CancellationToken ct)
    {
        var where = BuildTaskFilter(query);
        var total = await salesforce.CountAsync($"SELECT COUNT() FROM Task WHERE {where}", ct);

        var offset = (query.Page - 1) * query.PageSize;
        if (offset >= total || offset > MaxOffset)
        {
            return new PagedResult<ActivityTask>([], query.Page, query.PageSize, total);
        }

        var records = await salesforce.QueryAsync<TaskRecord>(
            $"SELECT {TaskFields} FROM Task WHERE {where} {TaskOrder} LIMIT {query.PageSize} OFFSET {offset}", ct);
        return new PagedResult<ActivityTask>(records.Select(ToTask).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<ActivityTask>?> GetHouseholdTasksAsync(string householdId, CancellationToken ct)
    {
        if (await GetHouseholdAsync(householdId, ct) is not { } household)
        {
            return null;
        }

        var records = await salesforce.QueryAsync<TaskRecord>(
            $"SELECT {TaskFields} FROM Task WHERE AccountId = {Soql.Quote(household.Id)} AND {IsHouseholdTask} " +
            $"{TaskOrder} LIMIT {MaxRecordTasks}", ct);
        return records.Select(ToTask).ToList();
    }

    public async Task<IReadOnlyList<ActivityTask>?> GetOpportunityTasksAsync(string opportunityId, CancellationToken ct)
    {
        if (await GetOpportunityAsync(opportunityId, ct) is not { } opportunity)
        {
            return null;
        }

        var records = await salesforce.QueryAsync<TaskRecord>(
            $"SELECT {TaskFields} FROM Task WHERE WhatId = {Soql.Quote(opportunity.Id)} AND {IsHouseholdTask} " +
            $"{TaskOrder} LIMIT {MaxRecordTasks}", ct);
        return records.Select(ToTask).ToList();
    }

    /// <summary>The WHERE clause for the task list. User input is escaped (see <see cref="Soql"/>).</summary>
    public static string BuildTaskFilter(ActivityQuery query)
    {
        var conditions = new List<string> { IsHouseholdTask };

        switch (query.State)
        {
            case ActivityStates.Open:
                conditions.Add("IsClosed = false");
                break;
            case ActivityStates.Overdue:
                // A date literal is unquoted in SOQL; tasks without a due date never match.
                conditions.Add($"IsClosed = false AND ActivityDate < {query.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
                break;
            case ActivityStates.Closed:
                conditions.Add("IsClosed = true");
                break;
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add($"Subject LIKE {Soql.LikeContains(query.Search.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.HouseholdId))
        {
            conditions.Add($"AccountId = {Soql.Quote(query.HouseholdId)}");
        }

        return string.Join(" AND ", conditions);
    }

    private static ActivityTask ToTask(TaskRecord r) => new(
        r.Id,
        r.Subject ?? string.Empty,
        r.Status ?? string.Empty,
        r.Priority,
        r.ActivityDate,
        r.IsClosed,
        r.AccountId ?? string.Empty,
        r.Account?.Name ?? string.Empty,
        r.WhatId,
        r.What?.Name,
        r.What?.Type switch
        {
            null => null,
            "Account" => RelatedToTypes.Household,
            var type => type,
        },
        r.Who?.Name,
        r.Owner?.Name,
        r.Automation);

    private sealed record TaskRecord(
        [property: JsonPropertyName("Id")] string Id,
        [property: JsonPropertyName("Subject")] string? Subject,
        [property: JsonPropertyName("Status")] string? Status,
        [property: JsonPropertyName("Priority")] string? Priority,
        [property: JsonPropertyName("ActivityDate")] DateOnly? ActivityDate,
        [property: JsonPropertyName("IsClosed")] bool IsClosed,
        [property: JsonPropertyName("AccountId")] string? AccountId,
        [property: JsonPropertyName("Account")] AccountNameRecord? Account,
        [property: JsonPropertyName("WhatId")] string? WhatId,
        [property: JsonPropertyName("What")] PolymorphicNameRecord? What,
        [property: JsonPropertyName("Who")] PolymorphicNameRecord? Who,
        [property: JsonPropertyName("Owner")] PolymorphicNameRecord? Owner,
        [property: JsonPropertyName("Automation__c")] string? Automation);

    private sealed record PolymorphicNameRecord(
        [property: JsonPropertyName("Name")] string? Name,
        [property: JsonPropertyName("Type")] string? Type);
}
