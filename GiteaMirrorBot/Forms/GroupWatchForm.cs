using GiteaMirrorBot.Analysis;
using GiteaMirrorBot.Services;
using GiteaMirrorBot.Telegram;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotBase;
using TelegramBotBase.Base;
using TelegramBotBase.Form;

namespace GiteaMirrorBot.Forms;

/// <summary>
/// Reads every message of the group (TA-04), detects GitHub links (TA-05) and asks via an
/// inline button whether a backup should be queued through Gitea (TA-06). Answers are always
/// sent in the topic of the original message (TA-10).
/// </summary>
public class GroupWatchForm : GroupForm
{
    /// <summary>Callback prefixes. Deliberately short: callback data may be at most 64 bytes.</summary>
    public const string ConfirmPrefix = "giteamirrorbot:ok:";
    public const string CancelPrefix = "giteamirrorbot:no:";

    private static ILogger? _log;
    private static ILogger Log => _log ??= BotServices.Current.LoggerFactory.CreateLogger<GroupWatchForm>();

    private readonly BotServices _services = BotServices.Current;

    public override async Task OnMessage(MessageResult e)
    {
        await HandleAsync(e.Message);
    }

    public override async Task OnMessageEdit(MessageResult e)
    {
        // Also evaluate links added afterwards (deduplicated via PendingRequestStore).
        await HandleAsync(e.Message);
    }

    public override async Task Action(MessageResult message)
    {
        if (message.Handled)
            return;

        var raw = message.RawData;
        if (string.IsNullOrEmpty(raw))
            return;

        var isConfirm = raw.StartsWith(ConfirmPrefix, StringComparison.Ordinal);
        var isCancel = raw.StartsWith(CancelPrefix, StringComparison.Ordinal);

        if (!isConfirm && !isCancel)
            return;

        message.Handled = true;

        var id = raw[(isConfirm ? ConfirmPrefix.Length : CancelPrefix.Length)..];
        var request = _services.Store.Find(id);

        // TA-13: buttons may only work in the configured group. A forwarded question
        // message in another chat would otherwise have triggered a backup.
        var callbackChatId = ChatGuard.ChatIdOf(message);

        if (!IsCallbackAllowed(callbackChatId, request))
        {
            Log.LogWarning(
                "Discarded callback \"{Raw}\" from chat {ChatId} – {Reason}.",
                raw, callbackChatId, ChatGuard.Describe(ChatGuard.Decide(callbackChatId, _services.Settings.Telegram.GroupId)));

            await message.ConfirmAction("The bot is not active in this chat.", showAlert: true);
            return;
        }

        // Answer ConfirmAction exactly once – otherwise Telegram reports "query already answered".
        await message.ConfirmAction(
            request is null ? "This request is no longer open." : null,
            showAlert: request is null);

        if (request is null)
            return;

        if (isConfirm)
        {
            // Gitea job in the background: the message loop stays responsive for the group.
            _ = Task.Run(async () =>
            {
                try
                {
                    await _services.Backup.ConfirmAsync(request);
                }
                catch (Exception ex)
                {
                    Log.LogError(ex, "Backup failed: {Link}", request.Link.DisplayName);
                }
            });
        }
        else
        {
            try
            {
                await _services.Backup.CancelAsync(request);
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Cancel failed: {Link}", request.Link.DisplayName);
            }
        }
    }

    // ------------------------------------------------------------------ TA-05 / TA-06

    private async Task HandleAsync(Message? message)
    {
        if (message is null)
            return;

        // TA-13: only the configured group is watched.
        var reason = ChatGuard.Decide(message, _services.Settings.Telegram.GroupId);
        if (reason != ChatGuard.Reason.Allowed)
        {
            Log.LogDebug(
                "Ignored message from chat {ChatId} – {Reason}.",
                TopicSender.ChatIdOf(message), ChatGuard.Describe(reason));
            return;
        }

        // Never evaluate our own messages (feedback loop with our own answers).
        if (message.From is null || message.From.IsBot)
            return;

        var links = GitHubLinkAnalyzer.Extract(message);
        if (links.Count == 0)
            return;

        var chatId = TopicSender.ChatIdOf(message);
        var threadId = TopicSender.ThreadIdOf(message);
        var askedBy = DisplayName(message.From);

        foreach (var link in links)
        {
            if (_services.Store.IsAlreadyAsked(chatId, link.Key))
            {
                Log.LogDebug("Question for {Link} in chat {ChatId} is already open.", link.DisplayName, chatId);
                continue;
            }

            await AskAsync(link, chatId, threadId, message.MessageId, askedBy);
        }
    }

    private async Task AskAsync(GitHubLink link, long chatId, int? threadId, int originMessageId, string askedBy)
    {
        var id = PendingRequestStore.NewId();

        var request = _services.Store.Add(new PendingRequest
        {
            Id = id,
            Link = link,
            ChatId = chatId,
            ThreadId = threadId,
            QuestionMessageId = 0,
            RequestedBy = askedBy
        });

        var text =
            $"<b>Back up this {link.TypeLabel} through Gitea?</b>" + Environment.NewLine +
            Environment.NewLine +
            $"Detected: <a href=\"{link.CanonicalUrl}\">{Escape(link.DisplayName)}</a>" + Environment.NewLine +
            $"Type: {link.TypeLabel}" + Environment.NewLine +
            $"Gitea: <code>{EndpointFor(link)}</code>" + Environment.NewLine +
            $"then: <code>{MirrorEndpointFor(link)}</code>" + Environment.NewLine +
            Environment.NewLine +
            "Should the backup be queued?";

        var bf = new ButtonForm();
        bf.AddButtonRow(new ButtonBase(
            link.Kind == LinkKind.Repository ? "Backup repository" : "Backup organization",
            ConfirmPrefix + id));
        bf.AddButtonRow(new ButtonBase("Cancel", CancelPrefix + id));

        var sent = await _services.Sender.SendAsync(
            chatId,
            threadId,
            text,
            (InlineKeyboardMarkup)bf,
            replyTo: originMessageId,
            parseMode: ParseMode.Html,
            silent: true);

        if (sent is not null)
            request.QuestionMessageId = sent.MessageId;

        Log.LogInformation(
            "Asked for confirmation: {Link} ({Kind}) in chat {ChatId} / topic {ThreadId} (message {MessageId})",
            link.DisplayName, link.Kind, chatId, threadId, request.QuestionMessageId);
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>
    /// A callback is only allowed when it comes from the configured group and – if the
    /// request is still known – was posted exactly where the question message lives.
    /// </summary>
    private bool IsCallbackAllowed(long callbackChatId, PendingRequest? request)
        => ChatGuard.IsCallbackAllowed(callbackChatId, _services.Settings.Telegram.GroupId, request?.ChatId);

    private string EndpointFor(GitHubLink link)
        => _services.Settings.Gitea.BaseUrl.TrimEnd('/') + (link.Kind == LinkKind.Repository
            ? Gitea.GiteaApiClient.RepositoryEndpoint
            : Gitea.GiteaApiClient.OrganizationEndpoint);

    private string MirrorEndpointFor(GitHubLink link)
        => link.Kind == LinkKind.Repository
            ? Gitea.GiteaApiClient.MirrorRepositoryJobEndpoint
            : Gitea.GiteaApiClient.MirrorOrganizationJobEndpoint;

    private static string DisplayName(User user)
        => string.IsNullOrWhiteSpace(user.FirstName) ? user.Username ?? user.Id.ToString() : user.FirstName;

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
