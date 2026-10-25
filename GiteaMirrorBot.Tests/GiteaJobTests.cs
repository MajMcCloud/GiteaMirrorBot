using System.Text.Json;
using GiteaBot.Analysis;
using GiteaBot.Gitea;
using Xunit;

namespace GiteaBot.Tests;

public class GiteaJobRequestTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void MirrorRepoPayload_Matches_The_Specification()
    {
        var json = JsonSerializer.Serialize(new MirrorRepositoryJobRequest(["9b2f"]), Options);

        Assert.Equal("""{"repositoryIds":["9b2f"]}""", json);
    }

    [Fact]
    public void MirrorOrgPayload_Matches_The_Specification()
    {
        var json = JsonSerializer.Serialize(new MirrorOrganizationJobRequest(["7c1a", "8d2b"]), Options);

        Assert.Equal("""{"organizationIds":["7c1a","8d2b"]}""", json);
    }

    [Fact]
    public void Empty_And_Whitespace_Ids_Are_Filtered_Out()
    {
        var repo = new MirrorRepositoryJobRequest(["a", "", "  ", null!]);
        var org = new MirrorOrganizationJobRequest([]);

        Assert.Equal(["a"], repo.RepositoryIds);
        Assert.Empty(org.OrganizationIds);
    }

    [Fact]
    public void Endpoint_Constants_Are_Correct()
    {
        Assert.Equal("/api/github/repositories", GiteaApiClient.RepositoriesEndpoint);
        Assert.Equal("/api/job/mirror-repo", GiteaApiClient.MirrorRepositoryJobEndpoint);
        Assert.Equal("/api/job/mirror-org", GiteaApiClient.MirrorOrganizationJobEndpoint);
    }
}

public class GitHubRepositoryEntryTests
{
    private const string Sample = """
    {
      "success": true,
      "message": "Repositories fetched successfully",
      "repositories": [
        {
          "id": "9b2f",
          "name": "hello-world",
          "fullName": "octocat/hello-world",
          "owner": "octocat",
          "organization": null,
          "status": "mirrored",
          "mirroredLocation": "github-mirrors/hello-world",
          "lastMirrored": "2026-09-02T08:00:00.000Z",
          "errorMessage": null,
          "destinationOrg": null,
          "sourceProvider": "github",
          "isPrivate": false
        }
      ]
    }
    """;

    [Fact]
    public void Sample_Response_Is_Read_Completely()
    {
        var payload = JsonSerializer.Deserialize<GitHubRepositoriesResponse>(
            Sample, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.True(payload.Success);
        Assert.Equal("Repositories fetched successfully", payload.Message);

        var repo = Assert.Single(payload.Repositories);
        Assert.Equal("9b2f", repo.Id);
        Assert.Equal("hello-world", repo.Name);
        Assert.Equal("octocat/hello-world", repo.FullName);
        Assert.Equal("octocat", repo.Owner);
        Assert.Null(repo.Organization);
        Assert.Equal("mirrored", repo.Status);
        Assert.Equal("github-mirrors/hello-world", repo.MirroredLocation);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero), repo.LastMirrored);
        Assert.Null(repo.ErrorMessage);
        Assert.Null(repo.DestinationOrg);
        Assert.Equal("github", repo.SourceProvider);
        Assert.False(repo.IsPrivate);
    }
}

public class GiteaCatalogPlanTests
{
    private static GitHubRepositoriesResponse Catalog(params GitHubRepositoryEntry[] entries)
        => new() { Success = true, Repositories = entries.ToList() };

    private static GitHubRepositoryEntry Repo(string id, string owner, string name, string? org = null)
        => new()
        {
            Id = id,
            Owner = owner,
            Name = name,
            FullName = $"{owner}/{name}",
            Organization = org
        };

    [Fact]
    public void Repo_Link_Finds_RepositoryId_Via_FullName()
    {
        var link = GitHubLinkAnalyzer.Parse("https://github.com/octocat/hello-world")!;

        var plan = GiteaCatalogService.PlanForRepository(link, Catalog(
            Repo("1111", "other", "hello-world"),
            Repo("9b2f", "octocat", "hello-world")));

        Assert.True(plan.Resolved);
        Assert.Equal(["9b2f"], plan.RepositoryIds);
        Assert.Empty(plan.OrganizationIds);
        Assert.True(plan.HasWork);
    }

    [Fact]
    public void Repo_Path_Stub_Still_Finds_The_Id()
    {
        var link = GitHubLinkAnalyzer.Parse("https://github.com/octocat/hello-world/issues/3")!;

        var plan = GiteaCatalogService.PlanForRepository(link, Catalog(Repo("9b2f", "octocat", "hello-world")));

        Assert.Equal(["9b2f"], plan.RepositoryIds);
    }

    [Fact]
    public void Letter_Case_Does_Not_Matter()
    {
        var link = GitHubLinkAnalyzer.Parse("https://github.com/Octocat/Hello-World")!;

        var plan = GiteaCatalogService.PlanForRepository(link, Catalog(Repo("9b2f", "octocat", "hello-world")));

        Assert.Equal(["9b2f"], plan.RepositoryIds);
    }

    [Fact]
    public void Missing_Repo_And_Missing_Id_Are_Distinguishable()
    {
        var link = GitHubLinkAnalyzer.Parse("https://github.com/octocat/fresh-repo")!;

        var missing = GiteaCatalogService.PlanForRepository(link, Catalog(Repo("9b2f", "octocat", "alt")));
        Assert.False(missing.Resolved);
        Assert.Contains("is not part of the Gitea catalog", missing.Reason);

        var noId = GiteaCatalogService.PlanForRepository(link, Catalog(Repo("", "octocat", "fresh-repo")));
        Assert.False(noId.Resolved);
        Assert.Contains("has no id in the catalog", noId.Reason);
    }

    [Fact]
    public void Org_Link_Collects_OrganizationIds_Without_Repos()
    {
        var link = GitHubLinkAnalyzer.Parse("https://github.com/my-org")!;

        var plan = GiteaCatalogService.PlanForOrganization(link, Catalog(
            Repo("r1", "my-org", "a", "org-1"),
            Repo("r2", "my-org", "b", "org-1"),
            Repo("r3", "my-org", "c", "org-2"),
            Repo("r4", "foreign", "d", "org-9")));

        Assert.True(plan.Resolved);
        Assert.Equal(["org-1", "org-2"], plan.OrganizationIds);
        Assert.Empty(plan.RepositoryIds);
    }

    [Fact]
    public void Org_Without_Repos_And_Org_Without_Id_Report_Reasons()
    {
        var link = GitHubLinkAnalyzer.Parse("https://github.com/my-org")!;

        var empty = GiteaCatalogService.PlanForOrganization(link, Catalog(Repo("r1", "other", "a", "org-1")));
        Assert.False(empty.Resolved);
        Assert.Contains("has no repositories in the Gitea catalog", empty.Reason);

        var noOrgId = GiteaCatalogService.PlanForOrganization(link, Catalog(Repo("r1", "my-org", "a")));
        Assert.False(noOrgId.Resolved);
        Assert.Contains("but no organization id set", noOrgId.Reason);
    }

    [Fact]
    public void Unresolved_Plan_Has_No_Ids()
    {
        var plan = MirrorPlan.Unresolved("catalog unreachable");

        Assert.False(plan.Resolved);
        Assert.False(plan.HasWork);
        Assert.Equal("catalog unreachable", plan.Reason);
    }
}
