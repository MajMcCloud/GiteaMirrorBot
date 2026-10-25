using GiteaBot.Configuration;
using GiteaBot.Forms;
using GiteaBot.Services;
using GiteaBot.Telegram;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types.Enums;
using TelegramBotBase;
using TelegramBotBase.Builder;
using TelegramBotBase.Commands;

namespace GiteaBot;

/// <summary>
/// Hosts the Telegram bot in the same process as the Minimal API (SKILL.md §17).
/// </summary>
public sealed class BotHostedService : IHostedService
{
    private readonly ILogger<BotHostedService> _log;
    private readonly TopicSender _sender;
    private readonly PendingRequestStore _store;
    private readonly AppSettings _settings;
    private BotBase? _bot;

    public BotHostedService(
        IOptions<AppSettings> settings,
        TopicSender sender,
        PendingRequestStore store,
        ILogger<BotHostedService> log)
    {
        _settings = settings.Value;
        _sender = sender;
        _store = store;
        _log = log;
    }

    public BotBase? Bot => _bot;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bot = BotBaseBuilder
            .Create()
            .WithAPIKey(_settings.Telegram.ApiKey)
            .DefaultMessageLoop()
            .WithStartForm<StartForm>()
            .NoProxy(throwPendingUpdates: true)
            .CustomCommands(a =>
            {
                a.Start("Starts the bot");
                a.Add("help", "Shows how to use the bot");
                a.Add("status", "Shows bot and endpoint status");
            })
            .NoSerialization()
            .UseEnglish()
            .UseSingleThread()
            .Build();

        bot.Exception += (_, e) =>
            _log.LogError(e.Error, "Bot exception (chat {DeviceId}, command {Command})", e.DeviceId, e.Command);

        bot.UnhandledCall += (_, e) =>
            _log.LogWarning("Unhandled callback: {RawData} (chat {DeviceId})", e.RawData, e.DeviceId);

        bot.BotCommand += async (_, e) =>
        {
            if (e.Command != "/status")
                return;

            // TA-13: Status output contains chat id and webhook address – only in the target group.
            var chatId = e.OriginalMessage?.Chat?.Id ?? e.DeviceId;
            var reason = ChatGuard.Decide(chatId, _settings.Telegram.GroupId);

            if (reason != ChatGuard.Reason.Allowed)
            {
                _log.LogInformation("Discarded /status from chat {ChatId} - {Reason}.", chatId, ChatGuard.Describe(reason));
                e.Handled = true;
                return;
            }

            await e.Device.Send(BuildStatusText(), replyTo: e.Device.LastMessageId, parseMode: ParseMode.Html);
            e.Handled = true;
        };

        _sender.Attach(bot);
        _bot = bot;

        try
        {
            await bot.UploadBotCommands();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Bot commands could not be uploaded (polling continues).");
        }

        await bot.Start();
        _log.LogInformation("Telegram bot is running.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_bot is null)
            return;

        var pruned = _store.Prune(TimeSpan.FromHours(2));
        if (pruned > 0)
            _log.LogInformation("Discarded {Count} orphaned pending requests.", pruned);

        try
        {
            await _bot.Stop();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Bot could not be shut down cleanly.");
        }

        _log.LogInformation("Telegram bot stopped.");
    }

    private string BuildStatusText()
    {
        var webhook = $"{_settings.Http.Urls.TrimEnd('/')}{AppSettings.WebhookPath}";
        var topic = _settings.Telegram.TopicId > 0 ? _settings.Telegram.TopicId.ToString() : "General";

        return
            "<b>GiteaBot - Status</b>" + Environment.NewLine +
            $"Group: <code>{_settings.Telegram.GroupId}</code>" + Environment.NewLine +
            $"Topic: <code>{topic}</code>" + Environment.NewLine +
            $"Gitea: <code>{_settings.Gitea.BaseUrl}</code>" + Environment.NewLine +
            $"Webhook: <code>{webhook}</code>" + Environment.NewLine +
            $"Pending requests: <code>{_store.Count}</code>";
    }
}
