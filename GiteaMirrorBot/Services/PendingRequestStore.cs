using System.Collections.Concurrent;
using GiteaMirrorBot.Analysis;

namespace GiteaMirrorBot.Services;

/// <summary>
/// One open question from the bot ("Back up?") that an inline button can answer.
/// </summary>
public sealed class PendingRequest
{
    public required string Id { get; init; }
    public required GitHubLink Link { get; init; }
    public required long ChatId { get; init; }
    public int? ThreadId { get; init; }
    public int QuestionMessageId { get; set; }
    /// <summary>Id of the "In progress" message that is updated while the operation runs.</summary>
    public int StatusMessageId { get; set; }
    public string RequestedBy { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
}

/// <summary>
/// In-memory storage for open questions. The inline button only carries a short id so
/// the 64-byte callback data limit is never exceeded (TA-06).
/// </summary>
public sealed class PendingRequestStore
{
    private readonly ConcurrentDictionary<string, PendingRequest> _items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of currently open questions.</summary>
    public int Count => _items.Count;

    /// <summary>Creates a short, low-collision id (12 hex characters).</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public PendingRequest Add(PendingRequest request)
    {
        _items[request.Id] = request;
        return request;
    }

    public PendingRequest? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return _items.TryGetValue(id, out var item) ? item : null;
    }

    public PendingRequest? Remove(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return _items.TryRemove(id, out var item) ? item : null;
    }

    /// <summary>True when a question for this link in this chat is already open.</summary>
    public bool IsAlreadyAsked(long chatId, string linkKey)
        => _items.Values.Any(p => p.ChatId == chatId && p.Link.Key.Equals(linkKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>Removes orphaned entries (older than <paramref name="maxAge"/>).</summary>
    public int Prune(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var removed = 0;

        foreach (var kv in _items.ToArray())
        {
            if (kv.Value.CreatedUtc >= cutoff)
                continue;

            if (_items.TryRemove(kv.Key, out _))
                removed++;
        }

        return removed;
    }
}
