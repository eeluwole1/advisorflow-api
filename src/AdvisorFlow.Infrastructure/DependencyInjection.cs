using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Infrastructure.Crm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdvisorFlow.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the CRM provider chosen by <c>Crm:Provider</c>: "Mock" (default,
    /// fictional in-memory data) or "Salesforce" (implemented in M2).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SalesforceOptions>(configuration.GetSection(SalesforceOptions.SectionName));

        var provider = configuration["Crm:Provider"] ?? "Mock";
        switch (provider.ToLowerInvariant())
        {
            case "mock":
                services.AddSingleton<ICrmDataProvider, MockCrmDataProvider>();
                break;
            case "salesforce":
                services.AddSingleton<ICrmDataProvider, SalesforceCrmDataProvider>();
                break;
            default:
                throw new InvalidOperationException($"Unknown Crm:Provider '{provider}'. Use 'Mock' or 'Salesforce'.");
        }

        return services;
    }
}
