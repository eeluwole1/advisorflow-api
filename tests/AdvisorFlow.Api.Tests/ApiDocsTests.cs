using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AdvisorFlow.Api.Tests;

/// <summary>Swagger UI and the contract file are served in Development only.</summary>
public class ApiDocsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Swagger_ui_is_served_in_development()
    {
        var client = factory.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

        var response = await client.GetAsync("/swagger");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/openapi/v1.yaml", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Contract_is_served_in_development()
    {
        var client = factory.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

        var response = await client.GetAsync("/openapi/v1.yaml");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("openapi: 3.1.0", body);
    }

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/openapi/v1.yaml")]
    public async Task Docs_are_not_served_in_production(string url)
    {
        var client = factory.WithWebHostBuilder(b => b.UseEnvironment("Production")).CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
