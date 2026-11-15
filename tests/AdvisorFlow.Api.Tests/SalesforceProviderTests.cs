using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Households;
using AdvisorFlow.Infrastructure.Crm;
using AdvisorFlow.Infrastructure.Crm.Salesforce;

namespace AdvisorFlow.Api.Tests;

/// <summary>SOQL escaping: request values must never change the shape of a query.</summary>
public class SoqlTests
{
    [Fact]
    public void Quote_escapes_quotes_and_backslashes()
    {
        Assert.Equal(@"'O\'Brien \\ Co'", Soql.Quote(@"O'Brien \ Co"));
    }

    [Fact]
    public void Quote_neutralises_an_injection_attempt()
    {
        Assert.Equal(@"'x\' OR Name != \''", Soql.Quote("x' OR Name != '"));
    }

    [Fact]
    public void LikeContains_matches_wildcards_literally()
    {
        Assert.Equal(@"'%50\%\_off%'", Soql.LikeContains("50%_off"));
    }

    [Fact]
    public void Household_filter_always_limits_to_the_household_record_type()
    {
        Assert.Equal(
            "RecordType.DeveloperName = 'Household'",
            SalesforceCrmDataProvider.BuildHouseholdFilter(new HouseholdQuery(1, 20, null, null)));
    }

    [Fact]
    public void Household_filter_adds_search_and_canonical_status()
    {
        Assert.Equal(
            "RecordType.DeveloperName = 'Household' AND Name LIKE '%Carter%' AND Client_Status__c = 'Active'",
            SalesforceCrmDataProvider.BuildHouseholdFilter(new HouseholdQuery(1, 20, "  Carter ", "active")));
    }
}

/// <summary>The JWT bearer assertion (RFC 7523) that Salesforce verifies with the uploaded certificate.</summary>
public class SalesforceJwtTests
{
    [Fact]
    public void Jwt_has_the_expected_claims_and_a_valid_RS256_signature()
    {
        var now = new DateTimeOffset(2026, 11, 13, 22, 0, 0, TimeSpan.Zero);
        using var key = RSA.Create(2048);

        var jwt = SalesforceJwt.Create("consumer-key", "integration@example.com", "https://login.salesforce.com", now, key);

        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
        Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());

        using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        Assert.Equal("consumer-key", claims.RootElement.GetProperty("iss").GetString());
        Assert.Equal("integration@example.com", claims.RootElement.GetProperty("sub").GetString());
        Assert.Equal("https://login.salesforce.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(now.AddMinutes(3).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());

        var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        Assert.True(key.VerifyData(signed, Base64Url.DecodeFromChars(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }
}

/// <summary>The Salesforce provider against a fake Salesforce: sign-in, queries, mapping and errors.</summary>
public class SalesforceProviderTests
{
    private const string Williams =
        """{"attributes":{"type":"Account"},"Id":"001fj00002Eg1LbAAJ","Name":"Williams Household","Client_Status__c":"Prospect","Client_Risk_Profile__c":"Moderate","CustomerPriority__c":"Medium","Phone":"204-555-0101","AccountNumber":"ADV-0001"}""";

    private const string Carter =
        """{"attributes":{"type":"Account"},"Id":"001fj00002Et3AfAAJ","Name":"Carter Household","Client_Status__c":null,"Client_Risk_Profile__c":null,"CustomerPriority__c":null,"Phone":null,"AccountNumber":null}""";

    private static readonly HouseholdQuery FirstPage = new(1, 20, null, null);

    [Fact]
    public async Task Signs_in_with_a_JWT_bearer_assertion()
    {
        using var sf = new SalesforceHarness();

        await sf.Provider.GetHouseholdsAsync(FirstPage, CancellationToken.None);

        var token = Assert.Single(sf.Http.TokenRequests);
        Assert.Equal(HttpMethod.Post, token.Method);
        Assert.Equal($"{FakeSalesforce.LoginUrl}/services/oauth2/token", token.Uri.ToString());
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer", token.Body);
        Assert.Contains("assertion=", token.Body);
    }

    [Fact]
    public async Task Lists_households_with_count_paging_and_mapping()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = r => FakeSalesforce.Json(r.Soql!.StartsWith("SELECT COUNT()", StringComparison.Ordinal)
            ? FakeSalesforce.Count(2)
            : FakeSalesforce.QueryResult(Carter, Williams));

        var page = await sf.Provider.GetHouseholdsAsync(FirstPage, CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        var carter = page.Items[0];
        Assert.Equal("Carter Household", carter.Name);
        Assert.Equal("Prospect", carter.ClientStatus);
        Assert.Null(carter.RiskProfile);
        var williams = page.Items[1];
        Assert.Equal("001fj00002Eg1LbAAJ", williams.Id);
        Assert.Equal("Moderate", williams.RiskProfile);
        Assert.Equal("Medium", williams.Priority);
        Assert.Equal("ADV-0001", williams.AccountNumber);

        var queries = sf.Http.Queries.Select(q => q.Soql!).ToList();
        Assert.Equal(2, queries.Count);
        Assert.Equal("SELECT COUNT() FROM Account WHERE RecordType.DeveloperName = 'Household'", queries[0]);
        Assert.Contains("WHERE RecordType.DeveloperName = 'Household' ORDER BY Name ASC, Id ASC LIMIT 20 OFFSET 0", queries[1]);
        Assert.All(sf.Http.Queries, q => Assert.Equal("Bearer token-1", q.Authorization));
        Assert.All(sf.Http.Queries, q => Assert.Equal($"{FakeSalesforce.InstanceUrl}/services/data/v68.0/query", q.Uri.GetLeftPart(UriPartial.Path)));
    }

    [Fact]
    public async Task Skips_the_page_query_when_the_page_is_past_the_end()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.Count(2));

        var page = await sf.Provider.GetHouseholdsAsync(new HouseholdQuery(3, 20, null, null), CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(2, page.TotalCount);
        Assert.Single(sf.Http.Queries);
    }

    [Fact]
    public async Task Reuses_the_access_token_until_it_expires()
    {
        using var sf = new SalesforceHarness();

        await sf.Provider.GetHouseholdsAsync(FirstPage, CancellationToken.None);
        await sf.Provider.GetHouseholdsAsync(FirstPage, CancellationToken.None);
        Assert.Single(sf.Http.TokenRequests);

        sf.Clock.Now = sf.Clock.Now.AddMinutes(31);
        await sf.Provider.GetHouseholdsAsync(FirstPage, CancellationToken.None);
        Assert.Equal(2, sf.Http.TokenRequests.Count());
    }

    [Fact]
    public async Task Signs_in_again_once_when_Salesforce_rejects_the_token()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = r => r.Authorization == "Bearer token-1"
            ? FakeSalesforce.Json(FakeSalesforce.Error("INVALID_SESSION_ID", "Session expired or invalid"), HttpStatusCode.Unauthorized)
            : FakeSalesforce.Json(FakeSalesforce.Count(0));

        var page = await sf.Provider.GetHouseholdsAsync(FirstPage, CancellationToken.None);

        Assert.Equal(0, page.TotalCount);
        Assert.Equal(2, sf.Http.TokenRequests.Count());
    }

    [Fact]
    public async Task A_rejected_token_request_is_a_503()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnToken = _ => FakeSalesforce.Json(
            """{"error":"invalid_grant","error_description":"user hasn't approved this consumer"}""", HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<CrmUnavailableException>(() => sf.Provider.GetHouseholdsAsync(FirstPage, CancellationToken.None));
        Assert.Empty(sf.Http.Queries);
    }

    [Fact]
    public async Task Missing_configuration_is_a_503_without_calling_Salesforce()
    {
        using var sf = new SalesforceHarness(o => o.ClientId = "");

        await Assert.ThrowsAsync<CrmUnavailableException>(() => sf.Provider.GetHouseholdsAsync(FirstPage, CancellationToken.None));
        Assert.Empty(sf.Http.Requests);
    }

    [Fact]
    public async Task A_missing_private_key_file_is_a_503()
    {
        using var sf = new SalesforceHarness(o => o.PrivateKeyReference = Path.Combine(Path.GetTempPath(), "does-not-exist.key"));

        await Assert.ThrowsAsync<CrmUnavailableException>(() => sf.Provider.GetHouseholdsAsync(FirstPage, CancellationToken.None));
        Assert.Empty(sf.Http.Requests);
    }

    [Fact]
    public async Task A_Salesforce_server_error_is_a_503()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.Error("SERVER_UNAVAILABLE", "down"), HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<CrmUnavailableException>(() => sf.Provider.GetHouseholdsAsync(FirstPage, CancellationToken.None));
    }

    [Fact]
    public async Task A_rejected_query_is_a_502()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.Error("INVALID_FIELD", "No such column"), HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<CrmUpstreamException>(() => sf.Provider.GetHouseholdsAsync(FirstPage, CancellationToken.None));
    }

    [Fact]
    public async Task Unreadable_JSON_is_a_502()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json("<html>maintenance</html>");

        await Assert.ThrowsAsync<CrmUpstreamException>(() => sf.Provider.GetHouseholdsAsync(FirstPage, CancellationToken.None));
    }

    [Fact]
    public async Task Gets_one_household_by_id_with_the_record_type_filter()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.QueryResult(Williams));

        var household = await sf.Provider.GetHouseholdAsync("001fj00002Eg1LbAAJ", CancellationToken.None);

        Assert.NotNull(household);
        Assert.Equal("Williams Household", household.Name);
        Assert.Equal(
            "SELECT Id, Name, Client_Status__c, Client_Risk_Profile__c, CustomerPriority__c, Phone, AccountNumber FROM Account " +
            "WHERE Id = '001fj00002Eg1LbAAJ' AND RecordType.DeveloperName = 'Household' LIMIT 1",
            Assert.Single(sf.Http.Queries).Soql);
    }

    [Fact]
    public async Task An_id_Salesforce_does_not_recognise_is_not_found()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(
            FakeSalesforce.Error("INVALID_QUERY_FILTER_OPERATOR", "invalid ID field: 001MOCK00000000001"), HttpStatusCode.BadRequest);

        Assert.Null(await sf.Provider.GetHouseholdAsync("001MOCK00000000001", CancellationToken.None));
        Assert.Null(await sf.Provider.GetMembersAsync("001MOCK00000000001", CancellationToken.None));
    }

    [Fact]
    public async Task Groups_members_by_household_in_one_query()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.QueryResult(
            """{"Id":"003fj00001v2tLdAAI","AccountId":"001fj00002Et3AfAAJ","FirstName":"Daniel","LastName":"Carter","Email":null,"Phone":"204-555-0102","Preferred_Contact_Method__c":null}""",
            """{"Id":"003fj00001v8tUDAAY","AccountId":"001fj00002Eg1LbAAJ","FirstName":null,"LastName":"Williams","Email":"james.williams@example.com","Phone":"204-555-0101","Preferred_Contact_Method__c":"Phone","Is_Primary_Member__c":true}"""));

        var members = await sf.Provider.GetMembersForHouseholdsAsync(
            ["001fj00002Eg1LbAAJ", "001fj00002Et3AfAAJ", "001fj00002EmptyAAA"], CancellationToken.None);

        var query = Assert.Single(sf.Http.Queries).Soql!;
        Assert.Contains("FROM Contact WHERE AccountId IN ('001fj00002Eg1LbAAJ', '001fj00002Et3AfAAJ', '001fj00002EmptyAAA')", query);
        Assert.Contains("Is_Primary_Member__c", query);
        Assert.Contains("ORDER BY Is_Primary_Member__c DESC", query);
        Assert.Equal("Daniel", Assert.Single(members["001fj00002Et3AfAAJ"]).FirstName);
        var williams = Assert.Single(members["001fj00002Eg1LbAAJ"]);
        Assert.Equal(string.Empty, williams.FirstName);
        Assert.Equal("Phone", williams.PreferredContactMethod);
        Assert.True(williams.IsPrimary);
        Assert.False(members["001fj00002Et3AfAAJ"][0].IsPrimary); // field missing from the record → false
        Assert.Empty(members["001fj00002EmptyAAA"]);
    }

    [Fact]
    public async Task No_household_ids_means_no_query()
    {
        using var sf = new SalesforceHarness();

        var members = await sf.Provider.GetMembersForHouseholdsAsync([], CancellationToken.None);

        Assert.Empty(members);
        Assert.Empty(sf.Http.Requests);
    }

    private static readonly HouseholdDraft Draft = new("Okafor Household", "Active", "Conservative", null, "204-555-0199");

    private const string NewAccount =
        """{"Id":"001fj00002NewAcAAA","Name":"Okafor Household","Client_Status__c":"Active","Client_Risk_Profile__c":"Conservative","CustomerPriority__c":null,"Phone":"204-555-0199","AccountNumber":null}""";

    private static HttpResponseMessage AnswerCreateQueries(FakeRequest r) => FakeSalesforce.Json(
        r.Soql!.Contains("FROM RecordType", StringComparison.Ordinal)
            ? FakeSalesforce.QueryResult("""{"Id":"012fj000009BFeTAAW"}""")
            : FakeSalesforce.QueryResult(NewAccount));

    [Fact]
    public async Task Creates_a_household_Account_with_the_Household_record_type()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = AnswerCreateQueries;

        var created = await sf.Provider.CreateHouseholdAsync(Draft, CancellationToken.None);

        Assert.Equal(FakeSalesforce.NewAccountId, created.Id);
        Assert.Equal("Okafor Household", created.Name);
        var write = Assert.Single(sf.Http.Writes);
        Assert.Equal(HttpMethod.Post, write.Method);
        Assert.Equal($"{FakeSalesforce.InstanceUrl}/services/data/v68.0/sobjects/Account", write.Uri.ToString());
        using var body = JsonDocument.Parse(write.Body);
        Assert.Equal("012fj000009BFeTAAW", body.RootElement.GetProperty("RecordTypeId").GetString());
        Assert.Equal("Okafor Household", body.RootElement.GetProperty("Name").GetString());
        Assert.Equal("Active", body.RootElement.GetProperty("Client_Status__c").GetString());
        Assert.Equal("Conservative", body.RootElement.GetProperty("Client_Risk_Profile__c").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("CustomerPriority__c").ValueKind);
        Assert.Equal("204-555-0199", body.RootElement.GetProperty("Phone").GetString());
    }

    [Fact]
    public async Task Looks_up_the_Household_record_type_only_once()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = AnswerCreateQueries;

        await sf.Provider.CreateHouseholdAsync(Draft, CancellationToken.None);
        await sf.Provider.CreateHouseholdAsync(Draft, CancellationToken.None);

        Assert.Single(sf.Http.Queries, q => q.Soql!.Contains("FROM RecordType", StringComparison.Ordinal));
        Assert.Equal(2, sf.Http.Writes.Count());
    }

    [Fact]
    public async Task Updates_a_household_and_sends_nulls_to_clear_fields()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.QueryResult(Williams));
        var draft = new HouseholdDraft("Williams Household", "Inactive", null, null, null);

        var updated = await sf.Provider.UpdateHouseholdAsync("001fj00002Eg1LbAAJ", draft, CancellationToken.None);

        Assert.NotNull(updated);
        var write = Assert.Single(sf.Http.Writes);
        Assert.Equal(HttpMethod.Patch, write.Method);
        Assert.Equal($"{FakeSalesforce.InstanceUrl}/services/data/v68.0/sobjects/Account/001fj00002Eg1LbAAJ", write.Uri.ToString());
        using var body = JsonDocument.Parse(write.Body);
        Assert.Equal("Inactive", body.RootElement.GetProperty("Client_Status__c").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("Client_Risk_Profile__c").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("Phone").ValueKind);
        Assert.False(body.RootElement.TryGetProperty("RecordTypeId", out _));
    }

    [Fact]
    public async Task Updating_something_that_is_not_a_household_changes_nothing()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.QueryResult()); // not found as a Household

        var updated = await sf.Provider.UpdateHouseholdAsync("001fj00002OtherAAA", Draft, CancellationToken.None);

        Assert.Null(updated);
        Assert.Empty(sf.Http.Writes);
    }

    [Fact]
    public async Task A_write_Salesforce_rejects_is_a_502()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = AnswerCreateQueries;
        sf.Http.OnWrite = _ => FakeSalesforce.Json(
            FakeSalesforce.Error("FIELD_CUSTOM_VALIDATION_EXCEPTION", "rejected"), HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<CrmUpstreamException>(() => sf.Provider.CreateHouseholdAsync(Draft, CancellationToken.None));
    }

    private const string WilliamsId = "001fj00002Eg1LbAAJ";
    private const string JamesId = "003fj00001v8tUDAAY";
    private const string SarahId = "003fj00001SarahAAA";

    private static string ContactJson(string id, string first, bool primary) =>
        $$"""{"Id":"{{id}}","AccountId":"{{WilliamsId}}","FirstName":"{{first}}","LastName":"Williams","Email":null,"Phone":null,"Preferred_Contact_Method__c":null,"Is_Primary_Member__c":{{(primary ? "true" : "false")}}}""";

    /// <summary>Answers "current primaries" with James, and member read-backs with the requested contact.</summary>
    private static HttpResponseMessage AnswerMemberQueries(FakeRequest r)
    {
        var soql = r.Soql!;
        if (soql.StartsWith("SELECT Id FROM Contact", StringComparison.Ordinal))
        {
            return FakeSalesforce.Json(FakeSalesforce.QueryResult($$"""{"Id":"{{JamesId}}"}"""));
        }

        var id = soql.Contains(FakeSalesforce.NewContactId, StringComparison.Ordinal) ? FakeSalesforce.NewContactId
            : soql.Contains(SarahId, StringComparison.Ordinal) ? SarahId
            : JamesId;
        return FakeSalesforce.Json(FakeSalesforce.QueryResult(ContactJson(id, id == JamesId ? "James" : "Ava", primary: id != JamesId)));
    }

    private static JsonElement[] Steps(JsonDocument composite) =>
        composite.RootElement.GetProperty("compositeRequest").EnumerateArray().ToArray();

    [Fact]
    public async Task Adds_a_non_primary_member_in_one_composite_step()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = AnswerMemberQueries;
        var draft = new MemberDraft("Ava", "Williams", "ava@example.com", null, "Email", IsPrimary: false);

        var member = await sf.Provider.CreateMemberAsync(WilliamsId, draft, CancellationToken.None);

        Assert.Equal(FakeSalesforce.NewContactId, member.Id);
        Assert.DoesNotContain(sf.Http.Queries, q => q.Soql!.Contains("Is_Primary_Member__c = true", StringComparison.Ordinal));
        using var composite = JsonDocument.Parse(Assert.Single(sf.Http.Composites).Body);
        Assert.True(composite.RootElement.GetProperty("allOrNone").GetBoolean());
        var step = Assert.Single(Steps(composite));
        Assert.Equal("POST", step.GetProperty("method").GetString());
        Assert.Equal("/services/data/v68.0/sobjects/Contact", step.GetProperty("url").GetString());
        var body = step.GetProperty("body");
        Assert.Equal(WilliamsId, body.GetProperty("AccountId").GetString());
        Assert.Equal("ava@example.com", body.GetProperty("Email").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("Phone").ValueKind);
        Assert.False(body.GetProperty("Is_Primary_Member__c").GetBoolean());
    }

    [Fact]
    public async Task Adding_a_primary_member_clears_the_old_primary_in_the_same_transaction() // BR-HH-003
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = AnswerMemberQueries;

        await sf.Provider.CreateMemberAsync(WilliamsId, new MemberDraft("Ava", "Williams", null, null, null, IsPrimary: true), CancellationToken.None);

        using var composite = JsonDocument.Parse(Assert.Single(sf.Http.Composites).Body);
        var steps = Steps(composite);
        Assert.Equal(2, steps.Length);
        Assert.Equal("PATCH", steps[0].GetProperty("method").GetString());
        Assert.Equal($"/services/data/v68.0/sobjects/Contact/{JamesId}", steps[0].GetProperty("url").GetString());
        Assert.False(steps[0].GetProperty("body").GetProperty("Is_Primary_Member__c").GetBoolean());
        Assert.Equal("POST", steps[1].GetProperty("method").GetString());
        Assert.True(steps[1].GetProperty("body").GetProperty("Is_Primary_Member__c").GetBoolean());
    }

    [Fact]
    public async Task Making_a_member_primary_patches_it_and_clears_only_the_others() // BR-HH-003
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = AnswerMemberQueries;

        var updated = await sf.Provider.UpdateMemberAsync(
            WilliamsId, SarahId, new MemberDraft("Sarah", "Williams", null, null, null, IsPrimary: true), CancellationToken.None);

        Assert.NotNull(updated);
        using var composite = JsonDocument.Parse(Assert.Single(sf.Http.Composites).Body);
        var steps = Steps(composite);
        Assert.Equal(2, steps.Length);
        Assert.Equal($"/services/data/v68.0/sobjects/Contact/{JamesId}", steps[0].GetProperty("url").GetString());
        Assert.Equal($"/services/data/v68.0/sobjects/Contact/{SarahId}", steps[1].GetProperty("url").GetString());
        Assert.False(steps[1].GetProperty("body").TryGetProperty("AccountId", out _)); // members are not moved
        Assert.Contains(sf.Http.Queries, q => q.Soql!.Contains($"AccountId = '{WilliamsId}'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Updating_a_contact_outside_the_household_changes_nothing()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = _ => FakeSalesforce.Json(FakeSalesforce.QueryResult());

        var updated = await sf.Provider.UpdateMemberAsync(
            WilliamsId, SarahId, new MemberDraft(null, "Williams", null, null, null, IsPrimary: false), CancellationToken.None);

        Assert.Null(updated);
        Assert.Empty(sf.Http.Composites);
    }

    [Fact]
    public async Task A_failed_composite_step_means_nothing_was_saved_and_is_a_502()
    {
        using var sf = new SalesforceHarness();
        sf.Http.OnQuery = AnswerMemberQueries;
        sf.Http.OnWrite = _ => FakeSalesforce.Json("""
            {"compositeResponse":[
              {"body":[{"errorCode":"PROCESSING_HALTED","message":"The transaction was rolled back"}],"httpHeaders":{},"httpStatusCode":400,"referenceId":"clearPrimary0"},
              {"body":[{"errorCode":"INVALID_EMAIL_ADDRESS","message":"Email: invalid"}],"httpHeaders":{},"httpStatusCode":400,"referenceId":"member"}]}
            """);

        await Assert.ThrowsAsync<CrmUpstreamException>(() => sf.Provider.CreateMemberAsync(
            WilliamsId, new MemberDraft("Ava", "Williams", null, null, null, IsPrimary: true), CancellationToken.None));
    }
}
