using System.Text.Json.Serialization;

namespace GiteaBot.Web.Models;

/// <summary>
/// Payload that Gitea sends to our endpoint (specified by the client).
/// </summary>
public class Root
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
