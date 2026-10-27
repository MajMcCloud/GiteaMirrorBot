using GiteaMirrorBot.Analysis;
using Microsoft.Extensions.Logging;

namespace GiteaMirrorBot.Gitea;

/// <summary>
/// What can additionally be mirrored after the record was created – resolved from the
/// catalog (GET /api/github/repositories).
/// </summary>
public sealed record MirrorPlan(
    bool Resolved,
    string Reason,
    IReadOnlyList<string> RepositoryIds,
    IReadOnlyList<string> OrganizationIds)
{
    public bool HasWork => RepositoryIds.Count > 0 || OrganizationIds.Count > 0;

    public static MirrorPlan Unresolved(string reason)
        => new(false, reason, [], []);
}

/// <summary>
/// Reads Gitea's repository catalog and translates a GitHub link into the ids the mirror
/// jobs expect (/api/job/mirror-repo, /api/job/mirror-org).
/// The catalog is cached briefly so repeated backups do not reload it in full every time.
/// </summary>
public sealed class GiteaCatalogService
{
    /// <summary>The catalog changes right after a record is created – hence the short lifetime.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly GiteaApiClient _gitea;
    private readonly ILogger<GiteaCatalogService> _log;

    private GitHubRepositoriesResponse? _cache;
    private DateTime _cacheExpiresUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GiteaCatalogService(GiteaApiClient gitea, ILogger<GiteaCatalogService> log)
    {
        _gitea = gitea;
        _log = log;
    }

    public async Task<MirrorPlan> PlanAsync(GitHubLink link, CancellationToken ct = default)
    {
        var catalog = await GetCatalogAsync(ct);
        if (catalog is null)
            return MirrorPlan.Unresolved("Gitea's repository catalog could not be fetched.");

        return link.Kind == LinkKind.Repository
            ? PlanForRepository(link, catalog)
            : PlanForOrganization(link, catalog);
    }

    /// <summary>Drop the cached catalog – e.g. right after a repository has been created.</summary>
    public void Invalidate()
    {
        _cache = null;
        _cacheExpiresUtc = DateTime.MinValue;
    }

    // ------------------------------------------------------------------ Lookup

    // Public for the tests: pure lookup logic without HTTP.
    public static MirrorPlan PlanForRepository(GitHubLink link, GitHubRepositoriesResponse catalog)
    {
        var entry = FindRepository(link, catalog.Repositories);

        if (entry is null)
            return MirrorPlan.Unresolved($"{link.DisplayName} is not part of the Gitea catalog.");

        if (string.IsNullOrWhiteSpace(entry.Id))
            return MirrorPlan.Unresolved($"{link.DisplayName} has no id in the catalog.");

        return new MirrorPlan(true, string.Empty, [entry.Id], []);
    }

    public static MirrorPlan PlanForOrganization(GitHubLink link, GitHubRepositoriesResponse catalog)
    {
        var owned = catalog.Repositories
            .Where(r => string.Equals(r.Owner, link.Owner, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var orgIds = Distinct(owned.Select(r => r.Organization));

        if (orgIds.Count == 0)
        {
            return owned.Count == 0
                ? MirrorPlan.Unresolved($"Organization {link.Owner} has no repositories in the Gitea catalog.")
                : MirrorPlan.Unresolved(
                    $"{owned.Count} repository(ies) of {link.Owner} in the catalog, but no organization id set.");
        }

        // The org job mirrors its repositories itself – also sending the repository ids
        // would just be duplicate work.
        return new MirrorPlan(true, string.Empty, [], orgIds);
    }

    private static GitHubRepositoryEntry? FindRepository(GitHubLink link, IEnumerable<GitHubRepositoryEntry> entries)
    {
        var fullName = link.DisplayName;

        return entries.FirstOrDefault(e => string.Equals(e.FullName, fullName, StringComparison.OrdinalIgnoreCase))
               ?? entries.FirstOrDefault(e =>
                    string.Equals(e.Owner, link.Owner, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Name, link.Repo, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> Distinct(IEnumerable<string?> values)
        => values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // ------------------------------------------------------------------ Catalog fetch

    private async Task<GitHubRepositoriesResponse?> GetCatalogAsync(CancellationToken ct)
    {
        if (_cache is not null && DateTime.UtcNow < _cacheExpiresUtc)
            return _cache;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cache is not null && DateTime.UtcNow < _cacheExpiresUtc)
                return _cache;

            var result = await _gitea.GetRepositoriesAsync(ct);
            if (!result.Success || result.Payload is null)
                return null;

            _cache = result.Payload;
            _cacheExpiresUtc = DateTime.UtcNow + CacheLifetime;
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }
}
