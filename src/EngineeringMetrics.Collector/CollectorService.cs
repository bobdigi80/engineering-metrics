using EngineeringMetrics.Collector.GitHub;
using EngineeringMetrics.Core;

namespace EngineeringMetrics.Collector;

public sealed record CollectorOptions(IReadOnlyList<string> Repositories, TimeSpan Window);

public sealed record CollectorResult(IReadOnlyList<MetricsSnapshot> Snapshots, IReadOnlyList<string> FailedRepositories);

/// <summary>Fetches pull requests for each repository, calculates metrics and stores them.</summary>
public sealed class CollectorService(
    IGitHubClient gitHub,
    IMetricsStore store,
    TimeProvider time,
    CollectorOptions options,
    Action<string>? log = null)
{
    /// <summary>
    /// A failing repository is logged and skipped so it cannot block the others.
    /// The caller decides whether partial failure should fail the run.
    /// </summary>
    public async Task<CollectorResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var snapshots = new List<MetricsSnapshot>();
        var failed = new List<string>();

        foreach (var repository in options.Repositories)
        {
            try
            {
                var pullRequests = await gitHub.GetMergedPullRequestsSinceAsync(
                    repository, now - options.Window, cancellationToken);
                var snapshot = MetricsCalculator.Calculate(repository, pullRequests, now, options.Window);
                await store.SaveAsync(snapshot, cancellationToken);
                snapshots.Add(snapshot);
                log?.Invoke($"{repository}: {snapshot.MergedPullRequests} merged PRs in window");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed.Add(repository);
                log?.Invoke($"{repository}: failed: {ex.Message}");
            }
        }

        return new CollectorResult(snapshots, failed);
    }
}
