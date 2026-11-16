using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdvisorFlow.Application.Activities;
using AdvisorFlow.Application.Households;

namespace AdvisorFlow.Api.Tests;

/// <summary>Task endpoints over HTTP (M5, contract v1.4) with the mock provider. Read-only.</summary>
public class ActivityApiTests(MockApiFactory factory) : IClassFixture<MockApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Default_list_is_open_tasks_overdue_first()
    {
        var page = await _client.GetFromJsonAsync<PagedResponse<ActivityDto>>("/api/activities");

        Assert.NotNull(page);
        Assert.Equal(6, page.TotalCount);
        Assert.All(page.Items, t => Assert.False(t.IsClosed));
        Assert.True(page.Items[0].IsOverdue);
    }

    [Theory]
    [InlineData("overdue", 2)]
    [InlineData("closed", 2)]
    [InlineData("all", 8)]
    [InlineData("ALL", 8)]
    public async Task State_filters_the_list(string state, int expected)
    {
        var page = await _client.GetFromJsonAsync<PagedResponse<ActivityDto>>($"/api/activities?state={state}");
        Assert.Equal(expected, page!.TotalCount);
    }

    [Fact]
    public async Task Paging_and_household_filter_work()
    {
        var page = await _client.GetFromJsonAsync<PagedResponse<ActivityDto>>(
            "/api/activities?state=all&householdId=001MOCK00000000002&pageSize=2&page=2");

        Assert.Equal(3, page!.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.All(page.Items, t => Assert.Equal("Okafor Household", t.HouseholdName));
    }

    [Theory]
    [InlineData("/api/activities?state=later", "state")]
    [InlineData("/api/activities?householdId=nope", "householdId")]
    [InlineData("/api/activities?pageSize=500", "pageSize")]
    [InlineData("/api/households/bad-id/activities", "id")]
    [InlineData("/api/opportunities/bad-id/activities", "id")]
    public async Task Invalid_requests_are_400_with_the_field(string url, string field)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Theory]
    [InlineData("/api/households/001MOCK00000000099/activities")]
    [InlineData("/api/opportunities/006MOCK00000000099/activities")]
    public async Task Unknown_records_are_404(string url)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Household_and_opportunity_tasks_are_listed()
    {
        var household = await _client.GetFromJsonAsync<List<ActivityDto>>("/api/households/001MOCK00000000002/activities");
        Assert.Equal(3, household!.Count);

        var empty = await _client.GetFromJsonAsync<List<ActivityDto>>("/api/households/001MOCK00000000007/activities");
        Assert.Empty(empty!);

        var opportunity = await _client.GetFromJsonAsync<List<ActivityDto>>("/api/opportunities/006MOCK00000000002/activities");
        Assert.Equal("Proposal review", Assert.Single(opportunity!).Automation);
    }
}
