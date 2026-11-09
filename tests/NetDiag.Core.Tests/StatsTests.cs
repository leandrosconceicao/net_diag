using NetDiag.Core.Net;

namespace NetDiag.Core.Tests;

public class StatsTests
{
    [Fact]
    public void Median_of_empty_sequence_is_null() =>
        Assert.Null(Stats.Median(Array.Empty<double>()));

    [Fact]
    public void Median_of_odd_count_is_the_middle_value() =>
        Assert.Equal<double?>(2, Stats.Median(new double[] { 3, 1, 2 }));

    [Fact]
    public void Median_of_even_count_averages_the_two_middle_values() =>
        Assert.Equal<double?>(2.5, Stats.Median(new double[] { 4, 1, 3, 2 }));
}

public class LatencyStatsTests
{
    [Fact]
    public void From_ignores_lost_samples_in_average_min_max_and_jitter()
    {
        var s = LatencyStats.From(new double?[] { 10.0, null, 20.0, 30.0 });

        Assert.Equal(4, s.Sent);
        Assert.Equal(3, s.Received);
        Assert.Equal(25.0, s.LossPercent, 6);
        Assert.Equal<double?>(20, s.Avg);
        Assert.Equal<double?>(10, s.Min);
        Assert.Equal<double?>(30, s.Max);
        Assert.Equal<double?>(20, s.Median);
        // Diferenças entre amostras consecutivas válidas: |20-10| e |30-20| → média 10.
        Assert.Equal<double?>(10, s.Jitter);
    }

    [Fact]
    public void From_with_only_losses_reports_all_lost()
    {
        var s = LatencyStats.From(new double?[] { null, null, null });

        Assert.True(s.AllLost);
        Assert.Equal(100.0, s.LossPercent, 6);
        Assert.Null(s.Avg);
        Assert.Null(s.Median);
        Assert.Null(s.Jitter);
    }

    [Fact]
    public void From_with_no_samples_counts_as_total_loss()
    {
        var s = LatencyStats.From(Array.Empty<double?>());

        Assert.Equal(0, s.Sent);
        Assert.Equal(100.0, s.LossPercent, 6);
        Assert.True(s.AllLost);
    }

    [Fact]
    public void Jitter_needs_at_least_two_valid_samples()
    {
        var s = LatencyStats.From(new double?[] { 15.0, null });

        Assert.Null(s.Jitter);
        Assert.Equal<double?>(15, s.Avg);
    }
}
