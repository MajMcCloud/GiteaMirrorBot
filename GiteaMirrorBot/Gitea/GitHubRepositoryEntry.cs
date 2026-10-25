using System.Text.Json.Serialization;

namespace GiteaBot.Gitea;

/// <summary>
/// Response of GET /api/github/repositories.
/// </summary>
public sealed class GitHubRepositoriesResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("repositories")]
    public List<GitHubRepositoryEntry> Repositories { get; set; } = [];
}

/// <summary>
/// An entry from GET /api/github/repositories. The <see cref="Id"/> is the key for
/// POST /api/job/mirror-repo and (via <see cref="Organization"/>) for POST /api/job/mirror-org.
/// </summary>
public sealed class GitHubRepositoryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>"owner/repo" – the reliable key for matching a GitHub link.</summary>
    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    /// <summary>Organization id when the repository belongs to an organization; otherwise null.</summary>
    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("mirroredLocation")]
    public string? MirroredLocation { get; set; }

    [JsonPropertyName("lastMirrored")]
    public DateTimeOffset? LastMirrored { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("destinationOrg")]
    public string? DestinationOrg { get; set; }

    [JsonPropertyName("sourceProvider")]
    public string? SourceProvider { get; set; }

    [JsonPropertyName("isPrivate")]
    public bool IsPrivate { get; set; }
}
