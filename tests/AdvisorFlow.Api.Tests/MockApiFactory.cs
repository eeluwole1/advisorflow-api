using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AdvisorFlow.Api.Tests;

/// <summary>
/// Hosts the API in memory with the mock provider and blank Salesforce settings.
/// In Development the API also reads dotnet user-secrets, so without this a developer
/// machine set up for Salesforce would run the tests against the real org.
/// These settings are added last, so they override user-secrets and appsettings.
/// </summary>
public sealed class MockApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Crm:Provider"] = "Mock",
            ["Salesforce:LoginUrl"] = "",
            ["Salesforce:ClientId"] = "",
            ["Salesforce:IntegrationUsername"] = "",
            ["Salesforce:PrivateKeyReference"] = "",
        }));
}
