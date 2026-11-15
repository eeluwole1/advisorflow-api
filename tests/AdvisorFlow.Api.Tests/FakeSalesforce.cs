using System.Net;
using System.Security.Cryptography;
using System.Text;
using AdvisorFlow.Infrastructure.Crm;
using AdvisorFlow.Infrastructure.Crm.Salesforce;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AdvisorFlow.Api.Tests;

/// <summary>One request the fake received, captured before the message is disposed.</summary>
public sealed record FakeRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body)
{
    /// <summary>The decoded SOQL of a query request.</summary>
    public string? Soql => Uri.AbsolutePath.EndsWith("/query", StringComparison.Ordinal)
        ? Uri.UnescapeDataString(Uri.Query.TrimStart('?')[2..])
        : null;
}

/// <summary>
/// Stands in for Salesforce in unit tests: answers the token endpoint and REST queries
/// with canned JSON, and records every request. No network or credentials are needed.
/// </summary>
public sealed class FakeSalesforce : HttpMessageHandler, IHttpClientFactory
{
    public const string LoginUrl = "https://example.my.salesforce.com";
    public const string InstanceUrl = "https://example-instance.my.salesforce.com";

    private int _tokens;

    public List<FakeRequest> Requests { get; } = [];

    public IEnumerable<FakeRequest> TokenRequests => Requests.Where(r => r.Uri.AbsolutePath == "/services/oauth2/token");

    public IEnumerable<FakeRequest> Queries => Requests.Where(r => r.Soql is not null);

    /// <summary>Record creates and updates (<c>/sobjects/...</c>).</summary>
    public IEnumerable<FakeRequest> Writes => Requests.Where(r => r.Uri.AbsolutePath.Contains("/sobjects/", StringComparison.Ordinal));

    /// <summary>Composite requests (<c>/composite</c>), the all-or-nothing writes.</summary>
    public IEnumerable<FakeRequest> Composites => Requests.Where(r => r.Uri.AbsolutePath.EndsWith("/composite", StringComparison.Ordinal));

    public const string NewAccountId = "001fj00002NewAcAAA";
    public const string NewContactId = "003fj00002NewCoAAA";

    /// <summary>Answers token requests; by default issues token-1, token-2, ...</summary>
    public Func<FakeRequest, HttpResponseMessage> OnToken { get; set; }

    /// <summary>Answers query requests; by default an empty result.</summary>
    public Func<FakeRequest, HttpResponseMessage> OnQuery { get; set; } = _ => Json(QueryResult());

    /// <summary>Answers creates (201 with <see cref="NewAccountId"/>) and updates (204) by default.</summary>
    public Func<FakeRequest, HttpResponseMessage> OnWrite { get; set; } = r =>
        r.Uri.AbsolutePath.EndsWith("/composite", StringComparison.Ordinal) ? Json(CompositeSuccess(r.Body))
        : r.Method == HttpMethod.Post ? Json($$"""{"id":"{{NewAccountId}}","success":true,"errors":[]}""", HttpStatusCode.Created)
        : new HttpResponseMessage(HttpStatusCode.NoContent);

    /// <summary>A successful composite response: 201 with <see cref="NewContactId"/> for each POST step, 204 for each PATCH.</summary>
    public static string CompositeSuccess(string requestBody)
    {
        using var request = System.Text.Json.JsonDocument.Parse(requestBody);
        var results = request.RootElement.GetProperty("compositeRequest").EnumerateArray().Select(step =>
        {
            var reference = step.GetProperty("referenceId").GetString();
            return step.GetProperty("method").GetString() == "POST"
                ? $$"""{"body":{"id":"{{NewContactId}}","success":true,"errors":[]},"httpHeaders":{},"httpStatusCode":201,"referenceId":"{{reference}}"}"""
                : $$"""{"body":null,"httpHeaders":{},"httpStatusCode":204,"referenceId":"{{reference}}"}""";
        });
        return $$"""{"compositeResponse":[{{string.Join(",", results)}}]}""";
    }

    public FakeSalesforce() =>
        OnToken = _ => Json($$"""{"access_token":"token-{{++_tokens}}","instance_url":"{{InstanceUrl}}","token_type":"Bearer"}""");

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        var captured = new FakeRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body);
        Requests.Add(captured);
        if (captured.Uri.AbsolutePath == "/services/oauth2/token")
        {
            return OnToken(captured);
        }

        return captured.Soql is not null ? OnQuery(captured) : OnWrite(captured);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>A REST query response with the given record objects (JSON).</summary>
    public static string QueryResult(params string[] records) =>
        $$"""{"totalSize":{{records.Length}},"done":true,"records":[{{string.Join(",", records)}}]}""";

    public static string Count(int total) => $$"""{"totalSize":{{total}},"done":true,"records":[]}""";

    public static string Error(string code, string message) => $$"""[{"errorCode":"{{code}}","message":"{{message}}"}]""";
}

/// <summary>A clock the tests move by hand.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Builds the Salesforce provider against <see cref="FakeSalesforce"/> with a throwaway key.</summary>
public sealed class SalesforceHarness : IDisposable
{
    private static readonly Lazy<RSA> SharedKey = new(() => RSA.Create(2048));

    public SalesforceHarness(Action<SalesforceOptions>? configure = null)
    {
        KeyPath = Path.Combine(Path.GetTempPath(), $"advisorflow-test-{Guid.NewGuid():N}.key");
        File.WriteAllText(KeyPath, SharedKey.Value.ExportPkcs8PrivateKeyPem());

        Options = new SalesforceOptions
        {
            LoginUrl = FakeSalesforce.LoginUrl,
            ClientId = "test-consumer-key",
            IntegrationUsername = "integration@example.com",
            PrivateKeyReference = KeyPath,
        };
        configure?.Invoke(Options);

        var options = Microsoft.Extensions.Options.Options.Create(Options);
        Authenticator = new SalesforceAuthenticator(Http, options, Clock, NullLogger<SalesforceAuthenticator>.Instance);
        Provider = new SalesforceCrmDataProvider(
            new SalesforceRestClient(Http, Authenticator, options, NullLogger<SalesforceRestClient>.Instance));
    }

    public static RSA PublicKey => SharedKey.Value;

    public string KeyPath { get; }

    public SalesforceOptions Options { get; }

    public FakeSalesforce Http { get; } = new();

    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 11, 13, 22, 0, 0, TimeSpan.Zero));

    public SalesforceAuthenticator Authenticator { get; }

    public SalesforceCrmDataProvider Provider { get; }

    public void Dispose()
    {
        Authenticator.Dispose();
        File.Delete(KeyPath);
    }
}
