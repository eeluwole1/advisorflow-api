using System.Globalization;
using System.Text.Json.Serialization;
using AdvisorFlow.Domain.Dashboard;
using AdvisorFlow.Domain.Opportunities;
using AdvisorFlow.Infrastructure.Crm.Salesforce;

namespace AdvisorFlow.Infrastructure.Crm;

/// <summary>
/// Dashboard aggregates (M6) with aggregate SOQL (GROUP BY, COUNT, SUM), so Salesforce does the
/// counting and the API never pages through every record. Same scope as the lists: advisory
/// opportunities on households, tasks on households or their opportunities, household Accounts.
/// </summary>
public sealed partial class SalesforceCrmDataProvider
{
    public async Task<DashboardData> GetDashboardDataAsync(DateOnly today, CancellationToken ct)
    {
        var stages = await salesforce.QueryAsync<StageAggregate>(
            $"SELECT StageName, COUNT(Id) n, SUM(Amount) fees FROM Opportunity WHERE {IsAdvisoryOpportunity} GROUP BY StageName", ct);

        var lossReasons = await salesforce.QueryAsync<LossReasonAggregate>(
            $"SELECT Loss_Reason__c, COUNT(Id) n FROM Opportunity WHERE {IsAdvisoryOpportunity} " +
            $"AND StageName = {Soql.Quote(OpportunityStages.ClosedLost)} GROUP BY Loss_Reason__c", ct);

        var statuses = await salesforce.QueryAsync<StatusAggregate>(
            $"SELECT Client_Status__c, COUNT(Id) n FROM Account WHERE {IsHousehold} GROUP BY Client_Status__c", ct);

        var tasks = new TaskCounts(
            Open: await CountTasksAsync("IsClosed = false", ct),
            Overdue: await CountTasksAsync($"IsClosed = false AND ActivityDate < {SoqlDate(today)}", ct),
            DueNext7Days: await CountTasksAsync(
                $"IsClosed = false AND ActivityDate >= {SoqlDate(today)} AND ActivityDate <= {SoqlDate(today.AddDays(6))}", ct),
            Completed: await CountTasksAsync("IsClosed = true", ct),
            OpenFromAutomation: await CountTasksAsync("IsClosed = false AND Automation__c != null", ct));

        return new DashboardData(
            stages.Select(s => new StageTotal(s.StageName ?? string.Empty, s.Count, s.Fees ?? 0m)).ToList(),
            lossReasons.Select(l => new NamedCount(l.LossReason, l.Count)).ToList(),
            tasks,
            statuses.Select(s => new NamedCount(s.ClientStatus, s.Count)).ToList());
    }

    private Task<int> CountTasksAsync(string condition, CancellationToken ct) =>
        salesforce.CountAsync($"SELECT COUNT() FROM Task WHERE {IsHouseholdTask} AND {condition}", ct);

    /// <summary>A SOQL date literal (unquoted yyyy-MM-dd).</summary>
    private static string SoqlDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record StageAggregate(
        [property: JsonPropertyName("StageName")] string? StageName,
        [property: JsonPropertyName("n")] int Count,
        [property: JsonPropertyName("fees")] decimal? Fees);

    private sealed record LossReasonAggregate(
        [property: JsonPropertyName("Loss_Reason__c")] string? LossReason,
        [property: JsonPropertyName("n")] int Count);

    private sealed record StatusAggregate(
        [property: JsonPropertyName("Client_Status__c")] string? ClientStatus,
        [property: JsonPropertyName("n")] int Count);
}
