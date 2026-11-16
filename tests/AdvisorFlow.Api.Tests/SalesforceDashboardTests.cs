using AdvisorFlow.Application.Dashboard;

namespace AdvisorFlow.Api.Tests;

/// <summary>The Salesforce provider's dashboard aggregates (M6) against a fake Salesforce.</summary>
public class SalesforceDashboardTests
{
    private static readonly FixedClock Noon16Nov = new(new DateTimeOffset(2026, 11, 16, 18, 0, 0, TimeSpan.Zero));

    /// <summary>Answers each aggregate query the way Salesforce does (grouped fields by name, aliases for aggregates).</summary>
    private static string Respond(string soql, string stages, string lossReasons, string statuses, Func<string, int> taskCount)
    {
        if (soql.StartsWith("SELECT COUNT() FROM Task", StringComparison.Ordinal))
        {
            return FakeSalesforce.Count(taskCount(soql));
        }

        if (soql.StartsWith("SELECT StageName", StringComparison.Ordinal))
        {
            return stages;
        }

        return soql.StartsWith("SELECT Loss_Reason__c", StringComparison.Ordinal) ? lossReasons : statuses;
    }

    [Fact]
    public async Task Aggregates_map_to_the_dashboard()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = r => FakeSalesforce.Json(Respond(
            r.Soql!,
            FakeSalesforce.QueryResult(
                """{"attributes":{"type":"AggregateResult"},"StageName":"Proposal Presented","n":2,"fees":20000.0}""",
                """{"attributes":{"type":"AggregateResult"},"StageName":"Prospecting","n":1,"fees":null}""",
                """{"attributes":{"type":"AggregateResult"},"StageName":"Closed Won","n":3,"fees":7500.0}""",
                """{"attributes":{"type":"AggregateResult"},"StageName":"Closed Lost","n":1,"fees":1000.0}"""),
            FakeSalesforce.QueryResult("""{"attributes":{"type":"AggregateResult"},"Loss_Reason__c":"Fees","n":1}"""),
            FakeSalesforce.QueryResult(
                """{"attributes":{"type":"AggregateResult"},"Client_Status__c":"Active","n":4}""",
                """{"attributes":{"type":"AggregateResult"},"Client_Status__c":null,"n":1}"""),
            soql => soql.Contains("Automation__c", StringComparison.Ordinal) ? 1
                : soql.Contains("ActivityDate >=", StringComparison.Ordinal) ? 3 // check before "<", which "<=" also contains
                : soql.Contains("ActivityDate <", StringComparison.Ordinal) ? 2
                : soql.Contains("IsClosed = true", StringComparison.Ordinal) ? 9 : 5));

        var summary = await new DashboardService(sf.Provider, Noon16Nov).GetSummaryAsync(CancellationToken.None);

        Assert.Equal((3, 20000m, 12000m), (summary.Pipeline.OpenCount, summary.Pipeline.OpenFees, summary.Pipeline.WeightedFees));
        Assert.Equal(0m, summary.Pipeline.Stages.Single(s => s.Stage == "Prospecting").Fees); // SUM of nulls
        Assert.Equal((3, 7500m, 1, 1000m, 0.75m), (summary.Outcomes.WonCount, summary.Outcomes.WonFees,
            summary.Outcomes.LostCount, summary.Outcomes.LostFees, summary.Outcomes.WinRate!.Value));
        Assert.Equal(new ActivitySummaryDto(5, 2, 3, 9, 1), summary.Activities);
        Assert.Equal(["Active:4", "Not set:1"], summary.Households.ByStatus.Select(s => $"{s.Name}:{s.Count}").ToArray());
    }

    [Fact]
    public async Task Queries_use_aggregates_and_the_same_scope_as_the_lists()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = r => FakeSalesforce.Json(Respond(
            r.Soql!, FakeSalesforce.QueryResult(), FakeSalesforce.QueryResult(), FakeSalesforce.QueryResult(), _ => 0));

        var summary = await new DashboardService(sf.Provider, Noon16Nov).GetSummaryAsync(CancellationToken.None);

        var queries = sf.Http.Queries.Select(q => q.Soql!).ToList();
        Assert.True(queries.Contains(
            "SELECT StageName, COUNT(Id) n, SUM(Amount) fees FROM Opportunity WHERE RecordType.DeveloperName = 'Advisory_Engagement' " +
            "AND Account.RecordType.DeveloperName = 'Household' GROUP BY StageName"));
        Assert.True(queries.Contains("SELECT Client_Status__c, COUNT(Id) n FROM Account WHERE RecordType.DeveloperName = 'Household' GROUP BY Client_Status__c"));
        Assert.True(queries.Contains(
            "SELECT COUNT() FROM Task WHERE Account.RecordType.DeveloperName = 'Household' AND IsClosed = false " +
            "AND ActivityDate >= 2026-11-16 AND ActivityDate <= 2026-11-22"));
        Assert.True(queries.Contains(
            "SELECT COUNT() FROM Task WHERE Account.RecordType.DeveloperName = 'Household' AND IsClosed = false AND ActivityDate < 2026-11-16"));

        // Nothing at all: every open stage is still listed, and there is no win rate yet.
        Assert.Equal(5, summary.Pipeline.Stages.Count);
        Assert.All(summary.Pipeline.Stages, s => Assert.Equal(0, s.Count));
        Assert.Null(summary.Outcomes.WinRate);
        Assert.Equal(0, summary.Households.Total);
    }
}
