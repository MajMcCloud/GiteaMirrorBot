using GiteaMirrorBot.Analysis;
using GiteaMirrorBot.Web.Models;
using Xunit;

namespace GiteaMirrorBot.Tests;

public class GitHubLinkAnalyzerTests
{
    [Theory]
    [InlineData("https://github.com/majmccloud/TelegramBotFramework", LinkKind.Repository, "majmccloud", "TelegramBotFramework")]
    [InlineData("github.com/majmccloud/TelegramBotFramework", LinkKind.Repository, "majmccloud", "TelegramBotFramework")]
    [InlineData("https://www.github.com/owner/repo", LinkKind.Repository, "owner", "repo")]
    [InlineData("https://github.com/owner/repo/issues/3", LinkKind.Repository, "owner", "repo")]
    [InlineData("https://github.com/owner/repo/tree/main/src/App.cs", LinkKind.Repository, "owner", "repo")]
    [InlineData("https://github.com/owner/repo/pull/1/files", LinkKind.Repository, "owner", "repo")]
    [InlineData("https://github.com/owner/repo/blob/abc123/README.md", LinkKind.Repository, "owner", "repo")]
    [InlineData("https://github.com/owner/repo.git", LinkKind.Repository, "owner", "repo")]
    [InlineData("https://github.com/owner/repo?x=1#frag", LinkKind.Repository, "owner", "repo")]
    [InlineData("https://github.com/majmccloud", LinkKind.Organization, "majmccloud", null)]
    [InlineData("https://github.com/owner/", LinkKind.Organization, "owner", null)]
    public void Parse_Recognizes_Repo_And_Org(string url, LinkKind kind, string owner, string? repo)
    {
        var link = GitHubLinkAnalyzer.Parse(url);

        Assert.NotNull(link);
        Assert.Equal(kind, link!.Kind);
        Assert.Equal(owner, link.Owner);

        if (repo is not null)
            Assert.Equal(repo, link.Repo);
        else
            Assert.Null(link.Repo);
    }

    [Theory]
    [InlineData("https://github.com/trending")]
    [InlineData("https://github.com/orgs")]
    [InlineData("https://github.com/settings")]
    [InlineData("https://gist.github.com/owner/abc")]
    [InlineData("https://gitlab.com/owner/repo")]
    [InlineData("https://gitea.example.com/owner/repo")]
    [InlineData("https://github.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_Ignores_Non_Link_Candidates(string? url)
    {
        Assert.Null(GitHubLinkAnalyzer.Parse(url));
    }

    [Fact]
    public void ExtractFromText_Returns_All_Links_Without_Duplicates()
    {
        var text = "See https://github.com/a/b and https://github.com/c plus again https://github.com/a/b/issues/1.";

        var links = GitHubLinkAnalyzer.ExtractFromText(text);

        Assert.Equal(2, links.Count);
        Assert.Equal(LinkKind.Repository, links[0].Kind);
        Assert.Equal("a/b", links[0].DisplayName);
        Assert.Equal(LinkKind.Organization, links[1].Kind);
        Assert.Equal("c", links[1].DisplayName);
    }

    [Fact]
    public void CanonicalUrl_Builds_The_Canonical_Backup_Url()
    {
        var repo = GitHubLinkAnalyzer.Parse("https://github.com/a/b/issues/9");
        var org = GitHubLinkAnalyzer.Parse("https://github.com/acme");

        Assert.Equal("https://github.com/a/b", repo!.CanonicalUrl);
        Assert.Equal("https://github.com/acme", org!.CanonicalUrl);
    }

    [Fact]
    public void Key_Treats_Case_And_Path_Variants_Equal()
    {
        var a = GitHubLinkAnalyzer.Parse("https://github.com/Owner/Repo");
        var b = GitHubLinkAnalyzer.Parse("https://github.com/owner/repo/tree/main/x.cs");

        Assert.Equal(a!.Key, b!.Key);
    }
}

public class NotificationRouterFormatTests
{
    [Fact]
    public void Format_Contains_Title_Message_Type_And_Time()
    {
        var n = new Root
        {
            Title = "Sync completed",
            Message = "Repo a/b has been backed up.",
            Type = "repository",
            Timestamp = new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        var text = Services.NotificationRouter.Format(n);

        Assert.Contains("<b>Sync completed</b>", text);
        Assert.Contains("Repo a/b has been backed up.", text);
        Assert.Contains("repository", text);
        Assert.Contains("2024", text);
    }

    [Fact]
    public void Format_Strips_Html()
    {
        var n = new Root { Title = "<script>x</script>", Message = "a < b" };

        var text = Services.NotificationRouter.Format(n);

        Assert.DoesNotContain("<script>", text);
        Assert.Contains("&lt;script&gt;", text);
        Assert.Contains("a &lt; b", text);
    }
}
