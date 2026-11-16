namespace AdvisorFlow.Api.Features;

/// <summary>
/// Bound from the "Features" configuration section. Secure by default: appsettings.json turns
/// writes off, and only appsettings.Development.json turns them on (ADR-009).
/// </summary>
public sealed class ApiFeatures
{
    public const string SectionName = "Features";

    /// <summary>
    /// When false, every create/update endpoint answers 403 before reading the request, so a
    /// public deployment can show real Salesforce data without anyone being able to change it.
    /// </summary>
    public bool AllowWrites { get; set; }
}
