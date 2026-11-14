using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AdvisorFlow.Infrastructure.Crm.Salesforce;

/// <summary>
/// Creates the signed JWT for the OAuth 2.0 JWT bearer flow (RFC 7523), using only
/// the .NET base library. Salesforce verifies the RS256 signature with the
/// certificate uploaded to the External Client App.
/// </summary>
public static class SalesforceJwt
{
    /// <summary>Salesforce rejects assertions that expire more than 3 minutes ahead.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(3);

    private static readonly byte[] Header = JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" });

    /// <param name="clientId">Consumer Key (<c>iss</c>).</param>
    /// <param name="username">Integration username (<c>sub</c>).</param>
    /// <param name="audience">Authorization server (<c>aud</c>).</param>
    /// <param name="now">Current time, from the injected <see cref="TimeProvider"/>.</param>
    /// <param name="key">RSA private key that matches the uploaded certificate.</param>
    public static string Create(string clientId, string username, string audience, DateTimeOffset now, RSA key)
    {
        var claims = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = clientId,
            ["sub"] = username,
            ["aud"] = audience,
            ["exp"] = now.Add(Lifetime).ToUnixTimeSeconds(),
        });

        var signingInput = $"{Base64Url.EncodeToString(Header)}.{Base64Url.EncodeToString(claims)}";
        var signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }
}
