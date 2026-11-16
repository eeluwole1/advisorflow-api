using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Application.Opportunities;

namespace AdvisorFlow.Api.Tests;

/// <summary>
/// Opportunity endpoints over HTTP (M4, contract v1.3) with the mock provider. This class has
/// its own <see cref="MockApiFactory"/>; tests that change data create the records they change.
/// </summary>
public class OpportunityApiTests(MockApiFactory factory) : IClassFixture<MockApiFactory>
{
    private const string Martin = "001MOCK00000000007";
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<OpportunityDto> CreateAsync(object body, string householdId = Martin)
    {
        var response = await _client.PostAsJsonAsync($"/api/households/{householdId}/opportunities", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OpportunityDto>())!;
    }

    private static async Task<string[]> ErrorFields(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).Order().ToArray();
    }

    [Fact]
    public async Task Pipeline_lists_opportunities_with_paging()
    {
        var page = await _client.GetFromJsonAsync<PagedResponse<OpportunityDto>>("/api/opportunities?pageSize=3");

        Assert.NotNull(page);
        Assert.Equal(3, page.Items.Count);
        Assert.True(page.TotalCount >= 7);
        Assert.Equal(page.Items.OrderBy(o => o.CloseDate).ToList(), page.Items.ToList());
    }

    [Fact]
    public async Task Pipeline_filters_by_stage_and_household()
    {
        var won = await _client.GetFromJsonAsync<PagedResponse<OpportunityDto>>("/api/opportunities?stage=closed%20won");
        Assert.All(won!.Items, o => Assert.Equal("Closed Won", o.Stage));

        var okafor = await _client.GetFromJsonAsync<PagedResponse<OpportunityDto>>("/api/opportunities?householdId=001MOCK00000000002");
        Assert.Equal(2, okafor!.TotalCount);
    }

    [Fact]
    public async Task Pipeline_rejects_invalid_filters()
    {
        var response = await _client.GetAsync("/api/opportunities?stage=Won&householdId=x&pageSize=500");

        Assert.Equal(new[] { "householdId", "pageSize", "stage" }, await ErrorFields(response));
    }

    [Fact]
    public async Task Get_returns_an_opportunity_or_404()
    {
        var found = await _client.GetFromJsonAsync<OpportunityDto>("/api/opportunities/006MOCK00000000006");
        Assert.Equal("Closed Lost", found!.Stage);
        Assert.Equal("Timing", found.LossReason);
        Assert.True(found.IsClosed);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/opportunities/006MOCK00000000999")).StatusCode);
        Assert.Equal(new[] { "id" }, await ErrorFields(await _client.GetAsync("/api/opportunities/bad")));
    }

    [Fact]
    public async Task Household_opportunities_are_listed_or_404()
    {
        var okafor = await _client.GetFromJsonAsync<List<OpportunityDto>>("/api/households/001MOCK00000000002/opportunities");
        Assert.Equal(2, okafor!.Count);
        Assert.All(okafor, o => Assert.Equal("Okafor Household", o.HouseholdName));

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/households/001MOCK00000000999/opportunities")).StatusCode);
    }

    [Fact]
    public async Task Post_creates_an_opportunity_with_a_location()
    {
        var response = await _client.PostAsJsonAsync($"/api/households/{Martin}/opportunities", new
        {
            name = "Martin Household - Financial Review",
            closeDate = "2027-03-31",
            amount = 1500,
            serviceType = "Financial Review",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OpportunityDto>();
        Assert.Equal("Prospecting", created!.Stage);
        Assert.Equal(new DateOnly(2027, 3, 31), created.CloseDate);
        Assert.Equal(10, created.Probability);
        Assert.EndsWith($"/api/opportunities/{created.Id}", response.Headers.Location!.ToString());
        Assert.Equal(created, await _client.GetFromJsonAsync<OpportunityDto>(response.Headers.Location));
    }

    [Fact]
    public async Task Post_enforces_required_fields_and_the_loss_reason_rule() // BR-OP-001, BR-OP-002
    {
        var missing = await _client.PostAsJsonAsync($"/api/households/{Martin}/opportunities", new { name = "" });
        Assert.Equal(new[] { "closeDate", "name" }, await ErrorFields(missing));

        var lost = await _client.PostAsJsonAsync($"/api/households/{Martin}/opportunities",
            new { name = "Lost one", stage = "Closed Lost", closeDate = "2026-11-30" });
        Assert.Equal(new[] { "lossReason" }, await ErrorFields(lost));
    }

    [Fact]
    public async Task Post_for_an_unknown_household_returns_404()
    {
        var response = await _client.PostAsJsonAsync("/api/households/001MOCK00000000999/opportunities",
            new { name = "Nobody", closeDate = "2027-01-31" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_follows_the_stage_rule() // BR-OP-004, ADR-010
    {
        var created = await CreateAsync(new { name = "Rule check", closeDate = "2027-01-31" });
        var url = $"/api/opportunities/{created.Id}";

        var forward = await _client.PutAsJsonAsync(url, new { name = "Rule check", stage = "Proposal Presented", closeDate = "2027-01-31" });
        Assert.Equal(HttpStatusCode.OK, forward.StatusCode);

        var tooFar = await _client.PutAsJsonAsync(url, new { name = "Rule check", stage = "Prospecting", closeDate = "2027-01-31" });
        Assert.Equal(new[] { "stage" }, await ErrorFields(tooFar));

        var lost = await _client.PutAsJsonAsync(url, new
        {
            name = "Rule check", stage = "Closed Lost", closeDate = "2027-01-31", lossReason = "Chose another advisor",
        });
        Assert.Equal(HttpStatusCode.OK, lost.StatusCode);
        Assert.True((await lost.Content.ReadFromJsonAsync<OpportunityDto>())!.IsClosed);

        var reopen = await _client.PutAsJsonAsync(url, new { name = "Rule check", stage = "Negotiation/Review", closeDate = "2027-01-31" });
        Assert.Equal(new[] { "stage" }, await ErrorFields(reopen));
    }

    [Fact]
    public async Task Put_requires_a_stage_and_clears_omitted_fields()
    {
        var created = await CreateAsync(new { name = "Clear check", closeDate = "2027-01-31", amount = 900, nextStep = "Call" });
        var url = $"/api/opportunities/{created.Id}";

        Assert.Equal(new[] { "stage" }, await ErrorFields(await _client.PutAsJsonAsync(url, new { name = "Clear check", closeDate = "2027-01-31" })));

        var updated = await _client.PutAsJsonAsync(url, new { name = "Clear check", stage = "Prospecting", closeDate = "2027-01-31" });
        var body = await updated.Content.ReadFromJsonAsync<OpportunityDto>();
        Assert.Null(body!.Amount);
        Assert.Null(body.NextStep);
    }

    [Fact]
    public async Task Put_of_an_unknown_opportunity_returns_404()
    {
        var response = await _client.PutAsJsonAsync("/api/opportunities/006MOCK00000000999",
            new { name = "Ghost", stage = "Prospecting", closeDate = "2027-01-31" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
