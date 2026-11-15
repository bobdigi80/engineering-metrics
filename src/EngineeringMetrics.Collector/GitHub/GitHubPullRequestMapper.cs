using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringMetrics.Core;

namespace EngineeringMetrics.Collector.GitHub;

/// <summary>Maps a GitHub GraphQL response to <see cref="PullRequest"/> records.</summary>
public static class GitHubPullRequestMapper
{
    /// <summary>
    /// Bot-authored pull requests (e.g. Dependabot) are skipped because they would skew review times.
    /// Reviews by the PR author, by bots, and pending (unsubmitted) reviews do not count as a first review.
    /// </summary>
    /// <exception cref="GitHubApiException">The response contains GraphQL errors or no repository.</exception>
    public static IReadOnlyList<PullRequest> Map(string json)
    {
        var response = JsonSerializer.Deserialize<GraphQlResponse>(json, JsonSerializerOptions.Web)
            ?? throw new GitHubApiException("Empty response from GitHub.");

        if (response.Errors is { Count: > 0 } errors)
        {
            throw new GitHubApiException(string.Join("; ", errors.Select(e => e.Message)));
        }

        var nodes = response.Data?.Repository?.PullRequests.Nodes
            ?? throw new GitHubApiException("Repository not found or not accessible.");

        return nodes
            .Where(pr => pr.MergedAt is not null && !IsBot(pr.Author))
            .Select(pr =>
            {
                var author = pr.Author?.Login ?? "ghost";
                var firstReview = pr.Reviews.Nodes
                    .Where(r => r.SubmittedAt is not null
                        && r.State != "PENDING"
                        && !IsBot(r.Author)
                        && !string.Equals(r.Author?.Login, author, StringComparison.OrdinalIgnoreCase))
                    .Min(r => r.SubmittedAt);

                return new PullRequest(pr.Number, author, pr.CreatedAt, pr.MergedAt!.Value, firstReview, pr.Additions, pr.Deletions);
            })
            .ToList();
    }

    private static bool IsBot(Actor? actor) => actor?.Type == "Bot";

    private sealed record GraphQlResponse(GraphQlData? Data, List<GraphQlError>? Errors);
    private sealed record GraphQlError(string Message);
    private sealed record GraphQlData(RepositoryNode? Repository);
    private sealed record RepositoryNode(Connection<PullRequestNode> PullRequests);
    private sealed record Connection<T>(List<T> Nodes);

    private sealed record PullRequestNode(
        int Number,
        DateTimeOffset CreatedAt,
        DateTimeOffset? MergedAt,
        int Additions,
        int Deletions,
        Actor? Author,
        Connection<ReviewNode> Reviews);

    private sealed record ReviewNode(DateTimeOffset? SubmittedAt, string State, Actor? Author);

    private sealed record Actor(string Login, [property: JsonPropertyName("__typename")] string? Type);
}

public sealed class GitHubApiException(string message) : Exception(message);
