using GiteaBot.Configuration;
using GiteaBot.Gitea;
using GiteaBot.Services;
using GiteaBot.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GiteaBot;

/// <summary>
/// Read configuration and register services – extracted so it can be
/// validated by automated tests (see GiteaBot.Tests).
/// </summary>
public static class ServiceRegistration
{
    /// <summary>Environment variables override secrets from the file.</summary>
    public static AppSettings ReadSettings(IConfiguration configuration)
    {
        return new AppSettings
        {
            Telegram = new TelegramSettings
            {
                ApiKey = Environment.GetEnvironmentVariable("API_KEY")
                         ?? configuration[$"{nameof(AppSettings.Telegram)}:{nameof(TelegramSettings.ApiKey)}"]
                         ?? string.Empty,
                GroupId = ReadLong(configuration, $"{nameof(AppSettings.Telegram)}:{nameof(TelegramSettings.GroupId)}"),
                TopicId = (int)ReadLong(configuration, $"{nameof(AppSettings.Telegram)}:{nameof(TelegramSettings.TopicId)}")
            },
            Gitea = new GiteaSettings
            {
                BaseUrl = Environment.GetEnvironmentVariable("GITEA_BASE_URL")
                          ?? configuration[$"{nameof(AppSettings.Gitea)}:{nameof(GiteaSettings.BaseUrl)}"]
                          ?? string.Empty,
                // "ApiKey" is the recommended key name; "Token" is still accepted for backward compatibility.
                ApiKey = Environment.GetEnvironmentVariable("GITEA_API_KEY")
                         ?? Environment.GetEnvironmentVariable("GITEA_TOKEN")
                         ?? configuration[$"{nameof(AppSettings.Gitea)}:{nameof(GiteaSettings.ApiKey)}"]
                         ?? configuration[$"{nameof(AppSettings.Gitea)}:Token"]
                         ?? string.Empty
            },
            Http = new HttpSettings
            {
                Urls = configuration[$"{nameof(AppSettings.Http)}:{nameof(HttpSettings.Urls)}"] ?? "http://0.0.0.0:5080"
            }
        };
    }

    public static void AddGiteaBotServices(this IServiceCollection services, AppSettings settings)
    {
        services.AddSingleton(Options.Create(settings));
        services.AddSingleton(settings);

        services.AddSingleton<TopicSender>();
        services.AddSingleton<PendingRequestStore>();

        services.AddHttpClient("gitea", client => client.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<GiteaApiClient>();
        services.AddSingleton<GiteaCatalogService>();

        services.AddSingleton<BackupService>();
        services.AddSingleton<NotificationRouter>();

        services.AddSingleton(sp => new BotServices
        {
            Sender = sp.GetRequiredService<TopicSender>(),
            Backup = sp.GetRequiredService<BackupService>(),
            Store = sp.GetRequiredService<PendingRequestStore>(),
            Settings = settings,
            LoggerFactory = sp.GetRequiredService<ILoggerFactory>()
        });
    }

    private static long ReadLong(IConfiguration configuration, string key)
    {
        var raw = configuration[key];
        return long.TryParse(raw, out var value) ? value : 0;
    }
}
