using System.Net;
using System.Net.Http.Json;
using AdvisorFlow.Application.Dashboard;

namespace AdvisorFlow.Api.Tests;

/// <summary>The dashboard endpoint over HTTP (M6, contract v1.5) with the mock provider.</summary>
public class DashboardApiTests(MockApiFactory factory) : IClassFixture<MockApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Dashboard_returns_the_seed_totals()
    {
        var summary = await _client.GetFromJsonAsync<DashboardSummaryDto>("/api/dashboard");

        Assert.NotNull(summary);
        Assert.Equal(5, summary.Pipeline.OpenCount);
        Assert.Equal(30500m, summary.Pipeline.OpenFees);
        Assert.Equal(16650m, summary.Pipeline.WeightedFees);
        Assert.Equal(0.5m, summary.Outcomes.WinRate);
        Assert.Equal(6, summary.Activities.Open);
        Assert.Equal(7, summary.Households.Total);
    }

    [Fact]
    public async Task Dashboard_works_on_a_read_only_deployment()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/dashboard")).StatusCode);
    }
}
