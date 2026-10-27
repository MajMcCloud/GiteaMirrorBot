namespace GiteaMirrorBot.Analysis;

public enum LinkKind
{
    /// <summary>https://github.com/{owner} – an organization or account.</summary>
    Organization,

    /// <summary>https://github.com/{owner}/{repo} – a repository.</summary>
    Repository
}

/// <summary>A normalized GitHub link extracted from a message.</summary>
public sealed record GitHubLink(LinkKind Kind, string Owner, string? Repo, string OriginalUrl)
{
    /// <summary>Stable key used for duplicate detection (case-insensitive path).</summary>
    public string Key => Repo is null
        ? $"org:{Owner.ToLowerInvariant()}"
        : $"repo:{Owner.ToLowerInvariant()}/{Repo.ToLowerInvariant()}";

    public string DisplayName => Repo is null ? Owner : $"{Owner}/{Repo}";

    /// <summary>The canonical GitHub URL of the backup target.</summary>
    public string CanonicalUrl => Repo is null
        ? $"https://github.com/{Owner}"
        : $"https://github.com/{Owner}/{Repo}";

    public string TypeLabel => Kind == LinkKind.Repository ? "Repository" : "Organization";
}
