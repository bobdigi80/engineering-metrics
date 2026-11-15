using EngineeringMetrics.Collector.GitHub;

namespace EngineeringMetrics.Tests;

public class GitHubPullRequestMapperTests
{
    private const string Response = """
        {
          "data": { "repository": { "pullRequests": { "nodes": [
            {
              "number": 1,
              "createdAt": "2026-11-01T09:00:00Z",
              "mergedAt": "2026-11-02T09:00:00Z",
              "additions": 40, "deletions": 2,
              "author": { "login": "alice", "__typename": "User" },
              "reviews": { "nodes": [
                { "submittedAt": "2026-11-01T09:30:00Z", "state": "COMMENTED", "author": { "login": "alice", "__typename": "User" } },
                { "submittedAt": "2026-11-01T09:45:00Z", "state": "COMMENTED", "author": { "login": "copilot-reviewer", "__typename": "Bot" } },
                { "submittedAt": null, "state": "PENDING", "author": { "login": "carol", "__typename": "User" } },
                { "submittedAt": "2026-11-01T12:00:00Z", "state": "APPROVED", "author": { "login": "bob", "__typename": "User" } },
                { "submittedAt": "2026-11-01T11:00:00Z", "state": "CHANGES_REQUESTED", "author": { "login": "dave", "__typename": "User" } }
              ] }
            },
            {
              "number": 2,
              "createdAt": "2026-11-03T09:00:00Z",
              "mergedAt": "2026-11-03T10:00:00Z",
              "additions": 1, "deletions": 1,
              "author": { "login": "dependabot", "__typename": "Bot" },
              "reviews": { "nodes": [] }
            },
            {
              "number": 3,
              "createdAt": "2026-11-04T09:00:00Z",
              "mergedAt": "2026-11-04T10:00:00Z",
              "additions": 5, "deletions": 0,
              "author": null,
              "reviews": { "nodes": [] }
            }
          ] } } }
        }
        """;

    [Fact]
    public void First_review_ignores_author_bots_and_pending_reviews()
    {
        var pr = GitHubPullRequestMapper.Map(Response).Single(p => p.Number == 1);

        Assert.Equal("alice", pr.Author);
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T11:00:00Z"), pr.FirstReviewAt);
        Assert.Equal(42, pr.Size);
        Assert.Equal(TimeSpan.FromHours(24), pr.CycleTime);
    }

    [Fact]
    public void Skips_bot_authored_pull_requests() =>
        Assert.DoesNotContain(GitHubPullRequestMapper.Map(Response), p => p.Number == 2);

    [Fact]
    public void Deleted_author_maps_to_ghost_with_no_review()
    {
        var pr = GitHubPullRequestMapper.Map(Response).Single(p => p.Number == 3);

        Assert.Equal("ghost", pr.Author);
        Assert.Null(pr.FirstReviewAt);
    }

    [Fact]
    public void GraphQL_errors_throw() =>
        Assert.Throws<GitHubApiException>(() =>
            GitHubPullRequestMapper.Map("""{ "data": null, "errors": [ { "message": "Bad credentials" } ] }"""));

    [Fact]
    public void Missing_repository_throws() =>
        Assert.Throws<GitHubApiException>(() =>
            GitHubPullRequestMapper.Map("""{ "data": { "repository": null } }"""));
}
