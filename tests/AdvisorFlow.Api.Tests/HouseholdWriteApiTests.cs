using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AdvisorFlow.Application.Households;

namespace AdvisorFlow.Api.Tests;

/// <summary>
/// Household create/update over HTTP (contract v1.1), with the mock provider.
/// This class has its own <see cref="MockApiFactory"/>, so its writes never change
/// the data seen by the read tests. Each test creates the records it changes.
/// </summary>
public class HouseholdWriteApiTests(MockApiFactory factory) : IClassFixture<MockApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HouseholdDetailDto> CreateAsync(object body)
    {
        var response = await _client.PostAsJsonAsync("/api/households", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HouseholdDetailDto>())!;
    }

    [Fact]
    public async Task Post_creates_a_household_and_returns_201_with_its_location()
    {
        var response = await _client.PostAsJsonAsync("/api/households", new { name = "Adeyemi Household", priority = "high" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<HouseholdDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Adeyemi Household", created.Name);
        Assert.Equal("Prospect", created.ClientStatus);
        Assert.Equal("High", created.Priority);
        Assert.Equal(0, created.MemberCount);
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/api/households/{created.Id}", response.Headers.Location.ToString());

        var fetched = await _client.GetFromJsonAsync<HouseholdDetailDto>(response.Headers.Location);
        Assert.Equal(created, fetched);
    }

    [Fact]
    public async Task Post_with_invalid_values_returns_400_with_field_errors() // BR-CL-001
    {
        var response = await _client.PostAsJsonAsync("/api/households", new { name = "", clientStatus = "VIP" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("clientStatus", out _));
    }

    [Fact]
    public async Task Post_with_malformed_json_returns_400()
    {
        var response = await _client.PostAsync(
            "/api/households", new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_replaces_the_editable_fields()
    {
        var created = await CreateAsync(new { name = "Haddad Household", clientStatus = "Active", riskProfile = "Moderate", phone = "204-555-0150" });

        var response = await _client.PutAsJsonAsync(
            $"/api/households/{created.Id}", new { name = "Haddad Family", clientStatus = "Inactive" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<HouseholdDetailDto>();
        Assert.NotNull(updated);
        Assert.Equal("Haddad Family", updated.Name);
        Assert.Equal("Inactive", updated.ClientStatus); // deactivated, not deleted (ADR-008)
        Assert.Null(updated.RiskProfile);                 // omitted → cleared (PUT)
        Assert.Null(updated.Phone);
    }

    [Fact]
    public async Task Put_without_a_client_status_returns_400()
    {
        var created = await CreateAsync(new { name = "Lindqvist Household" });

        var response = await _client.PutAsJsonAsync($"/api/households/{created.Id}", new { name = "Lindqvist Household" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_on_an_unknown_household_returns_404()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/households/001MOCK99999999999", new { name = "Nobody", clientStatus = "Active" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_with_a_malformed_id_returns_400()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/households/not-an-id", new { name = "Nobody", clientStatus = "Active" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task There_is_no_delete_endpoint() // ADR-008
    {
        var created = await CreateAsync(new { name = "Novak Household" });

        var response = await _client.DeleteAsync($"/api/households/{created.Id}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
