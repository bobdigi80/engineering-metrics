namespace EngineeringMetrics.Core;

public static class Percentile
{
    /// <summary>
    /// Percentile with linear interpolation between closest ranks (the same method as Excel's PERCENTILE.INC).
    /// </summary>
    /// <param name="values">Values in any order. Must not be empty.</param>
    /// <param name="p">Percentile between 0 and 1, e.g. 0.5 for the median.</param>
    public static double Of(IEnumerable<double> values, double p)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(p, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(p, 1);

        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        var rank = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
    }
}
