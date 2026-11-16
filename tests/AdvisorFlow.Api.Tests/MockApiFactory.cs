using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdvisorFlow.Api.Tests;

/// <summary>
/// Hosts the API in memory with the mock provider and blank Salesforce settings.
/// In Development the API also reads dotnet user-secrets, so without this a developer
/// machine set up for Salesforce would run the tests against the real org.
/// These settings are added last, so they override user-secrets and appsettings.
///
/// Sign-in (ADR-015) is replaced by <see cref="TestAdvisorAuth"/>, and every client is signed in
/// as an advisor by default, so write tests read as before. Tests about access remove the header.
/// </summary>
public sealed class MockApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Crm:Provider"] = "Mock",
            ["Salesforce:LoginUrl"] = "",
            ["Salesforce:ClientId"] = "",
            ["Salesforce:IntegrationUsername"] = "",
            ["Salesforce:PrivateKeyReference"] = "",
        }));

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAdvisorAuth.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAdvisorAuth>(TestAdvisorAuth.Scheme, _ => { });
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAdvisorAuth.Scheme;
                options.DefaultAuthenticateScheme = TestAdvisorAuth.Scheme;
                options.DefaultChallengeScheme = TestAdvisorAuth.Scheme;
                options.DefaultForbidScheme = TestAdvisorAuth.Scheme;
            });
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add(TestAdvisorAuth.Header, TestAdvisorAuth.Advisor);
    }
}
