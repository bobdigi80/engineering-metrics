namespace EngineeringMetrics.Core;

/// <summary>A merged pull request, reduced to the fields the metrics need.</summary>
/// <param name="FirstReviewAt">When someone other than the author first submitted a review; null if nobody did.</param>
public sealed record PullRequest(
    int Number,
    string Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset MergedAt,
    DateTimeOffset? FirstReviewAt,
    int Additions,
    int Deletions)
{
    /// <summary>Lines changed: additions plus deletions.</summary>
    public int Size => Additions + Deletions;

    public TimeSpan CycleTime => MergedAt - CreatedAt;

    public TimeSpan? TimeToFirstReview => FirstReviewAt - CreatedAt;
}
