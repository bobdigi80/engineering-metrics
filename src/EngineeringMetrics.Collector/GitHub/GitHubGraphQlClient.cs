using System.Net.Http.Headers;
using System.Net.Http.Json;
using EngineeringMetrics.Core;

namespace EngineeringMetrics.Collector.GitHub;

public interface IGitHubClient
{
    /// <summary>Every merged pull request that could have been merged at or after <paramref name="since"/>.</summary>
    /// <param name="repository">In <c>owner/name</c> form.</param>
    Task<IReadOnlyList<PullRequest>> GetMergedPullRequestsSinceAsync(
        string repository, DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>
/// Uses the GraphQL API so one request returns a page of pull requests with their sizes and reviews.
/// The REST API would need extra requests per pull request. See docs/adr/0003.
/// </summary>
public sealed class GitHubGraphQlClient(HttpClient http) : IGitHubClient
{
    public const int PageSize = 50;

    /// <summary>A safety limit. Hitting it fails the repository rather than publishing a partial count.</summary>
    public const int MaxPages = 60;

    private const string Query = """
        query($owner: String!, $name: String!, $count: Int!, $after: String) {
          repository(owner: $owner, name: $name) {
            pullRequests(states: MERGED, first: $count, after: $after, orderBy: { field: UPDATED_AT, direction: DESC }) {
              pageInfo { hasNextPage endCursor }
              nodes {
                number
                createdAt
                updatedAt
                mergedAt
                additions
                deletions
                author { login __typename }
                reviews(first: 20) {
                  nodes { submittedAt state author { login __typename } }
                }
              }
            }
          }
        }
        """;

    public static GitHubGraphQlClient Create(string token)
    {
        var http = new HttpClient { BaseAddress = new Uri("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("engineering-metrics", "1.0"));
        return new GitHubGraphQlClient(http);
    }

    /// <summary>
    /// Pages through merged pull requests, most recently updated first. A pull request is always updated
    /// at or after the time it was merged, so once a page reaches one last updated before
    /// <paramref name="since"/>, no later page can contain a pull request merged in the window.
    /// </summary>
    public async Task<IReadOnlyList<PullRequest>> GetMergedPullRequestsSinceAsync(
        string repository, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var parts = repository.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"Expected 'owner/name' but got '{repository}'.", nameof(repository));
        }

        var pullRequests = new List<PullRequest>();
        string? after = null;

        for (var page = 0; page < MaxPages; page++)
        {
            using var response = await http.PostAsJsonAsync(
                "graphql",
                new { query = Query, variables = new { owner = parts[0], name = parts[1], count = PageSize, after } },
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new GitHubApiException($"GitHub returned {(int)response.StatusCode} for {repository}.");
            }

            var result = GitHubPullRequestMapper.MapPage(body);
            pullRequests.AddRange(result.PullRequests);

            var reachedStartOfWindow = result.OldestUpdatedAt is { } oldest && oldest < since;
            if (reachedStartOfWindow || !result.HasNextPage || result.EndCursor is null)
            {
                return pullRequests;
            }

            after = result.EndCursor;
        }

        throw new GitHubApiException(
            $"{repository} has more than {MaxPages * PageSize} merged pull requests updated since {since:yyyy-MM-dd}.");
    }
}
