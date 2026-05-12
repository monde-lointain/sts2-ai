using Sts2Headless.Host;

namespace Sts2Headless.Tests.Host.Metrics;

/// <summary>
/// Tests for the additive histogram support required by the D5 schema
/// (<c>q1_gc_pause_microseconds</c> et al.). Histograms in Prometheus text
/// format render as a series of <c>_bucket{le="..."}</c> samples plus
/// <c>_sum</c> and <c>_count</c>.
/// </summary>
public sealed class HistogramTests
{
    [Fact]
    public void RegisterHistogram_then_observe_renders_buckets_count_sum()
    {
        var r = new PrometheusMetricsRegistry();
        double[] buckets = { 10, 50, 100, 500 };
        r.RegisterHistogram("q1_lat_microseconds", buckets, "Latency in µs.");
        r.ObserveHistogram("q1_lat_microseconds", 7);    // <= 10
        r.ObserveHistogram("q1_lat_microseconds", 25);   // <= 50
        r.ObserveHistogram("q1_lat_microseconds", 75);   // <= 100
        r.ObserveHistogram("q1_lat_microseconds", 1000); // +Inf only

        string s = r.RenderPrometheus();
        Assert.Contains("# TYPE q1_lat_microseconds histogram", s);
        // Cumulative buckets: le="10" has 1, le="50" has 2, le="100" has 3, le="500" has 3,
        // le="+Inf" has 4.
        Assert.Contains("q1_lat_microseconds_bucket{le=\"10\"} 1", s);
        Assert.Contains("q1_lat_microseconds_bucket{le=\"50\"} 2", s);
        Assert.Contains("q1_lat_microseconds_bucket{le=\"100\"} 3", s);
        Assert.Contains("q1_lat_microseconds_bucket{le=\"500\"} 3", s);
        Assert.Contains("q1_lat_microseconds_bucket{le=\"+Inf\"} 4", s);
        Assert.Contains("q1_lat_microseconds_count 4", s);
        Assert.Contains("q1_lat_microseconds_sum 1107", s);
    }

    [Fact]
    public void Histogram_renders_with_zero_observations()
    {
        var r = new PrometheusMetricsRegistry();
        r.RegisterHistogram("q1_lat_microseconds", new double[] { 10, 50 }, "Latency in µs.");
        string s = r.RenderPrometheus();
        Assert.Contains("q1_lat_microseconds_count 0", s);
        Assert.Contains("q1_lat_microseconds_sum 0", s);
        Assert.Contains("q1_lat_microseconds_bucket{le=\"+Inf\"} 0", s);
    }

    [Fact]
    public void Histogram_buckets_must_be_strictly_ascending()
    {
        var r = new PrometheusMetricsRegistry();
        Assert.Throws<ArgumentException>(
            () => r.RegisterHistogram("q1_bad", new double[] { 10, 10, 50 }, "Bad histogram."));
        Assert.Throws<ArgumentException>(
            () => r.RegisterHistogram("q1_bad", new double[] { 50, 10 }, "Bad histogram."));
        Assert.Throws<ArgumentException>(
            () => r.RegisterHistogram("q1_bad", Array.Empty<double>(), "Empty histogram."));
    }

    [Fact]
    public void Observe_on_unknown_histogram_throws()
    {
        var r = new PrometheusMetricsRegistry();
        Assert.Throws<InvalidOperationException>(() => r.ObserveHistogram("q1_unknown", 1));
    }
}
