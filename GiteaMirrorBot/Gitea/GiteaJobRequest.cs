using System.Text.Json.Serialization;

namespace GiteaMirrorBot.Gitea;

/// <summary>
/// Payload for POST /api/job/mirror-repo.
/// Example: { "repositoryIds": ["9b2f..."] }
/// </summary>
public sealed class MirrorRepositoryJobRequest
{
    [JsonPropertyName("repositoryIds")]
    public List<string> RepositoryIds { get; set; } = [];

    public MirrorRepositoryJobRequest()
    {
    }

    public MirrorRepositoryJobRequest(IEnumerable<string> ids)
        => RepositoryIds = ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
}

/// <summary>
/// Payload for POST /api/job/mirror-org.
/// Example: { "organizationIds": ["7c1a..."] }
/// </summary>
public sealed class MirrorOrganizationJobRequest
{
    [JsonPropertyName("organizationIds")]
    public List<string> OrganizationIds { get; set; } = [];

    public MirrorOrganizationJobRequest()
    {
    }

    public MirrorOrganizationJobRequest(IEnumerable<string> ids)
        => OrganizationIds = ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
}
