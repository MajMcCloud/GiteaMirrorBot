using GiteaMirrorBot.Configuration;
using GiteaMirrorBot.Telegram;
using GiteaMirrorBot.Web.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types.Enums;

namespace GiteaMirrorBot.Services;

/// <summary>
/// Forwards a notification received from Gitea into the group and there into the
/// GitHub topic (TA-12).
/// </summary>
public sealed class NotificationRouter
{
    private readonly TopicSender _sender;
    private readonly AppSettings _settings;
    private readonly ILogger<NotificationRouter> _log;

    public NotificationRouter(TopicSender sender, IOptions<AppSettings> settings, ILogger<NotificationRouter> log)
    {
        _sender = sender;
        _settings = settings.Value;
        _log = log;
    }

    public async Task RouteAsync(Root notification, CancellationToken ct = default)
    {
        var groupId = _settings.Telegram.GroupId;

        if (groupId == 0)
        {
            _log.LogWarning("Gitea notification discarded: Telegram:GroupId is not configured.");
            return;
        }

        var threadId = _settings.Telegram.TopicId > 0 ? _settings.Telegram.TopicId : (int?)null;
        var text = Format(notification);

        await _sender.SendAsync(groupId, threadId, text, parseMode: ParseMode.Html);

        _log.LogInformation(
            "Gitea notification forwarded: chat {GroupId}, topic {TopicId}, type {Type}",
            groupId, threadId, notification.Type);
    }

    /// <summary>HTML formatting of the Gitea notification.</summary>
    public static string Format(Root n)
    {
        var lines = new List<string>
        {
            $"<b>{Escape(string.IsNullOrWhiteSpace(n.Title) ? "Gitea" : n.Title)}</b>"
        };

        if (!string.IsNullOrWhiteSpace(n.Message))
            lines.Add(Escape(n.Message).Replace("\n", Environment.NewLine));

        var meta = new List<string>();

        if (!string.IsNullOrWhiteSpace(n.Type))
            meta.Add($"Type: <code>{Escape(n.Type)}</code>");

        if (n.Timestamp != default)
            meta.Add($"Time: {n.Timestamp.ToLocalTime():dd.MM.yyyy HH:mm:ss}");

        if (meta.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(string.Join("  ·  ", meta));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
