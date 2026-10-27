using GiteaMirrorBot.Gitea;
using Xunit;

namespace GiteaMirrorBot.Tests;

/// <summary>
/// The sync responses of POST /api/sync/* return the mirror id both on 200 and on
/// 409 (docs/API.md, issue #472) – the bot relies on both.
/// </summary>
public class GiteaSyncResponseTests
{
    [Fact]
    public void Org_Creation_200_Returns_OrganizationId()
    {
        const string body = """
        {
          "success": true,
          "message": "Organization added successfully",
          "organization": { "id": "7c1a", "name": "my-org", "status": "imported" }
        }
        """;

        var response = GiteaApiClient.ParseSyncResponse(body)!;

        Assert.True(response.Success);
        Assert.Equal("7c1a", response.Organization!.Id);
        Assert.Null(response.Repository);
    }

    [Fact]
    public void Org_Creation_409_Returns_Stored_OrganizationId()
    {
        const string body = """
        {
          "success": false,
          "error": "Organization already tracked",
          "organization": { "id": "7c1a", "name": "my-org" }
        }
        """;

        var response = GiteaApiClient.ParseSyncResponse(body)!;

        Assert.False(response.Success);
        Assert.Equal("Organization already tracked", response.Error);
        Assert.Equal("7c1a", response.Organization!.Id);
    }

    [Fact]
    public void Repo_Creation_Returns_RepositoryId()
    {
        const string body = """
        {
          "success": true,
          "message": "Repository added successfully",
          "repository": { "id": "9b2f", "fullName": "octocat/hello-world", "status": "imported" }
        }
        """;

        var response = GiteaApiClient.ParseSyncResponse(body)!;

        Assert.Equal("9b2f", response.Repository!.Id);
        Assert.Null(response.Organization);
    }

    [Fact]
    public void Error_Html_Or_Error_Without_Records_Is_Still_Parsed()
    {
        // 404 from POST /api/sync/organization for a user account (issue #472).
        const string body = """
        { "success": false, "error": "github.com/MajMcCloud is a user account, not an organization." }
        """;

        var response = GiteaApiClient.ParseSyncResponse(body)!;

        Assert.False(response.Success);
        Assert.Contains("user account", response.Error);
        Assert.Null(response.Organization);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kein json")]
    [InlineData("<html>404</html>")]
    public void Unusable_Bodies_Yield_Null(string? body)
    {
        Assert.Null(GiteaApiClient.ParseSyncResponse(body));
    }
}
