using System.Net;
using AdvisorFlow.Application.Activities;
using AdvisorFlow.Domain.Activities;
using AdvisorFlow.Infrastructure.Crm;

namespace AdvisorFlow.Api.Tests;

/// <summary>The Salesforce provider's task reads (M5) against a fake Salesforce.</summary>
public class SalesforceActivityTests
{
    private const string HouseholdTask = "Account.RecordType.DeveloperName = 'Household'";
    private const string OkaforId = "001fj00002FHXAnAAP";
    private const string OpportunityId = "006fj00000AbCdEAAA";
    private static readonly DateOnly Today = new(2026, 11, 16);

    private const string OkaforAccount =
        """{"attributes":{"type":"Account"},"Id":"001fj00002FHXAnAAP","Name":"Okafor Household","Client_Status__c":"Active","Client_Risk_Profile__c":null,"CustomerPriority__c":"High","Phone":null,"AccountNumber":null}""";

    private const string OkaforAdvisory =
        """{"attributes":{"type":"Opportunity"},"Id":"006fj00000AbCdEAAA","AccountId":"001fj00002FHXAnAAP","Account":{"Name":"Okafor Household"},"Name":"Okafor Household - Investment Advisory","StageName":"Proposal Presented","CloseDate":"2026-12-01","Amount":12000.0,"Probability":60.0,"Service_Type__c":null,"Engagement_Priority__c":null,"NextStep":null,"Loss_Reason__c":null}""";

    private const string FlowTask =
        """{"attributes":{"type":"Task"},"Id":"00Tfj00000Q1AAAA","Subject":"Proposal review: Okafor Household - Investment Advisory","Status":"Not Started","Priority":"Normal","ActivityDate":"2026-11-19","IsClosed":false,"AccountId":"001fj00002FHXAnAAP","Account":{"Name":"Okafor Household"},"WhatId":"006fj00000AbCdEAAA","What":{"attributes":{"type":"Name"},"Name":"Okafor Household - Investment Advisory","Type":"Opportunity"},"Who":null,"Owner":{"attributes":{"type":"Name"},"Name":"Demo Advisor","Type":"User"},"Automation__c":"Proposal review"}""";

    private const string HouseholdCall =
        """{"attributes":{"type":"Task"},"Id":"00Tfj00000Q2AAAA","Subject":"Call Amara","Status":"In Progress","Priority":"High","ActivityDate":null,"IsClosed":false,"AccountId":"001fj00002FHXAnAAP","Account":{"Name":"Okafor Household"},"WhatId":"001fj00002FHXAnAAP","What":{"Name":"Okafor Household","Type":"Account"},"Who":{"Name":"Amara Okafor","Type":"Contact"},"Owner":{"Name":"Demo Advisor","Type":"User"},"Automation__c":null}""";

    private static ActivityQuery Query(string state, string? search = null, string? household = null) =>
        new(1, 20, search, state, household, Today);

    [Fact]
    public void Each_state_adds_its_condition_after_the_household_filter()
    {
        Assert.Equal($"{HouseholdTask} AND IsClosed = false", SalesforceCrmDataProvider.BuildTaskFilter(Query(ActivityStates.Open)));
        Assert.Equal($"{HouseholdTask} AND IsClosed = false AND ActivityDate < 2026-11-16",
            SalesforceCrmDataProvider.BuildTaskFilter(Query(ActivityStates.Overdue)));
        Assert.Equal($"{HouseholdTask} AND IsClosed = true", SalesforceCrmDataProvider.BuildTaskFilter(Query(ActivityStates.Closed)));
        Assert.Equal(HouseholdTask, SalesforceCrmDataProvider.BuildTaskFilter(Query(ActivityStates.All)));
    }

    [Fact]
    public void Search_and_household_are_escaped()
    {
        Assert.Equal($"{HouseholdTask} AND Subject LIKE '%O\\'Brien%' AND AccountId = '{OkaforId}'",
            SalesforceCrmDataProvider.BuildTaskFilter(Query(ActivityStates.All, " O'Brien ", OkaforId)));
    }

    [Fact]
    public async Task Lists_tasks_with_count_order_and_mapping()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = r => FakeSalesforce.Json(r.Soql!.StartsWith("SELECT COUNT()", StringComparison.Ordinal)
            ? FakeSalesforce.Count(2)
            : FakeSalesforce.QueryResult(FlowTask, HouseholdCall));

        var page = await sf.Provider.GetTasksAsync(Query(ActivityStates.Open), CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(
            new ActivityTask("00Tfj00000Q1AAAA", "Proposal review: Okafor Household - Investment Advisory", "Not Started", "Normal",
                new DateOnly(2026, 11, 19), false, OkaforId, "Okafor Household", OpportunityId,
                "Okafor Household - Investment Advisory", "Opportunity", null, "Demo Advisor", "Proposal review"),
            page.Items[0]);

        var call = page.Items[1];
        Assert.Equal(RelatedToTypes.Household, call.RelatedToType); // What.Type "Account" reads as Household
        Assert.Equal("Amara Okafor", call.ContactName);
        Assert.Null(call.DueDate);

        var queries = sf.Http.Queries.Select(q => q.Soql!).ToList();
        Assert.Equal($"SELECT COUNT() FROM Task WHERE {HouseholdTask} AND IsClosed = false", queries[0]);
        Assert.EndsWith(
            "ORDER BY IsClosed ASC, ActivityDate ASC NULLS LAST, Subject ASC, Id ASC LIMIT 20 OFFSET 0", queries[1]);
        Assert.Contains("What.Type", queries[1]);
        Assert.Contains("Automation__c", queries[1]);
    }

    [Fact]
    public async Task Household_tasks_check_the_household_first()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = r => FakeSalesforce.Json(r.Soql!.Contains("FROM Account", StringComparison.Ordinal)
            ? FakeSalesforce.QueryResult(OkaforAccount)
            : FakeSalesforce.QueryResult(HouseholdCall));

        var tasks = await sf.Provider.GetHouseholdTasksAsync(OkaforId, CancellationToken.None);

        Assert.Single(tasks!);
        Assert.Contains($"FROM Task WHERE AccountId = '{OkaforId}' AND {HouseholdTask}", sf.Http.Queries.Last().Soql);
    }

    [Fact]
    public async Task Unknown_household_or_opportunity_reads_as_null_without_querying_tasks()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.QueryResult());

        Assert.Null(await sf.Provider.GetHouseholdTasksAsync(OkaforId, CancellationToken.None));
        Assert.Null(await sf.Provider.GetOpportunityTasksAsync(OpportunityId, CancellationToken.None));
        Assert.DoesNotContain(sf.Http.Queries, q => q.Soql!.Contains("FROM Task", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Opportunity_tasks_filter_on_what_id()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = r => FakeSalesforce.Json(r.Soql!.Contains("FROM Opportunity", StringComparison.Ordinal)
            ? FakeSalesforce.QueryResult(OkaforAdvisory)
            : FakeSalesforce.QueryResult(FlowTask));

        var tasks = await sf.Provider.GetOpportunityTasksAsync(OpportunityId, CancellationToken.None);

        Assert.Equal("Proposal review", Assert.Single(tasks!).Automation);
        Assert.Contains($"FROM Task WHERE WhatId = '{OpportunityId}' AND {HouseholdTask}", sf.Http.Queries.Last().Soql);
    }

    [Fact]
    public async Task A_salesforce_error_on_tasks_is_an_upstream_failure()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.Error("INVALID_TYPE", "sObject type 'Task' is not supported"), HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<AdvisorFlow.Application.Abstractions.CrmUpstreamException>(
            () => sf.Provider.GetTasksAsync(Query(ActivityStates.Open), CancellationToken.None));
    }
}
