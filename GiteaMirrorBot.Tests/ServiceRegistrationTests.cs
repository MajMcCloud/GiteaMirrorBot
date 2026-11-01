using GiteaMirrorBot;
using GiteaMirrorBot.Configuration;
using GiteaMirrorBot.Gitea;
using GiteaMirrorBot.Services;
using GiteaMirrorBot.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GiteaMirrorBot.Tests;

public class ServiceRegistrationTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Telegram:ApiKey"] = "123:TEST",
            ["Telegram:GroupId"] = "-1001234567890",
            ["Telegram:TopicId"] = "42",
            ["Gitea:BaseUrl"] = "https://gitea.example.com",
            ["Gitea:ApiKey"] = "test-api-key",
            ["Http:Urls"] = "http://0.0.0.0:5080"
        })
        .Build();

    [Fact]
    public void ReadSettings_Reads_Values_From_The_Configuration()
    {
        var settings = ServiceRegistration.ReadSettings(Config());

        Assert.Equal("-1001234567890", settings.Telegram.GroupId.ToString());
        Assert.Equal(42, settings.Telegram.TopicId);
        Assert.Equal("https://gitea.example.com", settings.Gitea.BaseUrl);
        Assert.Equal("http://0.0.0.0:5080", settings.Http.Urls);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public void TryValidate_Reports_All_Missing_Values_Without_Throwing(string empty)
    {
        var settings = new AppSettings
        {
            Telegram = new TelegramSettings { ApiKey = empty },
            Gitea = new GiteaSettings { BaseUrl = empty, ApiKey = empty },
            Http = new HttpSettings { Urls = empty }
        };

        Assert.False(settings.TryValidate(out var failure));
        Assert.Equal(string.Join(Environment.NewLine,
            "Incomplete configuration. Missing:",
            "  - Telegram:ApiKey (the bot token from @BotFather; environment variable: API_KEY)",
            "  - Telegram:GroupId (chat id of the target group, e.g. -1001234567890; environment variable: Telegram__GroupId)",
            "  - Gitea:BaseUrl (e.g. https://gitea.example.com; environment variable: GITEA_BASE_URL)",
            "  - Gitea:ApiKey (API key of the Gitea instance, header \"x-api-key\"; environment variable: GITEA_API_KEY)",
            "  - Http:Urls (e.g. http://0.0.0.0:5080; environment variable: Http__Urls)",
            "Template: appsettings.example.json"), failure);
    }

    [Theory]
    [InlineData("Telegram:ApiKey")]
    [InlineData("Telegram:GroupId")]
    [InlineData("Gitea:BaseUrl")]
    [InlineData("Gitea:ApiKey")]
    [InlineData("Http:Urls")]
    public void TryValidate_Reports_Only_The_Missing_Value(string missingKey)
    {
        var settings = new AppSettings
        {
            Telegram = new TelegramSettings
            {
                ApiKey = missingKey == "Telegram:ApiKey" ? "" : "123:TEST",
                GroupId = missingKey == "Telegram:GroupId" ? 0 : -1001234567890
            },
            Gitea = new GiteaSettings
            {
                BaseUrl = missingKey == "Gitea:BaseUrl" ? "" : "https://gitea.example.com",
                ApiKey = missingKey == "Gitea:ApiKey" ? "" : "test-api-key"
            },
            Http = new HttpSettings
            {
                Urls = missingKey == "Http:Urls" ? "" : "http://0.0.0.0:5080"
            }
        };

        Assert.False(settings.TryValidate(out var failure));
        var lines = failure.Split(Environment.NewLine);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith($"  - {missingKey} (", lines[1]);
        Assert.DoesNotContain("123:TEST", failure);
        Assert.DoesNotContain("test-api-key", failure);
    }

    [Fact]
    public void TryValidate_Does_Not_Complain_With_A_Complete_Configuration()
    {
        var settings = ServiceRegistration.ReadSettings(Config());

        Assert.True(settings.TryValidate(out var failure));
        Assert.Equal(string.Empty, failure);
    }

    [Fact]
    public void TryValidate_Accepts_Default_Http_Urls_And_General_Topic()
    {
        var settings = new AppSettings
        {
            Telegram = new TelegramSettings { ApiKey = "123:TEST", GroupId = -1001234567890 },
            Gitea = new GiteaSettings { BaseUrl = "https://gitea.example.com", ApiKey = "test-api-key" }
        };

        Assert.True(settings.TryValidate(out var failure));
        Assert.Equal(string.Empty, failure);
    }

    [Fact]
    public void All_Services_Can_Be_Resolved()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var settings = ServiceRegistration.ReadSettings(Config());
        services.AddGiteaMirrorBotServices(settings);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<TopicSender>());
        Assert.NotNull(provider.GetRequiredService<PendingRequestStore>());
        Assert.NotNull(provider.GetRequiredService<GiteaApiClient>());
        Assert.NotNull(provider.GetRequiredService<GiteaCatalogService>());
        Assert.NotNull(provider.GetRequiredService<BackupService>());
        Assert.NotNull(provider.GetRequiredService<NotificationRouter>());
        Assert.NotNull(provider.GetRequiredService<BotServices>());
        Assert.Same(settings, provider.GetRequiredService<BotServices>().Settings);
    }

    [Fact]
    public void GiteaApiClient_Applies_BaseUrl_From_Configuration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGiteaMirrorBotServices(ServiceRegistration.ReadSettings(Config()));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<GiteaApiClient>();

        Assert.NotNull(client);
    }
}

public class PendingRequestStoreTests
{
    [Fact]
    public void Callback_Prefix_Fits_In_64_Bytes()
    {
        // Prefix + 12-character id must stay well below 64 bytes.
        var id = PendingRequestStore.NewId();
        var confirm = Forms.GroupWatchForm.ConfirmPrefix + id;
        var cancel = Forms.GroupWatchForm.CancelPrefix + id;

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(confirm) <= 64);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(cancel) <= 64);
    }

    [Fact]
    public void IsAlreadyAsked_Detects_The_Open_Question_Per_Link()
    {
        var store = new PendingRequestStore();
        var link = Analysis.GitHubLinkAnalyzer.Parse("https://github.com/a/b")!;

        store.Add(new PendingRequest
        {
            Id = PendingRequestStore.NewId(),
            Link = link,
            ChatId = -100,
            QuestionMessageId = 5
        });

        Assert.True(store.IsAlreadyAsked(-100, link.Key));
        Assert.False(store.IsAlreadyAsked(-200, link.Key));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Remove_Deletes_The_Request()
    {
        var store = new PendingRequestStore();
        var link = Analysis.GitHubLinkAnalyzer.Parse("https://github.com/a/b")!;
        var request = store.Add(new PendingRequest
        {
            Id = PendingRequestStore.NewId(),
            Link = link,
            ChatId = -100
        });

        Assert.NotNull(store.Remove(request.Id));
        Assert.Null(store.Find(request.Id));
        Assert.Equal(0, store.Count);
    }
}
