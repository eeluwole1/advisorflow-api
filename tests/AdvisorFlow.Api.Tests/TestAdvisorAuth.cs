using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdvisorFlow.Api.Tests;

/// <summary>
/// Stands in for Microsoft Entra ID in the in-memory API (ADR-015). A request is signed in when it
/// carries the <see cref="Header"/> header, whose value lists the token claims, e.g.
/// <c>roles=Advisor;scp=access_as_user;name=Test Advisor</c>. Without the header the caller is anonymous.
/// The real JWT validation is configuration (AdvisorAuth) and is checked against Entra by hand (runbook).
/// </summary>
public sealed class TestAdvisorAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "TestAdvisor";
    public const string Header = "X-Test-Advisor";

    /// <summary>A signed-in advisor with the role and scope that writes require.</summary>
    public const string Advisor =
        "roles=Advisor;scp=access_as_user;name=Test Advisor;preferred_username=test.advisor@example.com;oid=11111111-2222-3333-4444-555555555555";

    /// <summary>The audit label <see cref="Advisor"/> produces (ADR-016).</summary>
    public const string AdvisorLabel = "Test Advisor (test.advisor@example.com)";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = value.ToString()
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => new Claim(parts[0].Trim(), parts[1].Trim()));

        var identity = new ClaimsIdentity(claims, Scheme, nameType: "name", roleType: "roles");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}
