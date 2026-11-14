using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using AdvisorFlow.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdvisorFlow.Infrastructure.Crm.Salesforce;

/// <summary>An access token and the org URL that REST calls go to.</summary>
public sealed record SalesforceSession(string AccessToken, Uri InstanceUrl);

/// <summary>
/// Signs in as the integration user with the JWT bearer flow and caches the session,
/// so the API does not request a token for every call (Developer Edition API limits).
/// Failures become <see cref="CrmUnavailableException"/> (HTTP 503); Salesforce's
/// error details are logged, never returned to the caller.
/// </summary>
public sealed class SalesforceAuthenticator(
    IHttpClientFactory httpClientFactory,
    IOptions<SalesforceOptions> options,
    TimeProvider time,
    ILogger<SalesforceAuthenticator> logger) : IDisposable
{
    public const string HttpClientName = "Salesforce";

    private readonly SalesforceOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private RSA? _key;
    private SalesforceSession? _session;
    private DateTimeOffset _sessionExpires;

    /// <summary>Returns the cached session, or signs in if there is none or it has expired.</summary>
    public async Task<SalesforceSession> GetSessionAsync(CancellationToken ct)
    {
        if (_session is { } cached && time.GetUtcNow() < _sessionExpires)
        {
            return cached;
        }

        await _lock.WaitAsync(ct);
        try
        {
            // Another caller may have signed in while this one waited.
            if (_session is { } current && time.GetUtcNow() < _sessionExpires)
            {
                return current;
            }

            _session = await RequestSessionAsync(ct);
            _sessionExpires = time.GetUtcNow().AddMinutes(_options.TokenCacheMinutes);
            return _session;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Drops the session after Salesforce rejects it (HTTP 401), unless a newer one already replaced it.</summary>
    public void Invalidate(SalesforceSession rejected)
    {
        if (ReferenceEquals(_session, rejected))
        {
            _session = null;
        }
    }

    public void Dispose()
    {
        _key?.Dispose();
        _lock.Dispose();
    }

    private async Task<SalesforceSession> RequestSessionAsync(CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            throw new CrmUnavailableException(
                "The Salesforce provider is not configured. Set the Salesforce:* settings with dotnet user-secrets, " +
                "or set Crm:Provider to \"Mock\".");
        }

        var assertion = SalesforceJwt.Create(
            _options.ClientId!, _options.IntegrationUsername!, _options.Audience, time.GetUtcNow(), LoadKey());

        var tokenUrl = new Uri(new Uri(_options.LoginUrl!.TrimEnd('/') + "/"), "services/oauth2/token");
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            }),
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new CrmUnavailableException("Could not reach Salesforce to request an access token.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorAsync(response, ct);
                logger.LogError(
                    "Salesforce token request failed with {StatusCode}: {Error} {Description}",
                    (int)response.StatusCode, error?.Error, error?.Description);
                throw new CrmUnavailableException(
                    $"Salesforce rejected the token request ({(int)response.StatusCode} {error?.Error}).");
            }

            var token = await ReadJsonAsync<TokenResponse>(response, ct);
            if (string.IsNullOrEmpty(token?.AccessToken) ||
                !Uri.TryCreate(token.InstanceUrl, UriKind.Absolute, out var instanceUrl) ||
                instanceUrl.Scheme != Uri.UriSchemeHttps)
            {
                throw new CrmUpstreamException("Salesforce returned an unexpected token response.");
            }

            logger.LogInformation("Signed in to Salesforce as the integration user.");
            return new SalesforceSession(token.AccessToken, instanceUrl);
        }
    }

    private RSA LoadKey()
    {
        if (_key is not null)
        {
            return _key;
        }

        var path = _options.PrivateKeyReference!;
        if (!File.Exists(path))
        {
            throw new CrmUnavailableException($"The Salesforce private key file was not found at '{path}'.");
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            rsa.Dispose();
            throw new CrmUnavailableException("The Salesforce private key file is not a valid PEM RSA key.", ex);
        }

        return _key = rsa;
    }

    private static async Task<TokenError?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TokenError>(ct);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            throw new CrmUpstreamException("Salesforce returned a response that is not valid JSON.", ex);
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("instance_url")] string? InstanceUrl);

    private sealed record TokenError(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? Description);
}
