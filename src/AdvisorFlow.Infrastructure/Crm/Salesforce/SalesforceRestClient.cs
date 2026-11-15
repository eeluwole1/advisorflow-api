using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdvisorFlow.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdvisorFlow.Infrastructure.Crm.Salesforce;

/// <summary>Salesforce reported that a record id in the query is not a valid id.
/// The provider turns this into "not found"; anywhere else it is a 502.</summary>
public sealed class SalesforceInvalidIdException(string message) : CrmUpstreamException(message);

/// <summary>
/// Calls the Salesforce REST API (<c>/services/data/vXX.X/...</c>): SOQL queries, and
/// record create/update for M3. Retries once with a new token after a 401, follows
/// <c>nextRecordsUrl</c> for large results, and maps failures: unreachable, 429 or 5xx
/// become 503; other errors become 502.
/// </summary>
public sealed class SalesforceRestClient(
    IHttpClientFactory httpClientFactory,
    SalesforceAuthenticator authenticator,
    IOptions<SalesforceOptions> options,
    ILogger<SalesforceRestClient> logger)
{
    /// <summary>Upper bound on result batches for one query (2,000 records each).</summary>
    private const int MaxBatches = 10;

    private static readonly HashSet<string> InvalidIdErrorCodes =
        new(StringComparer.Ordinal) { "INVALID_QUERY_FILTER_OPERATOR", "MALFORMED_ID", "INVALID_ID_FIELD" };

    private readonly string _apiVersion = options.Value.ApiVersion;

    /// <summary>Returns every record matched by <paramref name="soql"/>.</summary>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(string soql, CancellationToken ct)
    {
        var records = new List<T>();
        var page = await SendAsync<QueryResult<T>>(HttpMethod.Get, QueryPath(soql), body: null, ct)
            ?? throw new CrmUpstreamException("Salesforce returned an empty response.");
        records.AddRange(page.Records);

        for (var batch = 1; !page.Done && page.NextRecordsUrl is not null; batch++)
        {
            if (batch >= MaxBatches)
            {
                throw new CrmUpstreamException($"The Salesforce query returned more than {MaxBatches} batches.");
            }

            page = await SendAsync<QueryResult<T>>(HttpMethod.Get, page.NextRecordsUrl, body: null, ct)
                ?? throw new CrmUpstreamException("Salesforce returned an empty response.");
            records.AddRange(page.Records);
        }

        return records;
    }

    /// <summary>Runs a <c>SELECT COUNT() FROM ...</c> query and returns the count.</summary>
    public async Task<int> CountAsync(string soql, CancellationToken ct) =>
        (await SendAsync<QueryResult<JsonElement>>(HttpMethod.Get, QueryPath(soql), body: null, ct)
            ?? throw new CrmUpstreamException("Salesforce returned an empty response.")).TotalSize;

    /// <summary>Creates a record and returns its id. Null values are sent as null (field left empty).</summary>
    public async Task<string> CreateAsync(string sobject, IReadOnlyDictionary<string, object?> fields, CancellationToken ct)
    {
        var result = await SendAsync<CreateResult>(HttpMethod.Post, $"/services/data/{_apiVersion}/sobjects/{sobject}", fields, ct);
        return result is { Success: true, Id: { Length: > 0 } id }
            ? id
            : throw new CrmUpstreamException($"Salesforce did not confirm the new {sobject} record.");
    }

    /// <summary>Updates the given fields of a record. A null value clears the field.</summary>
    public Task UpdateAsync(string sobject, string id, IReadOnlyDictionary<string, object?> fields, CancellationToken ct) =>
        SendAsync<JsonElement>(HttpMethod.Patch, $"/services/data/{_apiVersion}/sobjects/{sobject}/{Uri.EscapeDataString(id)}", fields, ct);

    private string QueryPath(string soql) => $"/services/data/{_apiVersion}/query?q={Uri.EscapeDataString(soql)}";

    /// <summary>Sends one request; returns the parsed body, or default for 204 No Content.</summary>
    private async Task<TResult?> SendAsync<TResult>(HttpMethod method, string pathAndQuery, object? body, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var session = await authenticator.GetSessionAsync(ct);
            using var request = new HttpRequestMessage(method, new Uri(session.InstanceUrl, pathAndQuery));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            HttpResponseMessage response;
            try
            {
                response = await httpClientFactory.CreateClient(SalesforceAuthenticator.HttpClientName).SendAsync(request, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw new CrmUnavailableException("Could not reach the Salesforce REST API.", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
                {
                    logger.LogInformation("Salesforce rejected the cached access token; signing in again.");
                    authenticator.Invalidate(session);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw await ToExceptionAsync(response, ct);
                }

                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return default;
                }

                try
                {
                    return await response.Content.ReadFromJsonAsync<TResult>(ct);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException)
                {
                    throw new CrmUpstreamException("Salesforce returned a response that could not be read.", ex);
                }
            }
        }
    }

    private async Task<Exception> ToExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var error = await ReadFirstErrorAsync(response, ct);
        logger.LogWarning(
            "Salesforce request failed with {StatusCode}: {ErrorCode} {Message}", status, error?.ErrorCode, error?.Message);

        var message = $"Salesforce request failed ({status} {error?.ErrorCode}).";
        if (status == 400 && error?.ErrorCode is { } code && InvalidIdErrorCodes.Contains(code))
        {
            return new SalesforceInvalidIdException(message);
        }

        return status == 429 || status >= 500
            ? new CrmUnavailableException(message)
            : new CrmUpstreamException(message);
    }

    private static async Task<SalesforceError?> ReadFirstErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var errors = await response.Content.ReadFromJsonAsync<SalesforceError[]>(ct);
            return errors is { Length: > 0 } ? errors[0] : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record QueryResult<T>(
        [property: JsonPropertyName("totalSize")] int TotalSize,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("nextRecordsUrl")] string? NextRecordsUrl,
        [property: JsonPropertyName("records")] IReadOnlyList<T>? RecordsOrNull)
    {
        [JsonIgnore]
        public IReadOnlyList<T> Records => RecordsOrNull ?? [];
    }

    private sealed record CreateResult(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("success")] bool Success);

    private sealed record SalesforceError(
        [property: JsonPropertyName("errorCode")] string? ErrorCode,
        [property: JsonPropertyName("message")] string? Message);
}
