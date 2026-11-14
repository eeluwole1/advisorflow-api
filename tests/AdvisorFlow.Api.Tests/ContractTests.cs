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
