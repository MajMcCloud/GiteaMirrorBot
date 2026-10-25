using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GiteaBot.Analysis;
using GiteaBot.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GiteaBot.Gitea;

public sealed record GiteaResult(bool Success, int StatusCode, string Message, string? RawBody = null)
{
    public bool IsConflict => StatusCode == (int)HttpStatusCode.Conflict;
}

/// <summary>Result of GET /api/github/repositories (payload is null when the request failed).</summary>
public sealed record CatalogResult(bool Success, int StatusCode, string Message, GitHubRepositoriesResponse? Payload);

/// <summary>
/// HTTP client for the Gitea endpoints.
///   create      -> POST {BaseUrl}/api/sync/repository | /api/sync/organization
///   catalog     -> GET  {BaseUrl}/api/github/repositories
///   mirror job  -> POST {BaseUrl}/api/job/mirror-repo | /api/job/mirror-org
/// </summary>
public sealed class GiteaApiClient
{
    public const string RepositoryEndpoint = "/api/sync/repository";
    public const string OrganizationEndpoint = "/api/sync/organization";

    public const string RepositoriesEndpoint = "/api/github/repositories";
    public const string MirrorRepositoryJobEndpoint = "/api/job/mirror-repo";
    public const string MirrorOrganizationJobEndpoint = "/api/job/mirror-org";

    /// <summary>Name of the header Gitea expects the API key in.</summary>
    public const string ApiKeyHeader = "x-api-key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<GiteaApiClient> _log;

    public GiteaApiClient(IHttpClientFactory httpClientFactory, IOptions<AppSettings> settings, ILogger<GiteaApiClient> log)
    {
        _http = httpClientFactory.CreateClient("gitea");
        _log = log;

        var baseUrl = settings.Value.Gitea.BaseUrl;
        if (!string.IsNullOrWhiteSpace(baseUrl))
            _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

        var apiKey = settings.Value.Gitea.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyHeader, apiKey.Trim());
        else
            _log.LogWarning("Gitea:ApiKey is empty – requests will be sent without the \"{Header}\" header.", ApiKeyHeader);
    }

    // -------------------------------------------------------------- Create (sync)

    public Task<GiteaResult> SyncRepositoryAsync(GitHubLink link, CancellationToken ct = default)
    {
        if (link.Repo is null)
            return Task.FromResult(new GiteaResult(false, 0, "Link does not contain a repository."));

        return PostAsync(RepositoryEndpoint, RepositorySyncRequest.From(link), link.DisplayName, ct);
    }

    public Task<GiteaResult> SyncOrganizationAsync(GitHubLink link, CancellationToken ct = default)
        => PostAsync(OrganizationEndpoint, OrganizationSyncRequest.From(link), link.DisplayName, ct);

    /// <summary>Routes to the proper endpoint depending on the link kind (TA-08).</summary>
    public async Task<GiteaResult> SyncAsync(GitHubLink link, CancellationToken ct = default)
        => link.Kind == LinkKind.Repository
            ? await SyncRepositoryAsync(link, ct)
            : await SyncOrganizationAsync(link, ct);

    // -------------------------------------------------------------- Catalog

    /// <summary>
    /// GET /api/github/repositories – returns the repositories Gitea knows about, including
    /// the ids the mirror jobs need.
    /// </summary>
    public async Task<CatalogResult> GetRepositoriesAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, RepositoriesEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _http.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            var status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Catalog unavailable: HTTP {Status}: {Body}", status, Truncate(raw));
                return new CatalogResult(false, status, $"Gitea replied with HTTP {status}.", null);
            }

            var payload = JsonSerializer.Deserialize<GitHubRepositoriesResponse>(raw, JsonOptions);

            if (payload is null)
                return new CatalogResult(false, status, "The response from /api/github/repositories was empty.", null);

            if (!payload.Success)
            {
                _log.LogWarning("Catalog reported as failed: {Message}", payload.Message);
                return new CatalogResult(false, status, payload.Message, payload);
            }

            _log.LogDebug("Catalog loaded: {Count} repositories.", payload.Repositories.Count);
            return new CatalogResult(true, status, payload.Message, payload);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _log.LogError(ex, "Catalog unavailable.");
            return new CatalogResult(false, 0, $"Gitea is unreachable ({ex.Message}).", null);
        }
    }

    // -------------------------------------------------------------- Mirror jobs

    public Task<GiteaResult> MirrorRepositoriesAsync(IEnumerable<string> repositoryIds, CancellationToken ct = default)
    {
        var ids = Clean(repositoryIds);
        if (ids.Count == 0)
            return Task.FromResult(new GiteaResult(false, 0, "No repository ids supplied."));

        return PostAsync(MirrorRepositoryJobEndpoint, new MirrorRepositoryJobRequest(ids), $"{ids.Count} Repo(s)", ct);
    }

    public Task<GiteaResult> MirrorOrganizationsAsync(IEnumerable<string> organizationIds, CancellationToken ct = default)
    {
        var ids = Clean(organizationIds);
        if (ids.Count == 0)
            return Task.FromResult(new GiteaResult(false, 0, "No organization ids supplied."));

        return PostAsync(MirrorOrganizationJobEndpoint, new MirrorOrganizationJobRequest(ids), $"{ids.Count} Org(s)", ct);
    }

    private static List<string> Clean(IEnumerable<string> ids)
        => ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // -------------------------------------------------------------- Sync response

    /// <summary>
    /// Reads a sync response (POST /api/sync/*). Gitea returns the stored record together
    /// with its id both on 200 and on 409 – that id is the key for the mirror jobs.
    /// Returns null when the body is missing or is not JSON.
    /// </summary>
    public static SyncResponse? ParseSyncResponse(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SyncResponse>(rawBody, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // -------------------------------------------------------------- Intern

    private async Task<GiteaResult> PostAsync(string path, object payload, string target, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(payload, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _log.LogInformation("Gitea call: {Target} -> {Path} {Body}", target, path, Truncate(body));

        try
        {
            using var response = await _http.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            var status = (int)response.StatusCode;

            var human = response.StatusCode switch
            {
                HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.Accepted or HttpStatusCode.NoContent
                    => $"Queued (HTTP {status}).",
                HttpStatusCode.Conflict => "Target already exists in Gitea (HTTP 409).",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"Access denied (HTTP {status}).",
                _ => $"Gitea replied with HTTP {status}."
            };

            // On errors Gitea returns a JSON message (e.g. "user account is not an organization"
            // with 404 on POST /api/sync/organization) – pass it straight through to the user.
            var detail = ExtractErrorDetail(raw);
            if (!response.IsSuccessStatusCode && detail is not null)
                human += $" Gitea: \"{detail}\"";

            if (response.IsSuccessStatusCode)
                _log.LogInformation("Gitea call succeeded: {Target} -> {Status}", target, status);
            else
                _log.LogWarning("Gitea call failed: {Target} -> HTTP {Status}: {Body}", target, status, Truncate(raw));

            return new GiteaResult(response.IsSuccessStatusCode, status, human, raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogError(ex, "Gitea call not possible: {Target}", target);
            return new GiteaResult(false, 0, $"Gitea is unreachable ({ex.Message}).");
        }
    }

    /// <summary>Returns "error" (or "message" as a fallback) from a JSON error body, otherwise null.</summary>
    private static string? ExtractErrorDetail(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var text =
                (doc.RootElement.TryGetProperty("error", out var err) ? err.GetString() : null)
                ?? (doc.RootElement.TryGetProperty("message", out var msg) ? msg.GetString() : null);

            return string.IsNullOrWhiteSpace(text) ? null : Truncate(text.Trim());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string? s)
        => string.IsNullOrEmpty(s) ? "-" : (s.Length <= 400 ? s : s[..400] + "…");
}
