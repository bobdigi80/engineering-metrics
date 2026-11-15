namespace EngineeringMetrics.Core;

/// <summary>
/// Review and delivery metrics for one repository over a time window.
/// Durations are in hours so the JSON is easy to chart. Values are null when there is no data.
/// </summary>
public sealed record MetricsSnapshot(
    string Repository,
    DateTimeOffset GeneratedAt,
    int WindowDays,
    int MergedPullRequests,
    double? MedianHoursToFirstReview,
    double? P90HoursToFirstReview,
    double? MedianCycleTimeHours,
    double? P90CycleTimeHours,
    int? MedianSize,
    double? ReviewedShare);
