using GiteaBot.Analysis;
using GiteaBot.Configuration;
using GiteaBot.Telegram;
using Microsoft.Extensions.Logging;

namespace GiteaBot.Services;

/// <summary>
/// Access to the DI-built services from the Telegram forms.
/// Forms are created by the TelegramBotBase form factory (not by DI), which is why the
/// composition root is stored here once at startup.
/// </summary>
public sealed class BotServices
{
    private static BotServices? _current;

    public static BotServices Current => _current ?? throw new InvalidOperationException("BotServices was not initialized.");

    public required TopicSender Sender { get; init; }
    public required BackupService Backup { get; init; }
    public required PendingRequestStore Store { get; init; }
    public required AppSettings Settings { get; init; }
    public required ILoggerFactory LoggerFactory { get; init; }

    /// <summary>Call from startup before the bot starts.</summary>
    public void Publish() => _current = this;
}
