namespace EngineeringMetrics.Core;

public interface IMetricsStore
{
    /// <summary>Saves the snapshot to history and makes it the latest for its repository.</summary>
    Task SaveAsync(MetricsSnapshot snapshot, CancellationToken cancellationToken);
}

public interface IMetricsReader
{
    /// <summary>The most recent snapshot for every repository.</summary>
    Task<IReadOnlyList<MetricsSnapshot>> GetLatestAsync(CancellationToken cancellationToken);
}
