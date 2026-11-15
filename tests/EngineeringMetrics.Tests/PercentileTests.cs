using EngineeringMetrics.Core;

namespace EngineeringMetrics.Tests;

public class PercentileTests
{
    [Theory]
    [InlineData(new double[] { 5 }, 0.5, 5)]
    [InlineData(new double[] { 3, 1, 2 }, 0.5, 2)]
    [InlineData(new double[] { 1, 2, 3, 4 }, 0.5, 2.5)]
    [InlineData(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 0.9, 9.1)]
    [InlineData(new double[] { 1, 2, 3 }, 0, 1)]
    [InlineData(new double[] { 1, 2, 3 }, 1, 3)]
    public void Interpolates_between_closest_ranks(double[] values, double p, double expected) =>
        Assert.Equal(expected, Percentile.Of(values, p), precision: 10);

    [Fact]
    public void Empty_input_throws() =>
        Assert.Throws<ArgumentException>(() => Percentile.Of([], 0.5));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Out_of_range_percentile_throws(double p) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Percentile.Of([1], p));
}
