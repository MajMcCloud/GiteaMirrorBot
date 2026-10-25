using System.Text.Json;
using GiteaBot.Analysis;
using GiteaBot.Gitea;
using Xunit;

namespace GiteaBot.Tests;

public class GiteaSyncRequestTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void RepositoryPayload_Matches_The_Specification()
    {
        var link = GitHubLinkAnalyzer.Parse("https://github.com/octocat/hello-world")!;

        var json = JsonSerializer.Serialize(RepositorySyncRequest.From(link), Options);

        Assert.Equal("""{"owner":"octocat","repo":"hello-world"}""", json);
    }

    [Fact]
    public void RepositoryPayload_Ignores_Sections_Of_Repo_Paths()
    {
        var link = GitHubLinkAnalyzer.Parse("https://github.com/octocat/hello-world/issues/7")!;

        var payload = RepositorySyncRequest.From(link);

        Assert.Equal("octocat", payload.Owner);
        Assert.Equal("hello-world", payload.Repo);
    }

    [Fact]
    public void OrganizationPayload_Matches_The_Specification_Without_Role()
    {
        var link = GitHubLinkAnalyzer.Parse("https://github.com/my-org")!;

        var json = JsonSerializer.Serialize(OrganizationSyncRequest.From(link), Options);

        // role has been optional since Gitea Mirror v3.45.0 and is no longer sent.
        Assert.Equal("""{"org":"my-org"}""", json);
    }

    [Fact]
    public void OrganizationPayload_Uses_Owner_As_Org_Name()
    {
        var link = GitHubLinkAnalyzer.Parse("https://github.com/MajMcCloud")!;

        var payload = OrganizationSyncRequest.From(link);

        Assert.Equal("MajMcCloud", payload.Org);
    }
}
