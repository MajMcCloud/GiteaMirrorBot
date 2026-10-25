using GiteaBot.Configuration;
using GiteaBot.Gitea;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GiteaBot.Tests;

public class GiteaApiClientTests
{
    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static GiteaApiClient Create(GiteaSettings settings, out HttpClient client)
    {
        client = new HttpClient();
        return new GiteaApiClient(
            new SingleClientFactory(client),
            Options.Create(new AppSettings { Gitea = settings }),
            NullLogger<GiteaApiClient>.Instance);
    }

    [Fact]
    public void ApiKey_Is_Set_As_The_x_api_key_Header()
    {
        Create(new GiteaSettings { BaseUrl = "http://10.10.5.5:4321", ApiKey = " gm_secret " }, out var client);

        Assert.True(client.DefaultRequestHeaders.TryGetValues("x-api-key", out var values));
        Assert.Equal("gm_secret", Assert.Single(values));
        Assert.Equal("http://10.10.5.5:4321/", client.BaseAddress!.ToString());
    }

    [Fact]
    public void There_Is_No_Authorization_Header_Any_More()
    {
        Create(new GiteaSettings { BaseUrl = "http://localhost", ApiKey = "secret" }, out var client);

        Assert.False(client.DefaultRequestHeaders.Contains("Authorization"));
    }

    [Fact]
    public void No_Header_Is_Set_Without_An_ApiKey()
    {
        Create(new GiteaSettings { BaseUrl = "http://localhost" }, out var client);

        Assert.False(client.DefaultRequestHeaders.Contains("x-api-key"));
    }

    [Fact]
    public void HeaderName_Constant_Is_Correct()
    {
        Assert.Equal("x-api-key", GiteaApiClient.ApiKeyHeader);
    }
}
