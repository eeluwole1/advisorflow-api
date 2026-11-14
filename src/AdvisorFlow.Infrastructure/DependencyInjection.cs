using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Infrastructure.Crm;
using AdvisorFlow.Infrastructure.Crm.Salesforce;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdvisorFlow.Infrastructure;

public static class DependencyInjection
{
    private static readonly string[] Providers = ["Mock", "Salesforce"];

    /// <summary>
    /// Registers the CRM provider chosen by <c>Crm:Provider</c>: "Mock" (default,
    /// fictional in-memory data) or "Salesforce" (JWT bearer flow, read-only integration user).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail at startup on a typo rather than on the first request.
        ValidateProvider(configuration["Crm:Provider"]);

        services.Configure<SalesforceOptions>(configuration.GetSection(SalesforceOptions.SectionName));
        services.AddHttpClient(SalesforceAuthenticator.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SalesforceAuthenticator>();
        services.AddSingleton<SalesforceQueryClient>();
        services.AddSingleton<SalesforceCrmDataProvider>();
        services.AddSingleton<MockCrmDataProvider>();

        // Chosen from the final configuration when first needed, so settings added later
        // (e.g. by integration tests) take precedence over user-secrets and appsettings.
        services.AddSingleton<ICrmDataProvider>(sp =>
            ValidateProvider(sp.GetRequiredService<IConfiguration>()["Crm:Provider"]) == "Salesforce"
                ? sp.GetRequiredService<SalesforceCrmDataProvider>()
                : sp.GetRequiredService<MockCrmDataProvider>());

        return services;
    }

    private static string ValidateProvider(string? value)
    {
        var provider = string.IsNullOrWhiteSpace(value) ? "Mock" : value.Trim();
        return Providers.FirstOrDefault(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown Crm:Provider '{provider}'. Use 'Mock' or 'Salesforce'.");
    }
}
