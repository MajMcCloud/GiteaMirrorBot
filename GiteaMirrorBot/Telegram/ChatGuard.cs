using Telegram.Bot.Types;
using TelegramBotBase.Base;

namespace GiteaBot.Telegram;

/// <summary>
/// The soft gate that decides whether the bot may act in a chat (TA-13).
/// Without this check the bot would react in <b>every</b> group it is a member of and
/// in every private chat – Telegram delivers a bot updates for all of its chats.
/// Exactly the group from <c>Telegram:GroupId</c> is allowed.
/// </summary>
public static class ChatGuard
{
    /// <summary>Outcome of the check – used for traceable log entries.</summary>
    public enum Reason
    {
        /// <summary>The chat is the configured target group; message/callback is processed.</summary>
        Allowed,

        /// <summary><c>Telegram:GroupId</c> is not set; the bot deliberately processes nothing.</summary>
        NotConfigured,

        /// <summary>A different group/channel than configured.</summary>
        OtherGroup,

        /// <summary>Private chat (positive chat id); the bot is not active there.</summary>
        PrivateChat,

        /// <summary>Chat id could not be determined.</summary>
        Unknown
    }

    /// <summary>True when <paramref name="chatId"/> is the configured target group.</summary>
    public static bool IsAllowed(long chatId, long allowedGroupId)
        => Decide(chatId, allowedGroupId) == Reason.Allowed;

    public static Reason Decide(long chatId, long allowedGroupId)
    {
        if (allowedGroupId == 0)
            return Reason.NotConfigured;

        if (chatId == 0)
            return Reason.Unknown;

        if (chatId == allowedGroupId)
            return Reason.Allowed;

        // Groups, supergroups and channels have negative chat ids, private chats positive.
        return chatId > 0 ? Reason.PrivateChat : Reason.OtherGroup;
    }

    public static Reason Decide(Message? message, long allowedGroupId)
        => Decide(message?.Chat?.Id ?? 0, allowedGroupId);

    public static Reason Decide(MessageResult message, long allowedGroupId)
        => Decide(ChatIdOf(message), allowedGroupId);

    /// <summary>
    /// Checks an inline-button callback: allowed only from the configured group and – when the
    /// related request is still known – only from the chat the question was posted in.
    /// Otherwise a forwarded question message in a foreign chat could trigger a backup.
    /// </summary>
    /// <param name="callbackChatId">Chat the callback came from.</param>
    /// <param name="allowedGroupId">Configured target group (<c>Telegram:GroupId</c>).</param>
    /// <param name="requestChatId">Chat of the open request; <c>null</c> when no request is open anymore.</param>
    public static bool IsCallbackAllowed(long callbackChatId, long allowedGroupId, long? requestChatId)
    {
        if (!IsAllowed(callbackChatId, allowedGroupId))
            return false;

        return requestChatId is null || requestChatId.Value == callbackChatId;
    }

    /// <summary>
    /// Chat id of an incoming message or callback. TelegramBotBase keeps one session per
    /// chat via <c>DeviceId</c>; the associated Message is only set when the buttons
    /// message is known.
    /// </summary>
    public static long ChatIdOf(MessageResult message)
    {
        var fromMessage = message.Message?.Chat?.Id ?? 0;
        return fromMessage != 0 ? fromMessage : message.DeviceId;
    }

    /// <summary>Human-readable hint text for logs and answers.</summary>
    public static string Describe(Reason reason) => reason switch
    {
        Reason.Allowed => "the configured group",
        Reason.NotConfigured => "Telegram:GroupId is not configured",
        Reason.OtherGroup => "a different group than Telegram:GroupId",
        Reason.PrivateChat => "a private chat",
        Reason.Unknown => "chat id could not be determined",
        _ => reason.ToString()
    };
}
