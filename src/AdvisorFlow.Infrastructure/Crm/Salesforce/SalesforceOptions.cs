namespace AdvisorFlow.Infrastructure.Crm.Salesforce;

/// <summary>
/// Bound from the "Salesforce" configuration section. Values come from
/// <c>dotnet user-secrets</c> locally and Azure Key Vault in deployed environments,
/// never from appsettings.json (which only holds empty placeholders).
/// </summary>
public sealed class SalesforceOptions
{
    public const string SectionName = "Salesforce";

    /// <summary>Base URL that receives the token request: the org's My Domain URL,
    /// e.g. https://orgfarm-xxxx-dev-ed.develop.my.salesforce.com.</summary>
    public string? LoginUrl { get; set; }

    /// <summary>Consumer Key of the External Client App that has the JWT bearer flow enabled.</summary>
    public string? ClientId { get; set; }

    /// <summary>Username of the dedicated, read-only integration user.</summary>
    public string? IntegrationUsername { get; set; }

    /// <summary>Path to the PEM private key that signs the JWT. Local development only;
    /// deployed environments load the key from Azure Key Vault (M7).</summary>
    public string? PrivateKeyReference { get; set; }

    /// <summary>The JWT <c>aud</c> claim. Production and Developer Edition orgs use
    /// https://login.salesforce.com; sandboxes use https://test.salesforce.com.</summary>
    public string Audience { get; set; } = "https://login.salesforce.com";

    /// <summary>REST API version, matching sourceApiVersion in sfdx-project.json.</summary>
    public string ApiVersion { get; set; } = "v68.0";

    /// <summary>How long an access token is reused before a new one is requested.
    /// Salesforce does not return an expiry for JWT tokens; a 401 also forces a refresh.</summary>
    public int TokenCacheMinutes { get; set; } = 30;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(LoginUrl) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(IntegrationUsername) &&
        !string.IsNullOrWhiteSpace(PrivateKeyReference);
}
