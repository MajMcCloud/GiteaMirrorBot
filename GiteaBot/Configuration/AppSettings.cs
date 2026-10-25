namespace GiteaBot.Configuration;

/// <summary>The collected, validated runtime configuration (appsettings.json + environment variables).</summary>
public sealed class AppSettings
{
    public TelegramSettings Telegram { get; init; } = new();
    public GiteaSettings Gitea { get; init; } = new();
    public HttpSettings Http { get; init; } = new();

    /// <summary>The path Gitea can post its notifications to (used for the console banner).</summary>
    public const string WebhookPath = "/api/gitea/webhook";

    /// <summary>Throws a descriptive exception when required values are missing.</summary>
    public void Validate()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(Telegram.ApiKey))
            missing.Add("Telegram:ApiKey (the bot token from @BotFather)");

        if (Telegram.GroupId == 0)
            missing.Add("Telegram:GroupId (chat id of the target group, e.g. -1001234567890)");

        if (string.IsNullOrWhiteSpace(Gitea.BaseUrl))
            missing.Add("Gitea:BaseUrl (e.g. https://gitea.example.com)");

        if (string.IsNullOrWhiteSpace(Gitea.ApiKey))
            missing.Add("Gitea:ApiKey (API key of the Gitea instance, header \"x-api-key\")");

        if (string.IsNullOrWhiteSpace(Http.Urls))
            missing.Add("Http:Urls (e.g. http://0.0.0.0:5080)");

        if (missing.Count > 0)
            throw new InvalidOperationException(
                "Incomplete configuration. Missing:" +
                Environment.NewLine + "  - " + string.Join(Environment.NewLine + "  - ", missing) +
                Environment.NewLine + "Template: appsettings.example.json");
    }
}

public sealed class TelegramSettings
{
    /// <summary>Bot token. Must not live in the source.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Chat id of the group (negative). Target for notifications without an active session – and
    /// the <b>only</b> chat in which the bot reacts to messages, buttons and /status
    /// (see Telegram/ChatGuard.cs).
    /// </summary>
    public long GroupId { get; init; }

    /// <summary>
    /// Forum topic (message_thread_id) that Gitea notifications are pushed into.
    /// 0 = General topic.
    /// </summary>
    public int TopicId { get; init; }
}

public sealed class GiteaSettings
{
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// API key of the Gitea instance. Sent as the <c>x-api-key</c> header.
    /// Must not live in the source (appsettings.local.json or the GITEA_API_KEY environment variable).
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;
}

public sealed class HttpSettings
{
    /// <summary>Kestrel bind addresses, e.g. "http://0.0.0.0:5080".</summary>
    public string Urls { get; init; } = "http://0.0.0.0:5080";
}
