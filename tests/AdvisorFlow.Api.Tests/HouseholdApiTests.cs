using System.Net;
using System.Net.Http.Json;
using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Households;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AdvisorFlow.Api.Tests;

/// <summary>End-to-end HTTP tests against the API hosted in memory with the mock provider.</summary>
public class HouseholdApiTests(MockApiFactory factory) : IClassFixture<MockApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_returns_200()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task List_returns_paged_households()
    {
        var page = await _client.GetFromJsonAsync<PagedResponse<HouseholdSummaryDto>>("/api/households?pageSize=5");

        Assert.NotNull(page);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(7, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
    }

    [Theory]
    [InlineData("/api/households?page=0")]
    [InlineData("/api/households?pageSize=500")]
    [InlineData("/api/households?status=VIP")]
    [InlineData("/api/households/not-an-id")]
    [InlineData("/api/households/not-an-id/contacts")]
    public async Task Invalid_requests_return_400(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_returns_household_detail()
    {
        var household = await _client.GetFromJsonAsync<HouseholdDetailDto>("/api/households/001MOCK00000000001");

        Assert.NotNull(household);
        Assert.Equal("Williams Household", household.Name);
        Assert.Equal(2, household.MemberCount);
    }

    [Theory]
    [InlineData("/api/households/001MOCK99999999999")]
    [InlineData("/api/households/001MOCK99999999999/contacts")]
    public async Task Unknown_household_returns_404(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Contacts_returns_members()
    {
        var members = await _client.GetFromJsonAsync<List<HouseholdMemberDto>>("/api/households/001MOCK00000000001/contacts");

        Assert.NotNull(members);
        Assert.Equal(["James Williams", "Sarah Williams"], members.Select(m => m.FullName));
    }

    [Theory]
    [InlineData(typeof(CrmUnavailableException), HttpStatusCode.ServiceUnavailable)]
    [InlineData(typeof(CrmUpstreamException), HttpStatusCode.BadGateway)]
    public async Task Provider_failures_map_to_gateway_errors_without_leaking_details(Type exceptionType, HttpStatusCode expected)
    {
        var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICrmDataProvider>();
            services.AddSingleton<ICrmDataProvider>(new FailingProvider(exceptionType));
        })).CreateClient();

        var response = await client.GetAsync("/api/households");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain(FailingProvider.SecretDetail, body);
    }

    [Fact]
    public async Task Salesforce_provider_without_configuration_returns_503()
    {
        var client = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Crm:Provider"] = "Salesforce" }))).CreateClient();

        var response = await client.GetAsync("/api/households");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private sealed class FailingProvider(Type exceptionType) : ICrmDataProvider
    {
        public const string SecretDetail = "upstream-detail-that-must-not-leak";

        private Exception Fail() => (Exception)Activator.CreateInstance(exceptionType, SecretDetail, null)!;

        public Task<PagedResult<Household>> GetHouseholdsAsync(HouseholdQuery query, CancellationToken ct) => throw Fail();
        public Task<Household?> GetHouseholdAsync(string id, CancellationToken ct) => throw Fail();
        public Task<IReadOnlyList<HouseholdMember>?> GetMembersAsync(string householdId, CancellationToken ct) => throw Fail();
        public Task<IReadOnlyDictionary<string, IReadOnlyList<HouseholdMember>>> GetMembersForHouseholdsAsync(
            IReadOnlyCollection<string> householdIds, CancellationToken ct) => throw Fail();
        public Task<Household> CreateHouseholdAsync(HouseholdDraft draft, CancellationToken ct) => throw Fail();
        public Task<Household?> UpdateHouseholdAsync(string id, HouseholdDraft draft, CancellationToken ct) => throw Fail();
    }
}
