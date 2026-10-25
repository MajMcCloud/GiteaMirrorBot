using GiteaBot.Analysis;
using GiteaBot.Telegram;
using Xunit;

namespace GiteaBot.Tests;

/// <summary>
/// Verifies the plain text generation of the "In progress" message. The Telegram part
/// (send/edit) is deliberately left out here.
/// </summary>
public class BackupProgressTests
{
    private static readonly GitHubLink Repo = new(LinkKind.Repository, "octocat", "hello-world", "https://github.com/octocat/hello-world");
    private static readonly GitHubLink Org = new(LinkKind.Organization, "my-org", null, "https://github.com/my-org");

    [Fact]
    public void BuildText_Start_Without_Completion_Looks_In_Progress()
    {
        var text = BackupProgress.BuildText(Repo, [], "sending the request to Gitea", TimeSpan.FromSeconds(3));

        Assert.Contains("⏳ In progress", text);
        Assert.Contains("Target:", text);
        Assert.Contains("octocat/hello-world", text);
        Assert.Contains("Type: Repository", text);
        Assert.Contains("▶ sending the request to Gitea", text);
        Assert.Contains("running for 3 s", text);
        Assert.DoesNotContain("✅", text);
    }

    [Fact]
    public void BuildText_Lists_Completed_Steps()
    {
        var text = BackupProgress.BuildText(
            Repo,
            ["created via /api/sync/repository", "found repository id 9b2f"],
            "triggering the mirror job",
            TimeSpan.FromSeconds(12));

        Assert.Contains("✅ created via /api/sync/repository", text);
        Assert.Contains("✅ found repository id 9b2f", text);
        Assert.Contains("▶ triggering the mirror job", text);
        Assert.Contains("running for 12 s", text);
    }

    [Fact]
    public void BuildText_Organization_Link_Uses_Only_Owner_As_Target()
    {
        var text = BackupProgress.BuildText(Org, [], "starting", TimeSpan.Zero);

        Assert.Contains(">my-org<", text);
        Assert.Contains("Type: Organization", text);
        Assert.DoesNotContain("my-org/", text);
    }

    [Fact]
    public void BuildText_Escapes_Html_Characters_From_Target_Name_And_Steps()
    {
        var tricky = new GitHubLink(LinkKind.Repository, "a<b", "c&d", "https://github.com/a<b/c&d");

        var text = BackupProgress.BuildText(tricky, ["<script>alert(1)</script>"], "running", TimeSpan.Zero);

        Assert.Contains("a&lt;b/c&amp;d", text);
        Assert.Contains("&lt;script&gt;", text);
        Assert.DoesNotContain("<script>", text);
    }

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(2.4, "2 s")]
    [InlineData(59.6, "59 s")]
    [InlineData(60, "1 min")]
    [InlineData(125, "2 min 5 s")]
    public void FormatElapsed_Reads_Short(double seconds, string expected)
        => Assert.Equal(expected, BackupProgress.FormatElapsed(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Edit_Interval_And_Heartbeat_Are_Sensibly_Staggered()
    {
        // The heartbeat must fire far less often than the Telegram edit limit,
        // otherwise it produces "message is not modified" errors.
        Assert.True(BackupProgress.HeartbeatInterval > BackupProgress.MinEditInterval);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("a<b>&c", "a&lt;b&gt;&amp;c")]
    public void Escape_Replaces_Only_The_Three_Html_Characters(string? input, string expected)
        => Assert.Equal(expected, Html.Escape(input));
}
