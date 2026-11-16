using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AdvisorFlow.Api.Auth;
using AdvisorFlow.Infrastructure.Crm;
using Microsoft.Extensions.DependencyInjection;

namespace AdvisorFlow.Api.Tests;

/// <summary>
/// Sign-in for changes (ADR-015) and the acting advisor (ADR-016): anyone may read, only a
/// signed-in advisor with the Advisor role and the access_as_user scope may create or update,
/// and every change records who made it.
/// </summary>
public class AdvisorAuthApiTests(MockApiFactory factory) : IClassFixture<MockApiFactory>
{
    public static TheoryData<string, string> Writes => new()
    {
        { "POST", "/api/households" },
        { "PUT", "/api/households/001MOCK00000000001" },
        { "POST", "/api/households/001MOCK00000000001/contacts" },
        { "PUT", "/api/households/001MOCK00000000001/contacts/003MOCK00000000001" },
        { "POST", "/api/households/001MOCK00000000001/opportunities" },
        { "PUT", "/api/opportunities/006MOCK00000000001" },
    };

    private static readonly object AnyBody = new
    {
        name = "Any", firstName = "Any", lastName = "Any", clientStatus = "Active", stage = "Prospecting", closeDate = "2027-01-31",
    };

    private HttpClient ClientAs(string? claims)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove(TestAdvisorAuth.Header);
        if (claims is not null)
        {
            client.DefaultRequestHeaders.Add(TestAdvisorAuth.Header, claims);
        }

        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url) =>
        await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(AnyBody) });

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task Anonymous_writes_answer_401(string method, string url)
    {
        var response = await SendAsync(ClientAs(null), method, url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task Signed_in_without_the_Advisor_role_answers_403(string method, string url)
    {
        var response = await SendAsync(ClientAs("scp=access_as_user;name=Not An Advisor"), method, url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_token_without_the_api_scope_answers_403()
    {
        var response = await SendAsync(ClientAs("roles=Advisor;scp=User.Read;name=Wrong Scope"), "POST", "/api/households");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/households")]
    [InlineData("/api/households/001MOCK00000000001")]
    [InlineData("/api/opportunities")]
    [InlineData("/api/activities")]
    [InlineData("/api/dashboard")]
    public async Task Reads_stay_public(string url)
    {
        Assert.Equal(HttpStatusCode.OK, (await ClientAs(null).GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task An_advisor_can_create_and_the_change_records_who_made_it()
    {
        var response = await ClientAs(TestAdvisorAuth.Advisor).PostAsJsonAsync("/api/households", new { name = "Audit Household" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetString()!;
        Assert.Equal(TestAdvisorAuth.AdvisorLabel, Mock.LastChangedBy(id));
    }

    [Fact]
    public async Task Member_and_opportunity_changes_record_the_advisor_too()
    {
        var client = ClientAs(TestAdvisorAuth.Advisor);

        var member = await client.PostAsJsonAsync("/api/households/001MOCK00000000002/contacts", new { firstName = "Ada", lastName = "Audit" });
        var opportunity = await client.PostAsJsonAsync(
            "/api/households/001MOCK00000000002/opportunities",
            new { name = "Audit Planning", closeDate = DateTime.UtcNow.AddMonths(2).ToString("yyyy-MM-dd") });

        Assert.Equal(HttpStatusCode.Created, member.StatusCode);
        Assert.Equal(HttpStatusCode.Created, opportunity.StatusCode);
        Assert.Equal(TestAdvisorAuth.AdvisorLabel, Mock.LastChangedBy(await IdOf(member)));
        Assert.Equal(TestAdvisorAuth.AdvisorLabel, Mock.LastChangedBy(await IdOf(opportunity)));
    }

    [Theory]
    [InlineData("name=Ada Advisor;preferred_username=ada@example.com", "Ada Advisor (ada@example.com)")]
    [InlineData("name=Ada Advisor", "Ada Advisor")]
    [InlineData("preferred_username=ada@example.com", "ada@example.com")]
    [InlineData("name=ada@example.com;preferred_username=ada@example.com", "ada@example.com")]
    [InlineData("oid=abc", "abc")]
    public void Audit_label_uses_name_and_sign_in_name(string claims, string expected)
    {
        var identity = new ClaimsIdentity(
            claims.Split(';').Select(p => p.Split('=', 2)).Select(p => new Claim(p[0], p[1])), "test");

        Assert.Equal(expected, new ClaimsPrincipal(identity).AuditLabel());
    }

    [Fact]
    public void Anonymous_callers_have_no_audit_label()
    {
        Assert.Null(new ClaimsPrincipal(new ClaimsIdentity()).AuditLabel());
    }

    [Theory]
    [InlineData("access_as_user", true)]
    [InlineData("User.Read access_as_user", true)]
    [InlineData("User.Read", false)]
    [InlineData("access_as_user_extra", false)]
    public void Scope_check_matches_whole_scopes(string scp, bool expected)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scp", scp)], "test"));

        Assert.Equal(expected, AdvisorAuth.HasScope(user, "access_as_user"));
    }

    private MockCrmDataProvider Mock => factory.Services.GetRequiredService<MockCrmDataProvider>();

    private static async Task<string> IdOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetString()!;
    }
}
