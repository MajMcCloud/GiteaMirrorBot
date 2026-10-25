using GiteaBot;
using GiteaBot.Configuration;
using GiteaBot.Gitea;
using GiteaBot.Services;
using GiteaBot.Telegram;
using GiteaBot.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Offline self-test for link detection: dotnet run -- --selftest
if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
    return SelfTest.Run();

// ---------------------------------------------------------------- Configuration (TA-02)
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);

builder.Services.Configure<AppSettings>(builder.Configuration);

var appSettings = ServiceRegistration.ReadSettings(builder.Configuration);

try
{
    appSettings.Validate();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var urls = appSettings.Http.Urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.WebHost.UseUrls(urls);

// ---------------------------------------------------------------- Services
builder.Services.AddGiteaBotServices(appSettings);
builder.Services.AddHostedService<BotHostedService>();

var app = builder.Build();

// Forms are created by TelegramBotBase (not by DI) -> publish composition root once.
app.Services.GetRequiredService<BotServices>().Publish();

app.MapWebhook();

// ---------------------------------------------------------------- Startup (TA-11)
var webhook = $"{appSettings.Http.Urls.Split(';')[0].TrimEnd('/')}{AppSettings.WebhookPath}";

Console.WriteLine();
Console.WriteLine($"╔══════════════════════════════════════════════════════════════");
Console.WriteLine($"║ GiteaBot");
Console.WriteLine($"║ Gitea webhook endpoint : {webhook}");
Console.WriteLine($"║ Health check           : {appSettings.Http.Urls.Split(';')[0].TrimEnd('/')}/health");
Console.WriteLine($"║ Gitea base URL         : {appSettings.Gitea.BaseUrl}");
Console.WriteLine($"║ Gitea API key          : {(string.IsNullOrWhiteSpace(appSettings.Gitea.ApiKey) ? "MISSING" : "set (header x-api-key)")}");
Console.WriteLine($"║ Target group           : {appSettings.Telegram.GroupId}");
Console.WriteLine($"╚══════════════════════════════════════════════════════════════");
Console.WriteLine();

await app.RunAsync();
return 0;
