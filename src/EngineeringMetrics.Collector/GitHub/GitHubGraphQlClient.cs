using System.Net.Http.Headers;
using System.Net.Http.Json;
using EngineeringMetrics.Core;

namespace EngineeringMetrics.Collector.GitHub;

public interface IGitHubClient
{
    /// <param name="repository">In <c>owner/name</c> form.</param>
    Task<IReadOnlyList<PullRequest>> GetRecentMergedPullRequestsAsync(string repository, int count, CancellationToken cancellationToken);
}

/// <summary>
/// Uses the GraphQL API so one request returns pull requests with their sizes and reviews.
/// The REST API would need one extra request per pull request. See docs/adr/0003.
/// </summary>
public sealed class GitHubGraphQlClient(HttpClient http) : IGitHubClient
{
    private const string Query = """
        query($owner: String!, $name: String!, $count: Int!) {
          repository(owner: $owner, name: $name) {
            pullRequests(states: MERGED, first: $count, orderBy: { field: UPDATED_AT, direction: DESC }) {
              nodes {
                number
                createdAt
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

    public async Task<IReadOnlyList<PullRequest>> GetRecentMergedPullRequestsAsync(
        string repository, int count, CancellationToken cancellationToken)
    {
        var parts = repository.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"Expected 'owner/name' but got '{repository}'.", nameof(repository));
        }

        using var response = await http.PostAsJsonAsync(
            "graphql",
            new { query = Query, variables = new { owner = parts[0], name = parts[1], count } },
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubApiException($"GitHub returned {(int)response.StatusCode} for {repository}.");
        }

        return GitHubPullRequestMapper.Map(body);
    }
}
