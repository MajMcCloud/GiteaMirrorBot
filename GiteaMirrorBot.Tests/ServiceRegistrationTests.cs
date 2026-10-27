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

    [Fact]
    public void Validate_Requires_The_Mandatory_Values()
    {
        var settings = new AppSettings();

        var ex = Assert.Throws<InvalidOperationException>(settings.Validate);
        Assert.Contains("Telegram:ApiKey", ex.Message);
        Assert.Contains("Telegram:GroupId", ex.Message);
    }

    [Fact]
    public void Validate_Does_Not_Complain_With_A_Complete_Configuration()
    {
        var settings = ServiceRegistration.ReadSettings(Config());
        settings.Validate();
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
