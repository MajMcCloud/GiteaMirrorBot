using GiteaBot.Services;
using GiteaBot.Telegram;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using TelegramBotBase.Base;
using TelegramBotBase.Form;

namespace GiteaBot.Forms;

/// <summary>
/// Entry point: routes private chats to the help form and groups to the group watcher.
/// Foreign groups are deliberately <b>not</b> routed to the watcher (TA-13): Telegram
/// delivers a bot updates for every chat it is a member of.
/// </summary>
public class StartForm : SplitterForm
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= BotServices.Current.LoggerFactory.CreateLogger<StartForm>();

    /// <summary>Chats whose rejection has already been logged (keeps the log from flooding per message).</summary>
    private static readonly ConcurrentDictionary<long, byte> _loggedRejections = new();

    public override async Task<bool> Open(MessageResult e)
    {
        //await NavigateTo(new HelpForm());
        return true;
    }

    public override async Task<bool> OpenGroup(MessageResult e)
    {
        var allowedGroupId = BotServices.Current.Settings.Telegram.GroupId;
        var chatId = ChatGuard.ChatIdOf(e);
        var reason = ChatGuard.Decide(chatId, allowedGroupId);

        if (reason != ChatGuard.Reason.Allowed)
        {
            // The session stays on the (inactive) StartForm: nothing happens in foreign groups.
            if (_loggedRejections.TryAdd(chatId, 0))
                Log.LogInformation(
                    "Not watching group {ChatId} ({Title}) – {Reason}.",
                    chatId, SafeChatTitle(), ChatGuard.Describe(reason));
            else
                Log.LogDebug("Ignored message from foreign group {ChatId}.", chatId);

            return true;
        }

        Log.LogInformation("Group session opened: {ChatId} ({Title})", e.DeviceId, SafeChatTitle());
        await NavigateTo(new GroupWatchForm());
        return true;
    }

    public override Task<bool> OpenSupergroup(MessageResult e) => OpenGroup(e);

    /// <summary>The title is only readable once the session is already attached to the chat.</summary>
    private string SafeChatTitle()
    {
        try
        {
            return Device?.GetChatTitle() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
