using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace AdvisorFlow.Api.Tests;

/// <summary>
/// The read-only switch used on Azure (ADR-009): with Features:AllowWrites=false every
/// create/update answers 403 before its body is even read, while reads keep working.
/// </summary>
public class ReadOnlyModeApiTests(MockApiFactory factory) : IClassFixture<MockApiFactory>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["Features:AllowWrites"] = "false" }))).CreateClient();

    public static TheoryData<string, string> Writes => new()
    {
        { "POST", "/api/households" },
        { "PUT", "/api/households/001MOCK00000000001" },
        { "POST", "/api/households/001MOCK00000000001/contacts" },
        { "PUT", "/api/households/001MOCK00000000001/contacts/003MOCK00000000001" },
    };

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task Writes_are_refused_with_403(string method, string url)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = JsonContent.Create(new { name = "Any", lastName = "Any", clientStatus = "Active" }),
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_403_comes_before_body_validation()
    {
        var response = await _client.PostAsync("/api/households", new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reads_still_work_and_nothing_was_changed()
    {
        await _client.PostAsJsonAsync("/api/households", new { name = "Should Not Exist" });

        var page = await _client.GetStringAsync("/api/households?search=Should%20Not%20Exist");

        Assert.Contains("\"totalCount\":0", page);
    }
}
