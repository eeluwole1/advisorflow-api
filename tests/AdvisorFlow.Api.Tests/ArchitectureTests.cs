using System.Reflection;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Households;
using AdvisorFlow.Infrastructure;

namespace AdvisorFlow.Api.Tests;

/// <summary>Enforces the layer rules: Domain ← Application ← Infrastructure ← Api.</summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Household).Assembly;
    private static readonly Assembly Application = typeof(HouseholdService).Assembly;
    private static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;

    private static string[] AdvisorFlowReferences(Assembly assembly) => assembly.GetReferencedAssemblies()
        .Select(a => a.Name!)
        .Where(n => n.StartsWith("AdvisorFlow.", StringComparison.Ordinal))
        .Order()
        .ToArray();

    [Fact]
    public void Domain_depends_on_no_other_layer()
    {
        Assert.Empty(AdvisorFlowReferences(Domain));
    }

    [Fact]
    public void Application_depends_only_on_Domain()
    {
        Assert.Equal(["AdvisorFlow.Domain"], AdvisorFlowReferences(Application));
    }

    [Fact]
    public void Application_has_no_ASP_NET_Core_dependency()
    {
        Assert.DoesNotContain(Application.GetReferencedAssemblies(), a => a.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Api()
    {
        Assert.DoesNotContain("AdvisorFlow.Api", AdvisorFlowReferences(Infrastructure));
    }
}
