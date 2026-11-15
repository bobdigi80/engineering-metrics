using EngineeringMetrics.Collector;
using EngineeringMetrics.Collector.GitHub;
using EngineeringMetrics.Core;

namespace EngineeringMetrics.Tests;

public class CollectorServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Saves_a_snapshot_per_repository()
    {
        var store = new InMemoryStore();
        var gitHub = new FakeGitHub(new()
        {
            ["a/one"] = [new PullRequest(1, "dev", Now.AddDays(-2), Now.AddDays(-1), Now.AddDays(-2).AddHours(3), 10, 5)],
            ["b/two"] = [],
        });

        var result = await Service(gitHub, store, ["a/one", "b/two"]).RunAsync(CancellationToken.None);

        Assert.Empty(result.FailedRepositories);
        Assert.Equal(["a/one", "b/two"], store.Saved.Select(s => s.Repository));
        Assert.Equal(1, store.Saved[0].MergedPullRequests);
        Assert.Equal(3, store.Saved[0].MedianHoursToFirstReview);
        Assert.All(store.Saved, s => Assert.Equal(Now, s.GeneratedAt));
    }

    [Fact]
    public async Task One_failing_repository_does_not_stop_the_others()
    {
        var store = new InMemoryStore();
        var gitHub = new FakeGitHub(new() { ["ok/repo"] = [] });
        var log = new List<string>();

        var result = await Service(gitHub, store, ["broken/repo", "ok/repo"], log.Add).RunAsync(CancellationToken.None);

        Assert.Equal(["broken/repo"], result.FailedRepositories);
        Assert.Equal(["ok/repo"], store.Saved.Select(s => s.Repository));
        Assert.Contains(log, line => line.StartsWith("broken/repo: failed"));
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(new FakeGitHub([]), new InMemoryStore(), ["a/b"]).RunAsync(cts.Token));
    }

    private static CollectorService Service(IGitHubClient gitHub, IMetricsStore store, string[] repos, Action<string>? log = null) =>
        new(gitHub, store, new FixedTime(Now), new CollectorOptions(repos, TimeSpan.FromDays(30)), log);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeGitHub(Dictionary<string, PullRequest[]> data) : IGitHubClient
    {
        public Task<IReadOnlyList<PullRequest>> GetRecentMergedPullRequestsAsync(string repository, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return data.TryGetValue(repository, out var prs)
                ? Task.FromResult<IReadOnlyList<PullRequest>>(prs)
                : throw new GitHubApiException("Repository not found or not accessible.");
        }
    }

    private sealed class InMemoryStore : IMetricsStore
    {
        public List<MetricsSnapshot> Saved { get; } = [];

        public Task SaveAsync(MetricsSnapshot snapshot, CancellationToken cancellationToken)
        {
            Saved.Add(snapshot);
            return Task.CompletedTask;
        }
    }
}
