using System.Net;
using System.Text.Json;
using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Application.Opportunities;
using AdvisorFlow.Domain.Opportunities;
using AdvisorFlow.Infrastructure.Crm;

namespace AdvisorFlow.Api.Tests;

/// <summary>The Salesforce provider's opportunity reads and writes (M4) against a fake Salesforce.</summary>
public class SalesforceOpportunityTests
{
    private const string AdvisoryFilter =
        "RecordType.DeveloperName = 'Advisory_Engagement' AND Account.RecordType.DeveloperName = 'Household'";

    private const string OkaforId = "001fj00002FHXAnAAP";
    private const string OpportunityId = "006fj00000AbCdEAAA";

    private const string OkaforAccount =
        """{"attributes":{"type":"Account"},"Id":"001fj00002FHXAnAAP","Name":"Okafor Household","Client_Status__c":"Active","Client_Risk_Profile__c":null,"CustomerPriority__c":"High","Phone":null,"AccountNumber":null}""";

    private const string OkaforAdvisory =
        """{"attributes":{"type":"Opportunity"},"Id":"006fj00000AbCdEAAA","AccountId":"001fj00002FHXAnAAP","Account":{"attributes":{"type":"Account"},"Name":"Okafor Household"},"Name":"Okafor Household - Investment Advisory","StageName":"Proposal Presented","CloseDate":"2026-12-01","Amount":12000.0,"Probability":60.0,"Service_Type__c":"Investment Advisory","Engagement_Priority__c":"Medium","NextStep":"Follow up","Loss_Reason__c":null}""";

    private static readonly OpportunityDraft Draft = new(
        "Okafor Household - Investment Advisory", "Proposal Presented", new DateOnly(2026, 12, 1), 12000m,
        "Investment Advisory", "Medium", "Follow up", null);

    [Fact]
    public void Pipeline_filter_limits_to_advisory_opportunities_on_households()
    {
        Assert.Equal(
            AdvisoryFilter,
            SalesforceCrmDataProvider.BuildOpportunityFilter(new OpportunityQuery(1, 20, null, null, null)));
    }

    [Fact]
    public void Pipeline_filter_escapes_search_stage_and_household()
    {
        Assert.Equal(
            $"{AdvisoryFilter} AND Name LIKE '%O\\'Brien%' AND StageName = 'Closed Won' AND AccountId = '{OkaforId}'",
            SalesforceCrmDataProvider.BuildOpportunityFilter(new OpportunityQuery(1, 20, " O'Brien ", "Closed Won", OkaforId)));
    }

    [Fact]
    public async Task Lists_the_pipeline_with_count_paging_and_mapping()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = r => FakeSalesforce.Json(r.Soql!.StartsWith("SELECT COUNT()", StringComparison.Ordinal)
            ? FakeSalesforce.Count(1)
            : FakeSalesforce.QueryResult(OkaforAdvisory));

        var page = await sf.Provider.GetOpportunitiesAsync(new OpportunityQuery(1, 20, null, null, null), CancellationToken.None);

        Assert.Equal(1, page.TotalCount);
        var o = Assert.Single(page.Items);
        Assert.Equal(
            new Opportunity(OpportunityId, OkaforId, "Okafor Household", "Okafor Household - Investment Advisory",
                "Proposal Presented", new DateOnly(2026, 12, 1), 12000m, 60, "Investment Advisory", "Medium", "Follow up", null),
            o);

        var queries = sf.Http.Queries.Select(q => q.Soql!).ToList();
        Assert.Equal($"SELECT COUNT() FROM Opportunity WHERE {AdvisoryFilter}", queries[0]);
        Assert.EndsWith($"WHERE {AdvisoryFilter} ORDER BY CloseDate ASC, Name ASC, Id ASC LIMIT 20 OFFSET 0", queries[1]);
        Assert.Contains("Account.Name", queries[1]);
    }

    [Fact]
    public async Task An_invalid_id_reads_as_not_found()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.Error("MALFORMED_ID", "bad id"), HttpStatusCode.BadRequest);

        Assert.Null(await sf.Provider.GetOpportunityAsync("006MOCK00000000001", CancellationToken.None));
    }

    [Fact]
    public async Task Household_opportunities_check_the_household_first()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = r => FakeSalesforce.Json(r.Soql!.Contains("FROM Account", StringComparison.Ordinal)
            ? FakeSalesforce.QueryResult()
            : FakeSalesforce.QueryResult(OkaforAdvisory));

        Assert.Null(await sf.Provider.GetHouseholdOpportunitiesAsync(OkaforId, CancellationToken.None));
        Assert.DoesNotContain(sf.Http.Queries, q => q.Soql!.Contains("FROM Opportunity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Household_opportunities_are_queried_by_account()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = r => FakeSalesforce.Json(r.Soql!.Contains("FROM Account", StringComparison.Ordinal)
            ? FakeSalesforce.QueryResult(OkaforAccount)
            : FakeSalesforce.QueryResult(OkaforAdvisory));

        var list = await sf.Provider.GetHouseholdOpportunitiesAsync(OkaforId, CancellationToken.None);

        Assert.Equal(OpportunityId, Assert.Single(list!).Id);
        Assert.Contains(sf.Http.Queries, q => q.Soql!.Contains($"WHERE AccountId = '{OkaforId}' AND {AdvisoryFilter}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_sets_household_record_type_and_fields()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = r => FakeSalesforce.Json(r.Soql!.Contains("FROM RecordType", StringComparison.Ordinal)
            ? FakeSalesforce.QueryResult("""{"Id":"012fj000000AdvEAAA"}""")
            : FakeSalesforce.QueryResult(OkaforAdvisory));

        var created = await sf.Provider.CreateOpportunityAsync(OkaforId, Draft, CancellationToken.None);

        Assert.Equal(OpportunityId, created.Id);
        var write = Assert.Single(sf.Http.Writes);
        Assert.Equal(HttpMethod.Post, write.Method);
        Assert.EndsWith("/sobjects/Opportunity", write.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(write.Body);
        var fields = body.RootElement;
        Assert.Equal(OkaforId, fields.GetProperty("AccountId").GetString());
        Assert.Equal("012fj000000AdvEAAA", fields.GetProperty("RecordTypeId").GetString());
        Assert.Equal("Proposal Presented", fields.GetProperty("StageName").GetString());
        Assert.Equal("2026-12-01", fields.GetProperty("CloseDate").GetString());
        Assert.Equal(12000m, fields.GetProperty("Amount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, fields.GetProperty("Loss_Reason__c").ValueKind);
        Assert.False(fields.TryGetProperty("Probability", out _));
        Assert.Contains(sf.Http.Queries, q => q.Soql!.Contains("SobjectType = 'Opportunity' AND DeveloperName = 'Advisory_Engagement'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_patches_only_an_existing_advisory_opportunity()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.QueryResult());

        Assert.Null(await sf.Provider.UpdateOpportunityAsync(OpportunityId, Draft, CancellationToken.None));
        Assert.Empty(sf.Http.Writes);

        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.QueryResult(OkaforAdvisory));
        var updated = await sf.Provider.UpdateOpportunityAsync(OpportunityId, Draft, CancellationToken.None);

        Assert.NotNull(updated);
        var write = Assert.Single(sf.Http.Writes);
        Assert.Equal(HttpMethod.Patch, write.Method);
        Assert.EndsWith($"/sobjects/Opportunity/{OpportunityId}", write.Uri.AbsolutePath);
        Assert.DoesNotContain("AccountId", write.Body);
    }

    [Fact]
    public async Task A_Salesforce_validation_failure_is_a_gateway_error()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.QueryResult(OkaforAdvisory));
        sf.Http.OnWrite = _ => FakeSalesforce.Json(
            FakeSalesforce.Error("FIELD_CUSTOM_VALIDATION_EXCEPTION", "Select a Loss Reason"), HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<CrmUpstreamException>(() =>
            sf.Provider.UpdateOpportunityAsync(OpportunityId, Draft, CancellationToken.None));
    }
}
