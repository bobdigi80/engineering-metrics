using System.Net;
using System.Text;
using System.Text.Json;
using EngineeringMetrics.Collector.GitHub;

namespace EngineeringMetrics.Tests;

public class GitHubGraphQlClientTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Follows_pages_until_one_reaches_the_start_of_the_window()
    {
        var handler = new StubHandler(
            Page(hasNextPage: true, endCursor: "c1", (1, Since.AddDays(20)), (2, Since.AddDays(10))),
            Page(hasNextPage: true, endCursor: "c2", (3, Since.AddDays(5)), (4, Since.AddDays(-1))),
            Page(hasNextPage: false, endCursor: null, (5, Since.AddDays(-9))));

        var pullRequests = await Client(handler).GetMergedPullRequestsSinceAsync("a/b", Since, CancellationToken.None);

        Assert.Equal([1, 2, 3, 4], pullRequests.Select(p => p.Number));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(JsonValueKind.Null, After(handler.Requests[0]).ValueKind);
        Assert.Equal("c1", After(handler.Requests[1]).GetString());
    }

    [Fact]
    public async Task Stops_when_there_are_no_more_pages()
    {
        var handler = new StubHandler(
            Page(hasNextPage: true, endCursor: "c1", (1, Since.AddDays(20))),
            Page(hasNextPage: false, endCursor: null, (2, Since.AddDays(10))));

        var pullRequests = await Client(handler).GetMergedPullRequestsSinceAsync("a/b", Since, CancellationToken.None);

        Assert.Equal([1, 2], pullRequests.Select(p => p.Number));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Fails_rather_than_returning_a_partial_result_when_the_page_limit_is_hit()
    {
        var handler = new StubHandler(
            Enumerable.Repeat(Page(hasNextPage: true, endCursor: "more", (1, Since.AddDays(1))), GitHubGraphQlClient.MaxPages).ToArray());

        await Assert.ThrowsAsync<GitHubApiException>(() =>
            Client(handler).GetMergedPullRequestsSinceAsync("a/b", Since, CancellationToken.None));
    }

    [Fact]
    public async Task Http_error_throws()
    {
        var handler = new StubHandler { Status = HttpStatusCode.Unauthorized };

        await Assert.ThrowsAsync<GitHubApiException>(() =>
            Client(handler).GetMergedPullRequestsSinceAsync("a/b", Since, CancellationToken.None));
    }

    private static GitHubGraphQlClient Client(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test/") });

    private static JsonElement After(string requestBody) =>
        JsonDocument.Parse(requestBody).RootElement.GetProperty("variables").GetProperty("after");

    private static string Page(bool hasNextPage, string? endCursor, params (int Number, DateTimeOffset UpdatedAt)[] pullRequests) =>
        JsonSerializer.Serialize(new
        {
            data = new
            {
                repository = new
                {
                    pullRequests = new
                    {
                        pageInfo = new { hasNextPage, endCursor },
                        nodes = pullRequests.Select(pr => new
                        {
                            number = pr.Number,
                            createdAt = pr.UpdatedAt.AddDays(-1),
                            updatedAt = pr.UpdatedAt,
                            mergedAt = pr.UpdatedAt,
                            additions = 1,
                            deletions = 1,
                            author = new { login = "dev", __typename = "User" },
                            reviews = new { nodes = Array.Empty<object>() },
                        }),
                    },
                },
            },
        });

    private sealed class StubHandler(params string[] responses) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = Requests.Count <= responses.Length ? responses[Requests.Count - 1] : "{}";
            return new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
