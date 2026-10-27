using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotBase;

namespace GiteaMirrorBot.Telegram;

/// <summary>
/// The central send point. Makes sure every message lands in the right forum topic
/// (requirement 8) – regardless of whether an active bot session already exists for that chat.
/// </summary>
public sealed class TopicSender
{
    private BotBase? _bot;

    /// <summary>Set by the bot host once the bot has been built.</summary>
    public void Attach(BotBase bot) => _bot = bot;

    public bool IsAttached => _bot is not null;

    /// <summary>
    /// Sends <paramref name="text"/> in chat <paramref name="chatId"/>.
    /// <paramref name="threadId"/> is the forum topic id (null = General topic).
    /// </summary>
    public async Task<Message?> SendAsync(
        long chatId,
        int? threadId,
        string text,
        ReplyMarkup? markup = null,
        int? replyTo = null,
        ParseMode parseMode = ParseMode.Html,
        bool silent = false)
    {
        if (_bot is null)
            throw new InvalidOperationException("TopicSender is not attached to the bot yet.");

        var reply = replyTo is null
            ? null
            : new ReplyParameters { MessageId = replyTo, ChatId = new ChatId(chatId) };

        // Get or create the session -> sending goes through the RequestDispatcher (rate limit/retry)
        // and the topic source is unambiguous (messageThreadId).
        var session = _bot.Sessions.GetSession(chatId) ?? await _bot.Sessions.StartSession(chatId);

        return await session.SendMessage(
            text,
            parseMode,
            reply,
            markup,
            messageThreadId: threadId,
            disableNotification: silent);
    }

    /// <summary>
    /// Overwrites message <paramref name="messageId"/> with <paramref name="text"/>.
    /// Used for the status message during a running Gitea operation.
    /// If the edit fails (message deleted, too little time since the last change,
    /// missing rights), <see langword="false"/> is returned – the caller then decides
    /// whether to resend the status message as a new message.
    /// </summary>
    public async Task<bool> EditAsync(long chatId, int messageId, string text, ParseMode parseMode = ParseMode.Html)
    {
        if (_bot is null || messageId <= 0)
            return false;

        try
        {
            var session = _bot.Sessions.GetSession(chatId) ?? await _bot.Sessions.StartSession(chatId);
            await session.Edit(messageId, text, markup: null, parseMode: parseMode);
            return true;
        }
        catch (ApiRequestException)
        {
            // Telegram rejects edits that do not change the content or come too
            // quickly after each other -> the status message is expendable.
            return false;
        }
    }

    public async Task<bool> DeleteAsync(long chatId, int messageId)
    {
        if (_bot is null || messageId <= 0)
            return false;

        try
        {
            var session = _bot.Sessions.GetSession(chatId) ?? await _bot.Sessions.StartSession(chatId);
            return await session.DeleteMessage(messageId);
        }
        catch (ApiRequestException)
        {
            // Message already deleted or no permission -> not critical.
            return false;
        }
    }

    /// <summary>Chat id of an incoming message.</summary>
    public static long ChatIdOf(Message message) => message.Chat.Id;

    /// <summary>Forum topic of an incoming message (null when no forum / General topic).</summary>
    public static int? ThreadIdOf(Message message)
        => message.MessageThreadId is > 0 ? message.MessageThreadId : null;
}
