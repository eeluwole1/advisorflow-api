using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace AdvisorFlow.Api.Auth;

/// <summary>
/// Bound from the "Auth" configuration section: the Microsoft Entra ID registration of the
/// AdvisorFlow API (ADR-015). All values are identifiers, not secrets.
/// </summary>
public sealed class AuthSettings
{
    public const string SectionName = "Auth";

    /// <summary>Directory (tenant) ID of the Entra directory that issues advisor tokens.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Application (client) ID of the "AdvisorFlow API" registration; tokens must be issued for it.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>App role an advisor must hold to change records.</summary>
    public string RequiredRole { get; set; } = "Advisor";

    /// <summary>Delegated scope the web app must have requested.</summary>
    public string RequiredScope { get; set; } = "access_as_user";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId);
}

/// <summary>
/// Sign-in for changes (ADR-015): reads stay anonymous, every create/update needs an Entra access
/// token for this API that carries the Advisor app role and the access_as_user scope.
/// </summary>
public static class AdvisorAuth
{
    /// <summary>Authorization policy on every create/update endpoint.</summary>
    public const string AdvisorPolicy = "Advisor";

    public static IServiceCollection AddAdvisorAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(AuthSettings.SectionName).Get<AuthSettings>() ?? new AuthSettings();
        services.Configure<AuthSettings>(configuration.GetSection(AuthSettings.SectionName));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep Entra's claim names ("roles", "scp", "name", "oid") instead of the legacy SOAP URIs.
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "roles";

                if (!settings.IsConfigured)
                {
                    // No directory configured: there are no signing keys, so every token is rejected and
                    // writes answer 401 while reads keep working. Program.cs logs a warning at startup.
                    return;
                }

                // Entra publishes its signing keys at the authority's OpenID configuration.
                options.Authority = $"https://login.microsoftonline.com/{settings.TenantId}/v2.0";

                // Accept v2 and v1 access tokens for this API only: the audience is the API's client id
                // (v2) or its Application ID URI (v1), the issuer is this directory.
                options.TokenValidationParameters.ValidAudiences = [settings.ClientId, $"api://{settings.ClientId}"];
                options.TokenValidationParameters.ValidIssuers =
                [
                    $"https://login.microsoftonline.com/{settings.TenantId}/v2.0",
                    $"https://sts.windows.net/{settings.TenantId}/",
                ];
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdvisorPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(settings.RequiredRole)
                .RequireAssertion(context => HasScope(context.User, settings.RequiredScope)));

        return services;
    }

    /// <summary>True when the delegated scopes ("scp", space-separated) include <paramref name="scope"/>.</summary>
    public static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll("scp")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);

    /// <summary>
    /// How the acting advisor is recorded on a changed record (ADR-016): "Name (sign-in name)",
    /// or whichever of the two the token carries. Null for an anonymous caller.
    /// </summary>
    public static string? AuditLabel(this ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var name = user.FindFirst("name")?.Value?.Trim();
        var username = user.FindFirst("preferred_username")?.Value?.Trim();
        return (string.IsNullOrEmpty(name), string.IsNullOrEmpty(username)) switch
        {
            (false, false) when !string.Equals(name, username, StringComparison.OrdinalIgnoreCase) => $"{name} ({username})",
            (false, _) => name,
            (true, false) => username,
            _ => user.FindFirst("oid")?.Value,
        };
    }

    /// <summary>Stable id of the signed-in advisor for logs (Entra object id), never the email.</summary>
    public static string AdvisorId(this ClaimsPrincipal user) =>
        user.FindFirst("oid")?.Value ?? user.FindFirst("sub")?.Value ?? "unknown";
}
