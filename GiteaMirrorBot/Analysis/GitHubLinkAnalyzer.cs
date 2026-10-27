using System.Text.RegularExpressions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace GiteaMirrorBot.Analysis;

/// <summary>
/// Finds GitHub links in Telegram messages and decides whether the link points to a
/// repository or to an organization/account (TA-05).
/// </summary>
public static partial class GitHubLinkAnalyzer
{
    /// <summary>
    /// First path segments that can never stand for a repository/organization.
    /// </summary>
    private static readonly HashSet<string> ReservedOwners = new(StringComparer.OrdinalIgnoreCase)
    {
        "orgs", "topics", "trending", "collections", "features", "marketplace", "explore",
        "notifications", "new", "settings", "login", "join", "signup", "signin", "logout",
        "pricing", "sponsors", "sponsor", "about", "blog", "enterprise", "customer-story",
        "security", "status", "terms", "privacy", "site-policy", "contact", "help", "docs",
        "readme", "languages", "showcases", "releases", "issues", "pulls", "discussions",
        "codespaces", "copilot", "gallery", "installations", "apps", "verified"
    };

    /// <summary>Paths inside a repository that get trimmed off.</summary>
    private static readonly HashSet<string> RepoSubPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "issues", "pull", "pulls", "tree", "blob", "commit", "commits", "compare", "releases",
        "tags", "branches", "labels", "milestones", "actions", "wiki", "projects", "security",
        "network", "forks", "subscription", "settings", "stargazers", "watchers", "contributors",
        "activity", "discussions", "codespaces", "deployments", "environments", "packages",
        "discourse", "edit", "new", "import", "blame", "raw", "archive", "git", "pulls"
    };

    [GeneratedRegex(
        @"(?:https?://)?(?:www\.)?github\.com/(?<path>[A-Za-z0-9._~%!\$&'\(\)\*\+,\-=:@/\#\?]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GitHubUrlRegex();

    /// <summary>
    /// Extracts every GitHub link of a message – from entities (Url/TextUrl) and from raw
    /// text – without duplicates, in order of first appearance.
    /// </summary>
    public static IReadOnlyList<GitHubLink> Extract(Message? message)
    {
        if (message is null)
            return [];

        var candidates = new List<string>();

        // 1.) Entities are more reliable than regex (handles markdown links, @mentions, ...).
        var entities = message.Entities;
        if (entities is not null && entities.Length > 0)
        {
            foreach (var entity in entities)
            {
                if (entity.Type is not (MessageEntityType.Url or MessageEntityType.TextLink))
                    continue;

                var raw = entity.Type == MessageEntityType.TextLink
                    ? entity.Url
                    : GetEntityText(message.Text, entity);

                if (!string.IsNullOrWhiteSpace(raw))
                    candidates.Add(raw.Trim());
            }
        }

        // 2.) Cover the raw text (links without an entity, e.g. "github.com/a/b" or code snippets).
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            foreach (Match m in GitHubUrlRegex().Matches(message.Text))
                candidates.Add(m.Value);
        }

        return ExtractFromText(string.Join("\n", candidates));
    }

    /// <summary>Plain-text variant (also usable for webhook/test purposes).</summary>
    public static IReadOnlyList<GitHubLink> ExtractFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var result = new List<GitHubLink>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in GitHubUrlRegex().Matches(text))
        {
            var link = Parse(match.Value);
            if (link is null)
                continue;

            if (seen.Add(link.Key))
                result.Add(link);
        }

        return result;
    }

    /// <summary>
    /// Splits a GitHub URL. Everything after owner[/repo] is trimmed off
    /// (/issues/3, /tree/main/src/x.cs, /pull/1/files, ...).
    /// </summary>
    public static GitHubLink? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var raw = url.Trim().TrimEnd('.').Trim();

        // Strip the scheme so host and path can be told apart unambiguously.
        var schemeIdx = raw.IndexOf("://", StringComparison.Ordinal);
        var rest = schemeIdx >= 0 ? raw[(schemeIdx + 3)..] : raw;

        var slash = rest.IndexOf('/');
        if (slash < 0 || slash == rest.Length - 1)
            return null; // no path -> neither a repository nor an organization

        var host = rest[..slash];
        var afterHost = rest[slash..];

        // gist.github.com, subdomain.github.com, gitea.* etc. are not candidates.
        if (!host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
            return null;

        if (host.Length > "github.com".Length &&
            !host.EndsWith("www.github.com", StringComparison.OrdinalIgnoreCase))
            return null;

        var segments = afterHost
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Uri.UnescapeDataString(s))
            .ToList();

        if (segments.Count == 0)
            return null;

        var owner = Clean(segments[0]);
        if (owner.Length == 0 || ReservedOwners.Contains(owner))
            return null;

        // Organization / account
        if (segments.Count == 1)
            return new GitHubLink(LinkKind.Organization, owner, null, raw);

        var repo = Clean(segments[1]);
        if (repo.Length == 0 || RepoSubPaths.Contains(repo))
            return new GitHubLink(LinkKind.Organization, owner, null, raw);

        return new GitHubLink(LinkKind.Repository, owner, repo, raw);
    }

    private static string Clean(string segment)
    {
        var s = segment;

        // Remove query/fragment and markdown leftovers.
        var cut = s.IndexOfAny(['?', '#', ')', ']', '<', '>', '"', '\'']);
        if (cut >= 0)
            s = s[..cut];

        s = s.TrimEnd('.', ',', ':', ';', '!');

        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            s = s[..^4];

        return s;
    }

    private static string? GetEntityText(string? text, MessageEntity entity)
    {
        if (string.IsNullOrEmpty(text) || entity.Offset < 0)
            return null;

        var start = Math.Min(entity.Offset, text.Length);
        var end = Math.Min(start + Math.Max(entity.Length, 0), text.Length);

        return start >= end ? null : text[start..end];
    }
}
