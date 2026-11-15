using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdvisorFlow.Application.Households;

namespace AdvisorFlow.Api.Tests;

/// <summary>
/// Member create/update over HTTP (contract v1.2), with the mock provider. Each test works on a
/// household it creates, so tests never depend on each other's changes.
/// </summary>
public class MemberWriteApiTests(MockApiFactory factory) : IClassFixture<MockApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> NewHouseholdAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/households", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HouseholdDetailDto>())!.Id;
    }

    private async Task<HouseholdMemberDto> AddMemberAsync(string householdId, object body)
    {
        var response = await _client.PostAsJsonAsync($"/api/households/{householdId}/contacts", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HouseholdMemberDto>())!;
    }

    private Task<List<HouseholdMemberDto>?> MembersAsync(string householdId) =>
        _client.GetFromJsonAsync<List<HouseholdMemberDto>>($"/api/households/{householdId}/contacts");

    [Fact]
    public async Task Post_adds_the_first_member_as_primary_and_returns_201() // BR-HH-003
    {
        var household = await NewHouseholdAsync("Mensah Household");

        var response = await _client.PostAsJsonAsync(
            $"/api/households/{household}/contacts",
            new { firstName = "Kofi", lastName = "Mensah", email = "kofi.mensah@example.com", preferredContactMethod = "email" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var member = await response.Content.ReadFromJsonAsync<HouseholdMemberDto>();
        Assert.NotNull(member);
        Assert.Equal("Kofi Mensah", member.FullName);
        Assert.Equal("Email", member.PreferredContactMethod);
        Assert.True(member.IsPrimary);
        Assert.Equal(household, member.HouseholdId);
        Assert.EndsWith($"/api/households/{household}/contacts", response.Headers.Location!.ToString());

        var summary = await _client.GetFromJsonAsync<HouseholdDetailDto>($"/api/households/{household}");
        Assert.Equal(1, summary!.MemberCount);
    }

    [Fact]
    public async Task Making_another_member_primary_moves_the_flag() // BR-HH-003
    {
        var household = await NewHouseholdAsync("Ito Household");
        var first = await AddMemberAsync(household, new { firstName = "Haruto", lastName = "Ito" });
        var second = await AddMemberAsync(household, new { firstName = "Yui", lastName = "Ito" });
        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);

        var response = await _client.PutAsJsonAsync(
            $"/api/households/{household}/contacts/{second.Id}", new { firstName = "Yui", lastName = "Ito", isPrimary = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var members = await MembersAsync(household);
        Assert.Equal(second.Id, members![0].Id); // primary first
        Assert.Single(members, m => m.IsPrimary);
    }

    [Fact]
    public async Task Unsetting_the_primary_returns_400() // BR-HH-003
    {
        var household = await NewHouseholdAsync("Silva Household");
        var primary = await AddMemberAsync(household, new { lastName = "Silva" });

        var response = await _client.PutAsJsonAsync(
            $"/api/households/{household}/contacts/{primary.Id}", new { lastName = "Silva", isPrimary = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("isPrimary", out _));
    }

    [Fact]
    public async Task Invalid_member_fields_return_400_with_field_errors()
    {
        var household = await NewHouseholdAsync("Kowalski Household");

        var response = await _client.PostAsJsonAsync(
            $"/api/households/{household}/contacts", new { firstName = "Jan", email = "not-an-email", preferredContactMethod = "Phone" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("lastName", out _));
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("preferredContactMethod", out _));
    }

    [Fact]
    public async Task Unknown_household_or_member_returns_404()
    {
        var household = await NewHouseholdAsync("Byrne Household");

        var noHousehold = await _client.PostAsJsonAsync("/api/households/001MOCK99999999999/contacts", new { lastName = "Byrne" });
        var noMember = await _client.PutAsJsonAsync($"/api/households/{household}/contacts/003MOCK99999999999", new { lastName = "Byrne" });

        Assert.Equal(HttpStatusCode.NotFound, noHousehold.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noMember.StatusCode);
    }

    [Fact]
    public async Task A_malformed_contact_id_returns_400()
    {
        var household = await NewHouseholdAsync("Dubois Household");

        var response = await _client.PutAsJsonAsync($"/api/households/{household}/contacts/not-an-id", new { lastName = "Dubois" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
