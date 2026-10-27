using System.Text.Json.Serialization;

namespace GiteaMirrorBot.Gitea;

/// <summary>
/// Response of POST /api/sync/repository or POST /api/sync/organization.
/// Gitea returns the stored record both on 200 and on 409 ("Already tracked"), and its
/// id is the key for the mirror jobs (POST /api/job/mirror-repo | /api/job/mirror-org).
/// </summary>
public sealed class SyncResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>Error description on 4xx/5xx, e.g. when a user account is not an organization.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("repository")]
    public SyncedRecord? Repository { get; set; }

    [JsonPropertyName("organization")]
    public SyncedRecord? Organization { get; set; }
}

/// <summary>A stored repository or organization record from the sync response.</summary>
public sealed class SyncedRecord
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }
}
