using EngineeringMetrics.Core;

namespace EngineeringMetrics.Tests;

public class MetricsCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromDays(30);

    private static PullRequest Pr(int number, double cycleHours, double? reviewHours, int size = 10, double mergedDaysAgo = 1)
    {
        var merged = Now.AddDays(-mergedDaysAgo);
        var created = merged.AddHours(-cycleHours);
        return new PullRequest(number, "dev", created, merged, reviewHours is { } h ? created.AddHours(h) : null, size, 0);
    }

    [Fact]
    public void No_pull_requests_gives_empty_snapshot()
    {
        var snapshot = MetricsCalculator.Calculate("o/r", [], Now, Window);

        Assert.Equal(0, snapshot.MergedPullRequests);
        Assert.Equal(30, snapshot.WindowDays);
        Assert.Null(snapshot.MedianCycleTimeHours);
        Assert.Null(snapshot.MedianHoursToFirstReview);
        Assert.Null(snapshot.ReviewedShare);
    }

    [Fact]
    public void Calculates_medians_and_p90()
    {
        var prs = new[]
        {
            Pr(1, cycleHours: 10, reviewHours: 1, size: 100),
            Pr(2, cycleHours: 20, reviewHours: 2, size: 200),
            Pr(3, cycleHours: 30, reviewHours: 3, size: 300),
        };

        var snapshot = MetricsCalculator.Calculate("o/r", prs, Now, Window);

        Assert.Equal(3, snapshot.MergedPullRequests);
        Assert.Equal(20, snapshot.MedianCycleTimeHours);
        Assert.Equal(28, snapshot.P90CycleTimeHours);
        Assert.Equal(2, snapshot.MedianHoursToFirstReview);
        Assert.Equal(2.8, snapshot.P90HoursToFirstReview);
        Assert.Equal(200, snapshot.MedianSize);
        Assert.Equal(1, snapshot.ReviewedShare);
    }

    [Fact]
    public void Unreviewed_pull_requests_count_towards_cycle_time_but_not_review_time()
    {
        var prs = new[]
        {
            Pr(1, cycleHours: 10, reviewHours: 4),
            Pr(2, cycleHours: 2, reviewHours: null),
        };

        var snapshot = MetricsCalculator.Calculate("o/r", prs, Now, Window);

        Assert.Equal(6, snapshot.MedianCycleTimeHours);
        Assert.Equal(4, snapshot.MedianHoursToFirstReview);
        Assert.Equal(0.5, snapshot.ReviewedShare);
    }

    [Fact]
    public void Only_review_free_pull_requests_gives_null_review_metrics()
    {
        var snapshot = MetricsCalculator.Calculate("o/r", [Pr(1, 5, null)], Now, Window);

        Assert.Null(snapshot.MedianHoursToFirstReview);
        Assert.Equal(0, snapshot.ReviewedShare);
    }

    [Fact]
    public void Ignores_pull_requests_merged_outside_the_window()
    {
        var prs = new[]
        {
            Pr(1, cycleHours: 10, reviewHours: 1, mergedDaysAgo: 5),
            Pr(2, cycleHours: 500, reviewHours: 1, mergedDaysAgo: 45),
        };

        var snapshot = MetricsCalculator.Calculate("o/r", prs, Now, Window);

        Assert.Equal(1, snapshot.MergedPullRequests);
        Assert.Equal(10, snapshot.MedianCycleTimeHours);
    }
}
