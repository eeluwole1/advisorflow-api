using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AdvisorFlow.Api.Tests;

/// <summary>
/// Guards the versioned contract shared with advisorflow-web (docs/api/openapi.v1.yaml).
/// Each response's JSON property names must equal the schema's required list, so a
/// renamed, removed or undocumented field fails the build before the frontend breaks.
/// </summary>
public class ContractTests(MockApiFactory factory) : IClassFixture<MockApiFactory>
{
    private static readonly string Contract = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "openapi.v1.yaml"))
        .ReplaceLineEndings("\n"); // Windows checkouts may use CRLF; the regexes expect LF.
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public void Contract_declares_version_1()
    {
        Assert.Matches(@"(?m)^  version: 1\.\d+\.\d+$", Contract);
    }

    [Fact]
    public async Task Household_list_matches_contract()
    {
        using var page = await GetJson("/api/households?pageSize=2");
        AssertMatchesSchema(page.RootElement, "HouseholdSummaryPage");
        AssertMatchesSchema(page.RootElement.GetProperty("items")[0], "HouseholdSummary");
    }

    [Fact]
    public async Task Household_detail_matches_contract()
    {
        using var detail = await GetJson("/api/households/001MOCK00000000001");
        AssertMatchesSchema(detail.RootElement, "HouseholdDetail");
    }

    [Fact]
    public async Task Household_members_match_contract()
    {
        using var members = await GetJson("/api/households/001MOCK00000000001/contacts");
        AssertMatchesSchema(members.RootElement[0], "HouseholdMember");
    }

    [Fact]
    public async Task Household_create_and_update_responses_match_contract() // v1.1
    {
        var created = await _client.PostAsJsonAsync("/api/households", new { name = "Contract Household" });
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        AssertMatchesSchema(createdBody.RootElement, "HouseholdDetail");

        var id = createdBody.RootElement.GetProperty("id").GetString();
        var updated = await _client.PutAsJsonAsync($"/api/households/{id}", new { name = "Contract Household", clientStatus = "Active" });
        updated.EnsureSuccessStatusCode();
        using var updatedBody = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        AssertMatchesSchema(updatedBody.RootElement, "HouseholdDetail");
    }

    [Fact]
    public async Task Member_create_and_update_responses_match_contract() // v1.2
    {
        var created = await _client.PostAsJsonAsync("/api/households/001MOCK00000000007/contacts", new { lastName = "Martin" });
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        AssertMatchesSchema(createdBody.RootElement, "HouseholdMember");

        var id = createdBody.RootElement.GetProperty("id").GetString();
        var updated = await _client.PutAsJsonAsync(
            $"/api/households/001MOCK00000000007/contacts/{id}", new { firstName = "Luc", lastName = "Martin", isPrimary = true });
        updated.EnsureSuccessStatusCode();
        using var updatedBody = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        AssertMatchesSchema(updatedBody.RootElement, "HouseholdMember");
    }

    [Fact]
    public async Task Opportunity_reads_match_contract() // v1.3
    {
        using var page = await GetJson("/api/opportunities?pageSize=2");
        AssertMatchesSchema(page.RootElement, "OpportunityPage");
        AssertMatchesSchema(page.RootElement.GetProperty("items")[0], "Opportunity");

        using var detail = await GetJson("/api/opportunities/006MOCK00000000002");
        AssertMatchesSchema(detail.RootElement, "Opportunity");

        using var household = await GetJson("/api/households/001MOCK00000000002/opportunities");
        AssertMatchesSchema(household.RootElement[0], "Opportunity");
    }

    [Fact]
    public async Task Opportunity_create_and_update_responses_match_contract() // v1.3
    {
        var created = await _client.PostAsJsonAsync(
            "/api/households/001MOCK00000000007/opportunities", new { name = "Contract Opportunity", closeDate = "2027-01-31" });
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        AssertMatchesSchema(createdBody.RootElement, "Opportunity");
        Assert.Equal("2027-01-31", createdBody.RootElement.GetProperty("closeDate").GetString());

        var id = createdBody.RootElement.GetProperty("id").GetString();
        var updated = await _client.PutAsJsonAsync(
            $"/api/opportunities/{id}", new { name = "Contract Opportunity", stage = "Qualification", closeDate = "2027-01-31" });
        updated.EnsureSuccessStatusCode();
        using var updatedBody = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        AssertMatchesSchema(updatedBody.RootElement, "Opportunity");
    }

    private async Task<JsonDocument> GetJson(string url)
    {
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static void AssertMatchesSchema(JsonElement element, string schema)
    {
        var expected = RequiredFields(schema);
        var actual = element.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(expected, actual);
    }

    private static string[] RequiredFields(string schema)
    {
        var match = Regex.Match(Contract, $@"(?m)^    {schema}:\r?\n      type: object\r?\n      required: \[(?<fields>[^\]]+)\]");
        Assert.True(match.Success, $"Schema '{schema}' with a required list was not found in openapi.v1.yaml.");
        return match.Groups["fields"].Value.Split(',', StringSplitOptions.TrimEntries).Order().ToArray();
    }
}
