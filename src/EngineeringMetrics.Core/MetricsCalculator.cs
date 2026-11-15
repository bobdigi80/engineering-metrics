namespace EngineeringMetrics.Core;

public static class MetricsCalculator
{
    /// <summary>
    /// Calculates metrics for pull requests merged within <paramref name="window"/> before <paramref name="now"/>.
    /// Pull requests outside the window are ignored.
    /// </summary>
    public static MetricsSnapshot Calculate(
        string repository,
        IEnumerable<PullRequest> pullRequests,
        DateTimeOffset now,
        TimeSpan window)
    {
        var since = now - window;
        var merged = pullRequests.Where(pr => pr.MergedAt > since && pr.MergedAt <= now).ToList();
        var windowDays = (int)Math.Round(window.TotalDays);

        if (merged.Count == 0)
        {
            return new MetricsSnapshot(repository, now, windowDays, 0, null, null, null, null, null, null);
        }

        var reviewHours = merged
            .Where(pr => pr.TimeToFirstReview is not null)
            .Select(pr => pr.TimeToFirstReview!.Value.TotalHours)
            .ToList();
        var cycleHours = merged.Select(pr => pr.CycleTime.TotalHours).ToList();
        var sizes = merged.Select(pr => (double)pr.Size).ToList();

        return new MetricsSnapshot(
            repository,
            now,
            windowDays,
            merged.Count,
            MedianHoursToFirstReview: reviewHours.Count > 0 ? Round(Percentile.Of(reviewHours, 0.5)) : null,
            P90HoursToFirstReview: reviewHours.Count > 0 ? Round(Percentile.Of(reviewHours, 0.9)) : null,
            MedianCycleTimeHours: Round(Percentile.Of(cycleHours, 0.5)),
            P90CycleTimeHours: Round(Percentile.Of(cycleHours, 0.9)),
            MedianSize: (int)Math.Round(Percentile.Of(sizes, 0.5)),
            ReviewedShare: Math.Round((double)reviewHours.Count / merged.Count, 2));
    }

    private static double Round(double hours) => Math.Round(hours, 1);
}
