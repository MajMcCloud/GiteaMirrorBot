using GiteaMirrorBot;
using GiteaMirrorBot.Configuration;
using GiteaMirrorBot.Gitea;
using GiteaMirrorBot.Services;
using GiteaMirrorBot.Telegram;
using GiteaMirrorBot.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GiteaMirrorBot;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Offline self-test for link detection: dotnet run -- --selftest
        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
            return SelfTest.Run();

        var builder = WebApplication.CreateBuilder(args);

        ConfigureConfiguration(builder);

        var appSettings = ServiceRegistration.ReadSettings(builder.Configuration);
        if (!TryValidateSettings(appSettings, out var failure))
        {
            Console.Error.WriteLine(failure);
            return 1;
        }

        ConfigureWebHost(builder, appSettings);
        ConfigureServices(builder, appSettings);

        var app = builder.Build();
        ConfigureApp(app);

        WriteStartupBanner(appSettings);

        await app.RunAsync();
        return 0;
    }

    // ---------------------------------------------------------------- Configuration (TA-02)
    private static void ConfigureConfiguration(WebApplicationBuilder builder)
    {
        builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        builder.Services.Configure<AppSettings>(builder.Configuration);
    }

    private static bool TryValidateSettings(AppSettings appSettings, out string failure)
    {
        try
        {
            appSettings.Validate();
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            return false;
        }
    }

    private static void ConfigureWebHost(WebApplicationBuilder builder, AppSettings appSettings)
    {
        var urls = GetUrls(appSettings).ToArray();
        builder.WebHost.UseUrls(urls);
    }

    // ---------------------------------------------------------------- Services
    private static void ConfigureServices(WebApplicationBuilder builder, AppSettings appSettings)
    {
        builder.Services.AddGiteaMirrorBotServices(appSettings);
        builder.Services.AddHostedService<BotHostedService>();
    }

    private static void ConfigureApp(WebApplication app)
    {
        // Forms are created by TelegramBotBase (not by DI) -> publish composition root once.
        app.Services.GetRequiredService<BotServices>().Publish();

        app.MapWebhook();
    }

    // ---------------------------------------------------------------- Startup banner (TA-11)
    private static IEnumerable<string> GetUrls(AppSettings appSettings)
        => appSettings.Http.Urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string GetBaseUrl(AppSettings appSettings)
        => GetUrls(appSettings).First().TrimEnd('/');

    private static void WriteStartupBanner(AppSettings appSettings)
    {
        var baseUrl = GetBaseUrl(appSettings);
        var webhook = $"{baseUrl}{AppSettings.WebhookPath}";
        var apiKeyState = string.IsNullOrWhiteSpace(appSettings.Gitea.ApiKey) ? "MISSING" : "set (header x-api-key)";

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════");
        Console.WriteLine("║ GiteaMirrorBot");
        Console.WriteLine($"║ Gitea webhook endpoint : {webhook}");
        Console.WriteLine($"║ Health check           : {baseUrl}/health");
        Console.WriteLine($"║ Gitea base URL         : {appSettings.Gitea.BaseUrl}");
        Console.WriteLine($"║ Gitea API key          : {apiKeyState}");
        Console.WriteLine($"║ Target group           : {appSettings.Telegram.GroupId}");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════");
        Console.WriteLine();
    }
}
