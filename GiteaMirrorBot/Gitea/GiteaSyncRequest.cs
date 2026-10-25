using System.Text.Json.Serialization;
using GiteaBot.Analysis;

namespace GiteaBot.Gitea;

/// <summary>
/// Payload for POST /api/sync/repository.
/// Example: { "owner": "octocat", "repo": "hello-world" }
/// </summary>
public sealed class RepositorySyncRequest
{
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;

    public static RepositorySyncRequest From(GitHubLink link)
        => new() { Owner = link.Owner, Repo = link.Repo ?? string.Empty };
}

/// <summary>
/// Payload for POST /api/sync/organization.
/// Example: { "org": "my-org" }
/// role has been optional since v3.45.0 and is no longer sent – Gitea itself defaults
/// new organizations to "member".
/// </summary>
public sealed class OrganizationSyncRequest
{
    [JsonPropertyName("org")]
    public string Org { get; set; } = string.Empty;

    public static OrganizationSyncRequest From(GitHubLink link)
        => new() { Org = link.Owner };
}
